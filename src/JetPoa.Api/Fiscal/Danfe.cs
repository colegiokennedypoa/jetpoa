using System.Globalization;
using System.Net;
using System.Text;
using System.Xml.Linq;
using JetPoa.Api.Impressao;
using QRCoder;

namespace JetPoa.Api.Fiscal;

/// <summary>DANFE NFC-e: ticket ESC/POS (impressora do caixa) e página HTML (tela/reimpressão).</summary>
public static class Danfe
{
    private static readonly XNamespace Ns = NfceXml.Ns;
    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");

    public sealed record Dados(
        string Emitente, string Cnpj, string Ie, string Endereco, int Numero, int Serie, DateTime Emissao, string Chave,
        string? Protocolo, DateTime? AutorizadaEm, bool Contingencia, bool Homologacao, string? Consumidor,
        List<(string Cod, string Desc, decimal Qtd, string Un, decimal Unit, decimal Total)> Itens,
        decimal VProd, decimal VDesc, decimal VNF, List<(string Forma, decimal Valor)> Pagamentos,
        string QrCode, string UrlChave, string Tributos);

    private static readonly Dictionary<string, string> Formas = new()
    {
        ["01"] = "Dinheiro", ["03"] = "Cartão de Crédito", ["04"] = "Cartão de Débito", ["05"] = "Crédito Loja",
        ["17"] = "PIX", ["99"] = "Outros",
    };

    public static Dados Ler(string xml, string? protocolo, DateTime? autorizadaEm)
    {
        var doc = XElement.Parse(xml);
        var nfe = doc.Name.LocalName == "NFe" ? doc : doc.Descendants(Ns + "NFe").First();
        var inf = nfe.Element(Ns + "infNFe")!;
        var ide = inf.Element(Ns + "ide")!;
        var emit = inf.Element(Ns + "emit")!;
        var end = emit.Element(Ns + "enderEmit")!;
        var tot = inf.Descendants(Ns + "ICMSTot").First();
        var dest = inf.Element(Ns + "dest");
        string V(XElement e, string n) => e.Element(Ns + n)?.Value ?? "";
        decimal D(XElement e, string n) => decimal.Parse(V(e, n) is "" ? "0" : V(e, n), CultureInfo.InvariantCulture);

        var itens = inf.Elements(Ns + "det").Select(d => d.Element(Ns + "prod")!)
            .Select(p => (V(p, "cProd"), V(p, "xProd"), D(p, "qCom"), V(p, "uCom"), D(p, "vUnCom"), D(p, "vProd"))).ToList();
        var pags = inf.Descendants(Ns + "detPag").Select(p => (Formas.GetValueOrDefault(V(p, "tPag"), "Outros"), D(p, "vPag"))).ToList();
        var consumidor = dest is null ? null
            : dest.Element(Ns + "CPF") is { } cpf ? "CPF " + Mascara(cpf.Value, "###.###.###-##")
            : "CNPJ " + Mascara(V(dest, "CNPJ"), "##.###.###/####-##");

        return new Dados(
            V(emit, "xNome"), V(emit, "CNPJ"), V(emit, "IE"),
            $"{V(end, "xLgr")}, {V(end, "nro")} - {V(end, "xBairro")} - {V(end, "xMun")}/{V(end, "UF")}",
            int.Parse(V(ide, "nNF")), int.Parse(V(ide, "serie")),
            DateTime.Parse(V(ide, "dhEmi")[..19], CultureInfo.InvariantCulture),
            inf.Attribute("Id")!.Value[3..], protocolo, autorizadaEm, V(ide, "tpEmis") == "9", V(ide, "tpAmb") == "2",
            consumidor, itens, D(tot, "vProd"), D(tot, "vDesc"), D(tot, "vNF"), pags,
            nfe.Descendants(Ns + "qrCode").First().Value, nfe.Descendants(Ns + "urlChave").First().Value,
            inf.Descendants(Ns + "infCpl").FirstOrDefault()?.Value ?? "");
    }

    // ------------------------------------------------------------------ ESC/POS

