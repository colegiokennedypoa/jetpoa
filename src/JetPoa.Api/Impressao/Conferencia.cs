using System.Globalization;
using System.Net;
using System.Text;
using JetPoa.Api.Domain;

namespace JetPoa.Api.Impressao;

/// <summary>
/// Conferência de consumo (pré-conta): o cliente confere os itens antes de pagar.
/// Não é documento fiscal. Usa o mesmo cálculo do fechamento, sem descontos manuais.
/// Página impressa pelo navegador do caixa, na impressora padrão da máquina.
/// </summary>
public static class Conferencia
{
    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");

    public sealed record Dados(int Numero, string? Cliente, string? Mesa, DateTime AbertaEm,
        IReadOnlyList<Fechamento.Item> Itens, Fechamento.Resultado Calculo, DateTime Agora);

    private sealed record Grupo(string Produto, decimal PrecoUnitario, decimal Qtd)
    {
        public decimal Total => Math.Round(Qtd * PrecoUnitario, 2);
        public string QtdTexto => Qtd % 1 == 0 ? ((int)Qtd).ToString(Br) : Qtd.ToString("0.###", Br);
    }

    /// <summary>Itens agrupados por produto e preço: mais fácil de conferir.</summary>
    private static List<Grupo> Agrupar(IEnumerable<Fechamento.Item> itens) =>
        itens.GroupBy(i => (i.ProdutoId, i.Produto, i.PrecoUnitario))
             .Select(g => new Grupo(g.Key.Produto, g.Key.PrecoUnitario, g.Sum(x => x.Quantidade)))
             .OrderBy(g => g.Produto, StringComparer.CurrentCultureIgnoreCase)
             .ToList();

    /// <summary>Linhas de totais (rótulo, valor), na ordem do ticket. A última é o total.</summary>
    private static List<(string Rotulo, string Valor)> Totais(Fechamento.Resultado c)
    {
        string R(decimal v) => v.ToString("N2", Br);
        var l = new List<(string, string)> { ("Subtotal", R(c.Subtotal)) };
        l.AddRange(c.Descontos.Where(x => x.Valor > 0).Select(x => (x.Descricao, "-" + R(x.Valor))));
        if (c.TaxaServico > 0) l.Add(($"Taxa de serviço {c.TaxaServicoPct.ToString("0.##", Br)}%", R(c.TaxaServico)));
        l.Add(("TOTAL R$", R(c.Total)));
        return l;
    }

    /// <summary>Página da conferência (o front imprime por um iframe oculto).</summary>
    public static string Html(Dados d)
    {
        string H(string? s) => WebUtility.HtmlEncode(s ?? "");
        string R(decimal v) => v.ToString("N2", Br);
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"pt-BR\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        sb.Append($"<title>Conferência comanda {d.Numero}</title><style>");
        sb.Append("body{font-family:Consolas,'Liberation Mono',Menlo,monospace;font-size:13px;background:#eee;margin:0;padding:16px;color:#111}");
        sb.Append(".cupom{background:#fff;max-width:380px;margin:0 auto;padding:14px 16px;box-shadow:0 1px 4px #0002}");
        sb.Append(".c{text-align:center}.b{font-weight:bold}.g{font-size:20px;font-weight:bold}hr{border:0;border-top:1px dashed #555}");
        sb.Append("table{width:100%;border-collapse:collapse}td{padding:1px 0;vertical-align:top}.n{text-align:right;white-space:nowrap;padding-left:8px}");
        sb.Append(".un{color:#555;padding-left:1.5em}.total td{font-size:17px;font-weight:bold;padding-top:4px}");
        sb.Append("@media print{body{background:#fff;padding:0}.cupom{box-shadow:none;max-width:none}.np{display:none}}");
        sb.Append("</style></head><body><div class=\"cupom\">");
        sb.Append("<div class=\"c g\">CONFERÊNCIA</div><div class=\"c b\">NÃO É DOCUMENTO FISCAL</div><hr>");
        sb.Append($"<div class=g>COMANDA {d.Numero}</div>");
        if (!string.IsNullOrWhiteSpace(d.Cliente)) sb.Append($"<div>{H(d.Cliente.Trim())}</div>");
        if (!string.IsNullOrWhiteSpace(d.Mesa)) sb.Append($"<div>Mesa {H(d.Mesa.Trim())}</div>");
        sb.Append($"<div>Aberta {d.AbertaEm:dd/MM HH:mm} · Impressa {d.Agora:dd/MM HH:mm}</div><hr><table>");
        foreach (var g in Agrupar(d.Itens))
        {
            sb.Append($"<tr><td>{g.QtdTexto}x {H(g.Produto)}</td><td class=n>{R(g.Total)}</td></tr>");
            if (g.Qtd != 1) sb.Append($"<tr><td class=un colspan=2>un. {R(g.PrecoUnitario)}</td></tr>");
        }
        if (d.Itens.Count == 0) sb.Append("<tr><td>Nenhum item lançado.</td></tr>");
        sb.Append("</table><hr><table>");
        var totais = Totais(d.Calculo);
        for (var k = 0; k < totais.Count; k++)
            sb.Append($"<tr{(k == totais.Count - 1 ? " class=total" : "")}><td>{H(totais[k].Rotulo)}</td><td class=n>{H(totais[k].Valor)}</td></tr>");
        sb.Append("</table><hr><div class=c>Confira os itens e informe qualquer<br>divergência ao garçom ou ao caixa.</div>");
        sb.Append("<p class=\"c np\"><button onclick=\"print()\">Imprimir</button></p></div>");
        sb.Append("</body></html>");
        return sb.ToString();
    }
}
