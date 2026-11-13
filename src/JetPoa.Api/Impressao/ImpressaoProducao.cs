using System.Data;
using Dapper;
using JetPoa.Api.Data;

namespace JetPoa.Api.Impressao;

/// <summary>
/// Fila de impressão de produção. Cada linha de ENVIO_PRODUCAO com situação P (pendente)
/// vira um ticket ESC/POS enviado ao IP do setor. Falhas ficam em E com nova tentativa
/// automática (até 5) e podem ser reenviadas pela tela de Produção.
/// </summary>
public sealed class ImpressaoProducao(Db db, ILogger<ImpressaoProducao> log) : BackgroundService
{
    public const int MaxTentativas = 5;
    private readonly SemaphoreSlim _sinal = new(0);

    /// <summary>Acorda o serviço imediatamente (chamado após gravar um envio).</summary>
    public void Notificar()
    {
        if (_sinal.CurrentCount == 0) _sinal.Release();
    }

    private sealed class EnvioPendente
    {
        public long Id { get; set; }
        public string Tipo { get; set; } = "P";
        public int Tentativas { get; set; }
        public long? EnvioOrigemId { get; set; }
        public string SetorNome { get; set; } = "";
        public string Imprime { get; set; } = "S";
        public string? ImpressoraHost { get; set; }
        public int? ImpressoraPorta { get; set; }
        public int ColunasImpressao { get; set; } = 48;
        public int Numero { get; set; }
        public string? Mesa { get; set; }
        public string? Identificacao { get; set; }
        public string EntradaTipo { get; set; } = "T";
        public string? Area { get; set; }
        public DateTime CriadoEm { get; set; }
    }