    public static byte[] EscPos(Dados d, int colunas = 48)
    {
        var t = new EscPos(colunas, removerAcentos: true);
        string R(decimal v) => v.ToString("N2", Br);

        t.Centro().Negrito(true).Linha(d.Emitente).Negrito(false)
         .Linha($"CNPJ {Mascara(d.Cnpj, "##.###.###/####-##")}  IE {d.Ie}");
        foreach (var l in Impressao.EscPos.Quebrar(d.Endereco, colunas)) t.Linha(l);
        t.Separador().Negrito(true).Linha("DANFE NFC-e - Documento Auxiliar").Linha("da Nota Fiscal de Consumidor Eletronica").Negrito(false);
        if (d.Homologacao) t.Negrito(true).Linha("EMITIDA EM AMBIENTE DE HOMOLOGACAO").Linha("SEM VALOR FISCAL").Negrito(false);
        t.Separador().Esquerda();

        foreach (var i in d.Itens)
        {
            foreach (var l in Impressao.EscPos.Quebrar($"{i.Cod} {i.Desc}", colunas)) t.Linha(l);
            var linha = $"{i.Qtd.ToString("0.###", Br)} {i.Un} x {R(i.Unit)}";
            t.Linha(linha + R(i.Total).PadLeft(colunas - linha.Length));
        }
        t.Separador();
        Par(t, "Qtd. total de itens", d.Itens.Count.ToString(), colunas);
        Par(t, "Valor total R$", R(d.VProd), colunas);
        if (d.VDesc > 0) Par(t, "Descontos R$", R(d.VDesc), colunas);
        t.Negrito(true); Par(t, "Valor a pagar R$", R(d.VNF), colunas); t.Negrito(false);
        Par(t, "FORMA PAGAMENTO", "VALOR PAGO R$", colunas);
        foreach (var p in d.Pagamentos) Par(t, p.Forma, R(p.Valor), colunas);
        t.Separador().Centro();

        if (d.Contingencia) t.Negrito(true).Linha("EMITIDA EM CONTINGENCIA").Linha("Pendente de autorizacao").Negrito(false);
        t.Linha("Consulte pela Chave de Acesso em").Linha(d.UrlChave).Linha(Chave4(d.Chave));
        t.Linha(d.Consumidor is null ? "CONSUMIDOR NAO IDENTIFICADO" : "CONSUMIDOR " + d.Consumidor);
        t.Negrito(true).Linha($"NFC-e n {d.Numero:000000000} Serie {d.Serie:000} {d.Emissao:dd/MM/yyyy HH:mm:ss}").Negrito(false);
        if (d.Protocolo is not null) t.Linha($"Protocolo de autorizacao: {d.Protocolo}").Linha($"Data de autorizacao: {d.AutorizadaEm:dd/MM/yyyy HH:mm:ss}");

        QrCode(t, d.QrCode);
        t.Esquerda();
        foreach (var l in Impressao.EscPos.Quebrar(d.Tributos, colunas)) t.Linha(l);
        t.Avancar(4).Cortar();
        return t.ToArray();
    }

    private static void Par(EscPos t, string esq, string dir, int col) =>
        t.Linha(esq.Length + dir.Length + 1 > col ? esq[..Math.Max(0, col - dir.Length - 1)] + " " + dir : esq + dir.PadLeft(col - esq.Length));

    /// <summary>QR Code nativo ESC/POS (GS ( k, modelo 2, correção M).</summary>
    private static void QrCode(EscPos t, string conteudo)
    {
        var dados = Encoding.ASCII.GetBytes(conteudo);
        var len = dados.Length + 3;
        t.Raw(0x1D, 0x28, 0x6B, 0x04, 0x00, 0x31, 0x41, 0x32, 0x00);              // modelo 2
        t.Raw(0x1D, 0x28, 0x6B, 0x03, 0x00, 0x31, 0x43, 0x05);                    // tamanho do módulo
        t.Raw(0x1D, 0x28, 0x6B, 0x03, 0x00, 0x31, 0x45, 0x31);                    // correção M
        t.Raw(0x1D, 0x28, 0x6B, (byte)(len % 256), (byte)(len / 256), 0x31, 0x50, 0x30);
        t.Raw(dados);
        t.Raw(0x1D, 0x28, 0x6B, 0x03, 0x00, 0x31, 0x51, 0x30);                    // imprime
        t.Linha();
    }

    // ------------------------------------------------------------------ HTML

    public static string Html(Dados d)
    {
        string H(string s) => WebUtility.HtmlEncode(s);
        string R(decimal v) => v.ToString("N2", Br);
        using var gen = new QRCodeGenerator();
        var svg = new SvgQRCode(gen.CreateQrCode(d.QrCode, QRCodeGenerator.ECCLevel.M)).GetGraphic(4);

        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"pt-BR\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        sb.Append($"<title>NFC-e {d.Numero}</title><style>");
        sb.Append("body{font-family:Consolas,'Liberation Mono',Menlo,monospace;font-size:13px;background:#eee;margin:0;padding:16px;color:#111}");
        sb.Append(".cupom{background:#fff;max-width:380px;margin:0 auto;padding:14px 16px;box-shadow:0 1px 4px #0002}");
        sb.Append(".c{text-align:center}.b{font-weight:bold}hr{border:0;border-top:1px dashed #555}table{width:100%;border-collapse:collapse}");
        sb.Append("td{padding:1px 0;vertical-align:top}.n{text-align:right}.alerta{background:#fde68a;padding:4px;font-weight:bold}");
        sb.Append(".qr svg{width:180px;height:180px}.chave{word-spacing:2px}@media print{body{background:#fff;padding:0}.cupom{box-shadow:none}.np{display:none}}");
        sb.Append("</style></head><body><div class=\"cupom\">");
        sb.Append($"<div class=\"c b\">{H(d.Emitente)}</div><div class=\"c\">CNPJ {Mascara(d.Cnpj, "##.###.###/####-##")} IE {H(d.Ie)}<br>{H(d.Endereco)}</div><hr>");
        sb.Append("<div class=\"c b\">DANFE NFC-e - Documento Auxiliar da Nota Fiscal de Consumidor Eletrônica</div>");
        if (d.Homologacao) sb.Append("<div class=\"c alerta\">EMITIDA EM AMBIENTE DE HOMOLOGAÇÃO - SEM VALOR FISCAL</div>");
        sb.Append("<hr><table>");
        foreach (var i in d.Itens)
            sb.Append($"<tr><td colspan=2>{H(i.Cod)} {H(i.Desc)}</td></tr><tr><td>{i.Qtd.ToString("0.###", Br)} {H(i.Un)} x {R(i.Unit)}</td><td class=n>{R(i.Total)}</td></tr>");
        sb.Append($"</table><hr><table><tr><td>Qtd. total de itens</td><td class=n>{d.Itens.Count}</td></tr><tr><td>Valor total R$</td><td class=n>{R(d.VProd)}</td></tr>");
        if (d.VDesc > 0) sb.Append($"<tr><td>Descontos R$</td><td class=n>{R(d.VDesc)}</td></tr>");
        sb.Append($"<tr class=b><td>Valor a pagar R$</td><td class=n>{R(d.VNF)}</td></tr><tr class=b><td>FORMA PAGAMENTO</td><td class=n>VALOR PAGO R$</td></tr>");
        foreach (var p in d.Pagamentos) sb.Append($"<tr><td>{H(p.Forma)}</td><td class=n>{R(p.Valor)}</td></tr>");
        sb.Append("</table><hr><div class=c>");
        if (d.Contingencia) sb.Append("<div class=alerta>EMITIDA EM CONTINGÊNCIA<br>Pendente de autorização</div>");
        sb.Append($"Consulte pela Chave de Acesso em<br>{H(d.UrlChave)}<br><span class=chave>{Chave4(d.Chave)}</span><br><br>");
        sb.Append(d.Consumidor is null ? "CONSUMIDOR NÃO IDENTIFICADO" : "CONSUMIDOR " + H(d.Consumidor));
        sb.Append($"<br><b>NFC-e nº {d.Numero:000000000} Série {d.Serie:000} {d.Emissao:dd/MM/yyyy HH:mm:ss}</b>");
        if (d.Protocolo is not null) sb.Append($"<br>Protocolo de autorização: {H(d.Protocolo)}<br>Data de autorização: {d.AutorizadaEm:dd/MM/yyyy HH:mm:ss}");
        sb.Append($"<div class=qr>{svg}</div></div><hr><div>{H(d.Tributos)}</div>");
        sb.Append("<p class=\"c np\"><button onclick=\"print()\">Imprimir</button></p></div></body></html>");
        return sb.ToString();
    }

    private static string Chave4(string chave) => string.Join(' ', Enumerable.Range(0, 11).Select(i => chave.Substring(i * 4, 4)));

    private static string Mascara(string v, string mascara)
    {
        var sb = new StringBuilder(); var k = 0;
        foreach (var ch in mascara) { if (k >= v.Length) break; sb.Append(ch == '#' ? v[k++] : ch); }
        return sb.ToString();
    }
}
