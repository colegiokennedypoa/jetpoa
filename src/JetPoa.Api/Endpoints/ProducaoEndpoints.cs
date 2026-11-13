using System.Security.Claims;
using JetPoa.Api.Auth;
using Dapper;
using JetPoa.Api.Data;
using JetPoa.Api.Impressao;

namespace JetPoa.Api.Endpoints;

public static class ProducaoEndpoints
{
    public sealed class Envio
    {
        public long Id { get; set; }
        public long ComandaId { get; set; }
        public int Numero { get; set; }
        public string? Mesa { get; set; }
        public string Setor { get; set; } = "";
        public string Tipo { get; set; } = "P";
        public string Situacao { get; set; } = "P";
        public int Tentativas { get; set; }
        public string? UltimoErro { get; set; }
        public DateTime CriadoEm { get; set; }
        public DateTime? ImpressoEm { get; set; }
        public int QtdItens { get; set; }
    }

    private sealed class SetorTeste
    {
        public string Nome { get; set; } = "";
        public string? Host { get; set; }
        public int? Porta { get; set; }
        public int Colunas { get; set; } = 48;
    }

    public sealed record SetorInput(string? ImpressoraHost, int? ImpressoraPorta, bool Imprime, int? ColunasImpressao);

    public static void MapProducao(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/producao").WithTags("Produção").RequireAuthorization(Permissoes.ProducaoVer);

        // Envios recentes (últimas 12h) ou apenas os com erro
        g.MapGet("/envios", async (Db db, bool? somenteErros, CancellationToken ct) =>
        {
            using var c = await db.OpenAsync(ct);
            return await c.QueryAsync<Envio>("""
                select e.id, e.comanda_id, co.numero, e.mesa, s.nome setor, e.tipo, e.situacao, e.tentativas,
                       e.ultimo_erro, e.criado_em, e.impresso_em,
                       (select count(*) from comanda_item i
                         where (e.tipo = 'C' and i.envio_cancelamento_id = e.id)
                            or (e.tipo = 'P' and i.envio_id = e.id)
                            or (e.tipo = 'R' and i.envio_id = e.envio_origem_id)) qtd_itens
                  from envio_producao e
                  join comanda co on co.id = e.comanda_id
                  join setor_producao s on s.id = e.setor_producao_id
                 where (:erros = 1 and e.situacao <> 'I')
                    or (:erros = 0 and e.criado_em > sysdate - 0.5)
                 order by e.id desc
                """, new { erros = somenteErros == true ? 1 : 0 });
        });

        // Reimprime: cria novo envio tipo R apontando para o original
        g.MapPost("/envios/{id:long}/reimprimir", async (Db db, ImpressaoProducao impressao, long id, CancellationToken ct) =>
        {
            using var c = await db.OpenAsync(ct);
            var n = await c.ExecuteAsync("""
                insert into envio_producao (comanda_id, setor_producao_id, tipo, envio_origem_id, mesa)
                select comanda_id, setor_producao_id, 'R', case when tipo = 'R' then envio_origem_id else id end, mesa
                  from envio_producao where id = :id and tipo <> 'C'
                """, new { id });
            if (n == 0) return Results.NotFound(new { erro = "Envio não encontrado (cancelamentos não são reimpressos)." });
            impressao.Notificar();
            return Results.Accepted();
        });

        // Tenta de novo um envio com erro (zera tentativas)
        g.MapPost("/envios/{id:long}/tentar-novamente", async (Db db, ImpressaoProducao impressao, long id, CancellationToken ct) =>
        {
            using var c = await db.OpenAsync(ct);
            var n = await c.ExecuteAsync(
                "update envio_producao set situacao = 'P', tentativas = 0 where id = :id and situacao = 'E'", new { id });
            if (n == 0) return Results.NotFound();
            impressao.Notificar();
            return Results.Accepted();
        });

        var s = app.MapGroup("/api/setores").WithTags("Produção").RequireAuthorization(Permissoes.ProducaoConfigurar);

        s.MapPut("/{id:long}", async (Db db, long id, SetorInput i, CancellationToken ct) =>
        {
            var host = string.IsNullOrWhiteSpace(i.ImpressoraHost) ? null : i.ImpressoraHost.Trim();
            if (i.Imprime && host is null)
                return Results.UnprocessableEntity(new { erro = "Informe o IP da impressora." });
            if (i.ImpressoraPorta is < 1 or > 65535)
                return Results.UnprocessableEntity(new { erro = "Porta inválida." });
            using var c = await db.OpenAsync(ct);
            var n = await c.ExecuteAsync("""
                update setor_producao
                   set impressora_host = :host, impressora_porta = nvl(:porta, 9100),
                       imprime = :imprime, colunas_impressao = nvl(:colunas, colunas_impressao)
                 where id = :id
                """, new { id, host, porta = i.ImpressoraPorta, imprime = i.Imprime ? "S" : "N", colunas = i.ColunasImpressao });
            return n == 0 ? Results.NotFound() : Results.NoContent();
        });

        s.MapPost("/{id:long}/teste", async (Db db, long id, CancellationToken ct) =>
        {
            using var c = await db.OpenAsync(ct);
            var setor = await c.QuerySingleOrDefaultAsync<SetorTeste>(
                "select nome, impressora_host host, impressora_porta porta, colunas_impressao colunas from setor_producao where id = :id", new { id });
            if (setor is null) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(setor.Host))
                return Results.UnprocessableEntity(new { erro = "Setor sem impressora configurada." });
            var remover = (await c.ExecuteScalarAsync<string>(
                "select valor from parametro where chave = 'IMPRESSAO_REMOVER_ACENTOS'")) != "N";
            try
            {
                await ImpressoraRede.EnviarAsync(setor.Host, setor.Porta ?? 9100,
                    ImpressaoProducao.TicketTeste(setor.Nome, setor.Colunas, remover), ct);
                return Results.Ok(new { ok = true, mensagem = $"Teste enviado para {setor.Host}:{setor.Porta ?? 9100}." });
            }
            catch (IOException e)
            {
                return Results.UnprocessableEntity(new { erro = e.Message });
            }
        });
    }
}