    private sealed class ItemTicket
    {
        public decimal Quantidade { get; set; }
        public string Produto { get; set; } = "";
        public string? Observacao { get; set; }
        public string? Garcom { get; set; }
        public string? MotivoCancelamento { get; set; }
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        log.LogInformation("Serviço de impressão de produção iniciado.");
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await ProcessarFilaAsync(stop);
            }
            catch (Exception e) when (!stop.IsCancellationRequested)
            {
                log.LogError(e, "Falha ao processar fila de impressão");
            }
            try { await _sinal.WaitAsync(TimeSpan.FromSeconds(10), stop); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task ProcessarFilaAsync(CancellationToken ct)
    {
        using var c = await db.OpenAsync(ct);
        // pendentes + erros ainda dentro do limite, com espera crescente entre tentativas (20s * tentativas)
        var pendentes = (await c.QueryAsync<EnvioPendente>("""
            select e.id, e.tipo, e.tentativas, e.envio_origem_id, e.criado_em, e.mesa,
                   s.nome setor_nome, s.imprime, s.impressora_host, s.impressora_porta, s.colunas_impressao,
                   co.numero, nvl(cl.nome, co.identificacao) identificacao, co.entrada_tipo, a.nome area
              from envio_producao e
              join setor_producao s on s.id = e.setor_producao_id
              join comanda co on co.id = e.comanda_id
              left join cliente cl on cl.id = co.cliente_id
              left join area a on a.id = co.area_id
             where e.situacao = 'P'
                or (e.situacao = 'E' and e.tentativas < :max
                    and sysdate > e.criado_em + (e.tentativas * 20) / 86400)
             order by e.id
            """, new { max = MaxTentativas })).ToList();

        foreach (var env in pendentes)
        {
            if (ct.IsCancellationRequested) return;
            await ImprimirEnvioAsync(c, env, ct);
        }
    }

    private async Task ImprimirEnvioAsync(IDbConnection c, EnvioPendente env, CancellationToken ct)
    {
        try
        {
            if (env.Imprime != "S" || string.IsNullOrWhiteSpace(env.ImpressoraHost))
                throw new IOException($"Setor {env.SetorNome} sem impressora configurada.");

            // itens do ticket: P = itens do envio; C = itens cancelados neste envio; R = itens do envio original
            var sqlItens = """
                select i.quantidade, p.nome produto, i.observacao, g.nome garcom, i.motivo_cancelamento
                  from comanda_item i
                  join produto p on p.id = i.produto_id
                  left join garcom g on g.id = i.garcom_id
                 where
                """ + " " + env.Tipo switch
            {
                "C" => "i.envio_cancelamento_id = :id",
                "R" => "i.envio_id = :origem",
                _ => "i.envio_id = :id"
            } + " order by i.id";
            var itens = (await c.QueryAsync<ItemTicket>(sqlItens, new { id = env.Id, origem = env.EnvioOrigemId })).ToList();

            var removerAcentos = (await c.ExecuteScalarAsync<string>(
                "select valor from parametro where chave = 'IMPRESSAO_REMOVER_ACENTOS'")) != "N";

            var bytes = MontarTicket(env, itens, removerAcentos);
            await ImpressoraRede.EnviarAsync(env.ImpressoraHost!, env.ImpressoraPorta ?? 9100, bytes, ct);

            await c.ExecuteAsync("""
                update envio_producao
                   set situacao = 'I', impresso_em = sysdate, tentativas = tentativas + 1, ultimo_erro = null
                 where id = :id
                """, new { id = env.Id });
            log.LogInformation("Envio {Id} impresso em {Setor}", env.Id, env.SetorNome);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            var msg = e.Message.Length > 1000 ? e.Message[..1000] : e.Message;
            await c.ExecuteAsync("""
                update envio_producao
                   set situacao = 'E', tentativas = tentativas + 1, ultimo_erro = :msg
                 where id = :id
                """, new { id = env.Id, msg });
            log.LogWarning("Envio {Id} ({Setor}) falhou: {Erro}", env.Id, env.SetorNome, msg);
        }
    }

    private static byte[] MontarTicket(EnvioPendente env, List<ItemTicket> itens, bool removerAcentos)
    {
        var t = new EscPos(env.ColunasImpressao, removerAcentos);
        var titulo = env.Tipo switch { "C" => "*** CANCELAMENTO ***", "R" => "REIMPRESSAO", _ => env.SetorNome.ToUpperInvariant() };

        t.Centro().Negrito(true).Tamanho(0x11).Linha(titulo).Tamanho(0x00);
        if (env.Tipo != "P") t.Linha(env.SetorNome.ToUpperInvariant());
        t.Negrito(false).Linha();

        // mesa em destaque: é onde o pedido deve ser entregue
        if (!string.IsNullOrWhiteSpace(env.Mesa))
        {
            t.Centro().Negrito(true).Tamanho(0x11);
            foreach (var l in EscPos.Quebrar("MESA " + env.Mesa.Trim().ToUpperInvariant(), t.Colunas / 2)) t.Linha(l);
            t.Tamanho(0x00).Negrito(false).Linha();
        }
        t.Esquerda().Negrito(true).Tamanho(0x11).Linha($"COMANDA {env.Numero}").Tamanho(0x00).Negrito(false);
        if (!string.IsNullOrWhiteSpace(env.Identificacao)) t.Linha(env.Identificacao.Trim());
        var local = (env.EntradaTipo == "R" ? "RIO" : "TERRA") + (env.Area is null ? "" : $" - {env.Area}");
        t.Linha(local);
        var garcons = itens.Select(i => i.Garcom).Where(g => g is not null).Distinct().ToList();
        if (garcons.Count > 0) t.Linha("Garcom: " + string.Join(", ", garcons));
        t.Linha($"{env.CriadoEm:dd/MM HH:mm}   Envio #{env.Id}");
        t.Separador();

        foreach (var i in itens)
        {
            var qtd = i.Quantidade % 1 == 0 ? ((int)i.Quantidade).ToString() : i.Quantidade.ToString("0.###");
            var prefixo = $"{qtd}x ";
            // item em fonte dupla (24 col em 80mm)
            t.Negrito(true).Tamanho(0x11);
            var largura = Math.Max(10, t.Colunas / 2 - prefixo.Length);
            var linhas = EscPos.Quebrar(i.Produto, largura).ToList();
            for (var k = 0; k < linhas.Count; k++)
                t.Linha((k == 0 ? prefixo : new string(' ', prefixo.Length)) + linhas[k]);
            t.Tamanho(0x00).Negrito(false);
            if (!string.IsNullOrWhiteSpace(i.Observacao))
                foreach (var l in EscPos.Quebrar("OBS: " + i.Observacao.Trim(), t.Colunas - 3)) t.Linha("   " + l);
            if (env.Tipo == "C" && !string.IsNullOrWhiteSpace(i.MotivoCancelamento))
                foreach (var l in EscPos.Quebrar("MOTIVO: " + i.MotivoCancelamento.Trim(), t.Colunas - 3)) t.Linha("   " + l);
            t.Linha();
        }

        t.Separador().Avancar(4).Cortar();
        return t.ToArray();
    }

    /// <summary>Ticket de teste para um setor (usado na tela de Produção).</summary>
    public static byte[] TicketTeste(string setor, int colunas, bool removerAcentos)
    {
        var t = new EscPos(colunas, removerAcentos);
        t.Centro().Negrito(true).Tamanho(0x11).Linha("TESTE").Tamanho(0x00).Linha(setor.ToUpperInvariant()).Negrito(false)
         .Linha($"{DateTime.Now:dd/MM/yyyy HH:mm:ss}").Linha("Acentuação: ÁÉÍÓÚ ãõ ç").Esquerda().Separador()
         .Linha("JetPOA - novo sistema do bistro").Avancar(4).Cortar();
        return t.ToArray();
    }
}
