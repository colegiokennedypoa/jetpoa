using System.Globalization;
using System.Net;
using System.Text;
using Dapper;
using JetPoa.Api.Data;
using JetPoa.Api.Domain;
using JetPoa.Api.Endpoints;

namespace JetPoa.Api.Impressao;

/// <summary>
/// Posição das comandas para contingência: se o sistema ou o banco cair, o caixa abre o arquivo do dia
/// no navegador (ou imprime) e cobra cada cliente pelo que estava lançado.
/// Um arquivo por dia (posicao-AAAA-MM-DD.html), regravado a cada poucos minutos; o último retrato fica
/// mesmo que o banco pare. Fica fora da pasta do programa para não sumir nas atualizações.
/// </summary>
public sealed class PosicaoComandas(Db db, IConfiguration cfg, ILogger<PosicaoComandas> log) : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromMinutes(2);
    private const int DiasGuardados = 90;

    public string Pasta => cfg["Posicao:Pasta"] is { Length: > 0 } p ? p : @"C:\Projeto\JetPOA\posicao-comandas";

    public sealed record Aberta(long Id, int Numero, string? Cliente, string? Celular, string? Mesa, string EntradaTipo,
        string? Area, DateTime AbertaEm, Conferencia.Dados Conta);

    public sealed record Fechada(int Numero, string? Cliente, DateTime FechadaEm, decimal Total, string Formas);

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try { await GravarAsync(stop); }
            catch (Exception e) when (!stop.IsCancellationRequested) { log.LogError(e, "Falha ao gravar a posição das comandas"); }
            try { await Task.Delay(Intervalo, stop); }
            catch (OperationCanceledException) { break; }
        }
    }

    public async Task<string> GravarAsync(CancellationToken ct)
    {
        var agora = DateTime.Now;
        using var c = await db.OpenAsync(ct);

        var cabecalhos = await c.QueryAsync<(decimal Id, decimal Numero, string? Cliente, string? Celular, string? Mesa, string EntradaTipo, string? Area, DateTime AbertaEm)>("""
            select co.id, co.numero, nvl(cl.nome, co.identificacao) cliente, cl.celular, co.mesa, co.entrada_tipo, a.nome area, co.aberta_em
              from comanda co
              left join cliente cl on cl.id = co.cliente_id
              left join area a on a.id = co.area_id
             where co.situacao = 'A'
             order by co.numero
            """);
        var abertas = new List<Aberta>();
        foreach (var h in cabecalhos)
        {
            var (dados, _) = await FechamentoEndpoints.ConferenciaAsync(c, (long)h.Id);
            if (dados is not null)
                abertas.Add(new Aberta((long)h.Id, (int)h.Numero, h.Cliente, h.Celular, h.Mesa, h.EntradaTipo, h.Area, h.AbertaEm, dados));
        }

        var fechadas = (await c.QueryAsync<(decimal Id, decimal Numero, string? Cliente, DateTime FechadaEm, decimal? Total)>("""
            select co.id, co.numero, nvl(cl.nome, co.identificacao) cliente, co.fechada_em, co.valor_total
              from comanda co left join cliente cl on cl.id = co.cliente_id
             where co.situacao = 'F' and co.fechada_em >= trunc(sysdate)
             order by co.fechada_em
            """)).ToList();
        var pagamentos = (await c.QueryAsync<(decimal ComandaId, string Forma, decimal Valor)>("""
            select p.comanda_id, f.nome forma, p.valor
              from comanda_pagamento p
              join forma_pagamento f on f.id = p.forma_pagamento_id
              join comanda co on co.id = p.comanda_id
             where co.situacao = 'F' and co.fechada_em >= trunc(sysdate)
            """)).ToLookup(p => (long)p.ComandaId);
        var br = CultureInfo.GetCultureInfo("pt-BR");
        var lista = fechadas.Select(f => new Fechada((int)f.Numero, f.Cliente, f.FechadaEm, f.Total ?? 0,
            string.Join(", ", pagamentos[(long)f.Id].GroupBy(p => p.Forma).Select(g => $"{g.Key} {g.Sum(x => x.Valor).ToString("N2", br)}")))).ToList();

        Directory.CreateDirectory(Pasta);
        var arquivo = Path.Combine(Pasta, $"posicao-{agora:yyyy-MM-dd}.html");
        // grava num temporário e troca: quem abrir o arquivo no meio da gravação nunca vê página pela metade
        var temp = arquivo + ".tmp";
        await File.WriteAllTextAsync(temp, Html(agora, abertas, lista), Encoding.UTF8, ct);
        File.Move(temp, arquivo, overwrite: true);
        Limpar(agora);
        return arquivo;
    }

    private void Limpar(DateTime agora)
    {
        foreach (var f in Directory.EnumerateFiles(Pasta, "posicao-*.html"))
            if (File.GetLastWriteTime(f) < agora.AddDays(-DiasGuardados))
                try { File.Delete(f); } catch (IOException) { /* aberto em outro programa: tenta na próxima */ }
    }

    public static string Html(DateTime agora, IReadOnlyList<Aberta> abertas, IReadOnlyList<Fechada> fechadas)
    {
        var br = CultureInfo.GetCultureInfo("pt-BR");
        string H(string? s) => WebUtility.HtmlEncode(s ?? "");
        string R(decimal v) => v.ToString("N2", br);
        decimal APagar(Aberta a) => Math.Max(0, a.Conta.Calculo.Total - a.Conta.PagoPrevio);
        decimal Credito(Aberta a) => Math.Max(0, a.Conta.PagoPrevio - a.Conta.Calculo.Total);
        string Cel(string? v) => v is null ? "" : System.Text.RegularExpressions.Regex.Replace(v, @"^(\d{2})(\d{4,5})(\d{4})$", "($1) $2-$3");

        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"pt-BR\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        sb.Append($"<title>Posição das comandas {agora:dd/MM/yyyy}</title><style>");
        sb.Append("body{font-family:Segoe UI,Arial,sans-serif;font-size:13px;margin:16px;color:#111}h1{font-size:20px;margin:0}h2{font-size:16px;margin:20px 0 6px}");
        sb.Append(".aviso{background:#fff4d6;border:1px solid #e0b84a;padding:8px 10px;margin:8px 0}");
        sb.Append("table{border-collapse:collapse;width:100%}th,td{border:1px solid #bbb;padding:3px 6px;text-align:left;vertical-align:top}th{background:#eee}");
        sb.Append(".n{text-align:right;white-space:nowrap}.tot td{font-weight:bold;background:#f6f6f6}");
        sb.Append(".cmd{border:1px solid #888;padding:6px 10px;margin:8px 0;break-inside:avoid}.cmd h3{margin:0 0 4px;font-size:15px}.cmd table td,.cmd table th{border:0;border-bottom:1px dotted #ccc}");
        sb.Append("@media print{body{margin:0}.cmd{page-break-inside:avoid}}");
        sb.Append("</style></head><body>");
        sb.Append($"<h1>Posição das comandas · {agora:dd/MM/yyyy}</h1>");
        sb.Append($"<div class=aviso><b>Retrato de {agora:dd/MM/yyyy HH:mm}.</b> Atualizado sozinho a cada {(int)Intervalo.TotalMinutes} minutos enquanto o sistema funciona. ");
        sb.Append("Se o sistema parar, use esta página para cobrar: lançamentos feitos depois deste horário não aparecem aqui. Não é documento fiscal.</div>");

        // resumo das abertas
        sb.Append($"<h2>Comandas abertas: {abertas.Count} · a receber R$ {R(abertas.Sum(APagar))}</h2>");
        if (abertas.Count == 0) sb.Append("<p>Nenhuma comanda aberta.</p>");
        else
        {
            sb.Append("<table><tr><th>Nº</th><th>Cliente</th><th>Celular</th><th>Mesa</th><th>Entrada</th><th>Aberta</th><th class=n>Total</th><th class=n>Pago antes</th><th class=n>A pagar</th></tr>");
            foreach (var a in abertas)
                sb.Append($"<tr><td><b>{a.Numero}</b></td><td>{H(a.Cliente)}</td><td>{H(Cel(a.Celular))}</td><td>{H(a.Mesa)}</td>" +
                          $"<td>{(a.EntradaTipo == "R" ? "Rio" : "Terra")}{(a.Area is null ? "" : " · " + H(a.Area))}</td><td>{a.AbertaEm:dd/MM HH:mm}</td>" +
                          $"<td class=n>{R(a.Conta.Calculo.Total)}</td><td class=n>{(a.Conta.PagoPrevio > 0 ? R(a.Conta.PagoPrevio) : "")}</td><td class=n><b>{(Credito(a) > 0 ? "crédito " + R(Credito(a)) : R(APagar(a)))}</b></td></tr>");
            sb.Append($"<tr class=tot><td colspan=6>Total</td><td class=n>{R(abertas.Sum(a => a.Conta.Calculo.Total))}</td><td class=n>{R(abertas.Sum(a => a.Conta.PagoPrevio))}</td><td class=n>{R(abertas.Sum(APagar))}</td></tr></table>");

            // detalhe de cada comanda: o que foi consumido
            sb.Append("<h2>Itens de cada comanda aberta</h2>");
            foreach (var a in abertas)
            {
                var k = a.Conta.Calculo;
                sb.Append($"<div class=cmd><h3>Comanda {a.Numero} · {H(a.Cliente)}{(string.IsNullOrWhiteSpace(a.Mesa) ? "" : " · mesa " + H(a.Mesa))}</h3><table>");
                foreach (var g in a.Conta.Itens.GroupBy(i => (i.Produto, i.PrecoUnitario)).OrderBy(g => g.Key.Produto, StringComparer.CurrentCultureIgnoreCase))
                {
                    var q = g.Sum(i => i.Quantidade);
                    sb.Append($"<tr><td>{q.ToString("0.###", br)} x {H(g.Key.Produto)}</td><td class=n>un. {R(g.Key.PrecoUnitario)}</td><td class=n>{R(Math.Round(q * g.Key.PrecoUnitario, 2))}</td></tr>");
                }
                if (a.Conta.Itens.Count == 0) sb.Append("<tr><td colspan=3>Nenhum item lançado.</td></tr>");
                sb.Append($"<tr><td colspan=2>Subtotal</td><td class=n>{R(k.Subtotal)}</td></tr>");
                foreach (var d in k.Descontos.Where(d => d.Valor > 0))
                    sb.Append($"<tr><td colspan=2>{H(d.Descricao)}</td><td class=n>-{R(d.Valor)}</td></tr>");
                if (k.TaxaServico > 0) sb.Append($"<tr><td colspan=2>Taxa de serviço {k.TaxaServicoPct.ToString("0.##", br)}%</td><td class=n>{R(k.TaxaServico)}</td></tr>");
                if (a.Conta.PagoPrevio > 0) sb.Append($"<tr><td colspan=2>Pago antecipado</td><td class=n>-{R(a.Conta.PagoPrevio)}</td></tr>");
                sb.Append(Credito(a) > 0
                    ? $"<tr class=tot><td colspan=2>CRÉDITO A DEVOLVER (pagou antes mais que o consumo)</td><td class=n>{R(Credito(a))}</td></tr></table></div>"
                    : $"<tr class=tot><td colspan=2>A PAGAR</td><td class=n>{R(APagar(a))}</td></tr></table></div>");
            }
        }

        // fechadas no dia: para conferir o caixa
        sb.Append($"<h2>Fechadas hoje: {fechadas.Count} · R$ {R(fechadas.Sum(f => f.Total))}</h2>");
        if (fechadas.Count == 0) sb.Append("<p>Nenhuma comanda fechada hoje.</p>");
        else
        {
            sb.Append("<table><tr><th>Nº</th><th>Cliente</th><th>Fechada</th><th class=n>Total</th><th>Pagamentos</th></tr>");
            foreach (var f in fechadas)
                sb.Append($"<tr><td>{f.Numero}</td><td>{H(f.Cliente)}</td><td>{f.FechadaEm:HH:mm}</td><td class=n>{R(f.Total)}</td><td>{H(f.Formas)}</td></tr>");
            sb.Append("</table>");
        }
        sb.Append("</body></html>");
        return sb.ToString();
    }
}
