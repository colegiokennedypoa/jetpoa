using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace JetPoa.Api.Fiscal;

/// <summary>Montagem do XML da NFC-e 4.00 (modelo 65), chave de acesso e QR Code v2.</summary>
public static class NfceXml
{
    public static readonly XNamespace Ns = "http://www.portalfiscal.inf.br/nfe";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    public const string CUfRs = "43";
    private const string TextoHomologacao = "NOTA FISCAL EMITIDA EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL";

    public sealed record Emitente(string Cnpj, string Ie, string RazaoSocial, string? NomeFantasia, string Logradouro,
        string Numero, string Bairro, string CodMunicipio, string Municipio, string Uf, string Cep, string? Fone,
        string Crt, decimal PcSimples);

    public sealed record Item(long ProdutoId, string Nome, string Ncm, string? Cest, string Cfop, string Csosn,
        string CstPisCofins, string? Gtin, string Unidade, decimal Quantidade, decimal PrecoUnitario, decimal Desconto);

    public sealed record Pagamento(string TPag, decimal Valor);

    public sealed record Dados(Emitente Emit, int Serie, int Numero, string CodigoNumerico, DateTime Emissao,
        int Ambiente, int TpEmis, DateTime? DhCont, string? XJust, string? CpfCnpjDest,
        IReadOnlyList<Item> Itens, IReadOnlyList<Pagamento> Pagamentos);

    // ------------------------------------------------------------------ chave

    public static string Chave(string cnpj, DateTime emissao, int serie, int numero, int tpEmis, string cNF)
    {
        var semDv = CUfRs + emissao.ToString("yyMM", Inv) + cnpj.PadLeft(14, '0') + "65"
                    + serie.ToString("000") + numero.ToString("000000000") + tpEmis + cNF.PadLeft(8, '0');
        return semDv + DigitoVerificador(semDv);
    }

    /// <summary>Módulo 11 com pesos 2..9 da direita para a esquerda.</summary>
    public static int DigitoVerificador(string chave43)
    {
        int soma = 0, peso = 2;
        for (var i = chave43.Length - 1; i >= 0; i--)
        {
            soma += (chave43[i] - '0') * peso;
            peso = peso == 9 ? 2 : peso + 1;
        }
        var resto = soma % 11;
        return resto is 0 or 1 ? 0 : 11 - resto;
    }

    public static string CodigoNumericoAleatorio(int numero)
    {
        // cNF não pode ser igual ao número da nota
        string c;
        do c = RandomNumberGenerator.GetInt32(10_000_000, 99_999_999).ToString(Inv);
        while (c == numero.ToString("00000000"));
        return c;
    }

    // ------------------------------------------------------------------ QR Code v2

    public static string QrCodeOnline(string urlQr, string chave, int ambiente, string cscId, string csc)
    {
        var p = $"{chave}|2|{ambiente}|{int.Parse(cscId, Inv)}";
        return $"{urlQr}?p={p}|{Sha1Hex(p + csc)}";
    }

    /// <summary>Contingência offline: inclui dia da emissão, valor e DigestValue (em hexadecimal).</summary>
    public static string QrCodeOffline(string urlQr, string chave, int ambiente, DateTime emissao, decimal vNF,
                                       string digestValueBase64, string cscId, string csc)
    {
        var digHex = Convert.ToHexString(Encoding.ASCII.GetBytes(digestValueBase64)).ToLowerInvariant();
        var p = $"{chave}|2|{ambiente}|{emissao:dd}|{F2(vNF)}|{digHex}|{int.Parse(cscId, Inv)}";
        return $"{urlQr}?p={p}|{Sha1Hex(p + csc)}";
    }

    private static string Sha1Hex(string s) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(s)));

    // ------------------------------------------------------------------ XML

    /// <summary>Monta o elemento NFe (sem assinatura e sem infNFeSupl). Retorna também a chave e o vNF.</summary>
    public static (XElement NFe, string Chave, decimal VNF) Montar(Dados d)
    {
        var chave = Chave(d.Emit.Cnpj, d.Emissao, d.Serie, d.Numero, d.TpEmis, d.CodigoNumerico);
        var homolog = d.Ambiente == 2;

        var ide = new XElement(Ns + "ide",
            E("cUF", CUfRs), E("cNF", d.CodigoNumerico), E("natOp", "Venda de mercadorias"), E("mod", "65"),
            E("serie", d.Serie), E("nNF", d.Numero), E("dhEmi", DataHora(d.Emissao)), E("tpNF", 1), E("idDest", 1),
            E("cMunFG", d.Emit.CodMunicipio), E("tpImp", 4), E("tpEmis", d.TpEmis), E("cDV", chave[43]),
            E("tpAmb", d.Ambiente), E("finNFe", 1), E("indFinal", 1), E("indPres", 1), E("indIntermed", 0),
            E("procEmi", 0), E("verProc", "JETPOA 2.0"));
        if (d.TpEmis == 9)
            ide.Add(E("dhCont", DataHora(d.DhCont ?? d.Emissao)), E("xJust", d.XJust ?? "Falha de comunicacao com a SEFAZ"));

        var emit = new XElement(Ns + "emit",
            E("CNPJ", d.Emit.Cnpj), E("xNome", Corta(d.Emit.RazaoSocial, 60)),
            d.Emit.NomeFantasia is null ? null : E("xFant", Corta(d.Emit.NomeFantasia, 60)),
            new XElement(Ns + "enderEmit",
                E("xLgr", d.Emit.Logradouro), E("nro", d.Emit.Numero), E("xBairro", d.Emit.Bairro),
                E("cMun", d.Emit.CodMunicipio), E("xMun", d.Emit.Municipio), E("UF", d.Emit.Uf), E("CEP", d.Emit.Cep),
                E("cPais", "1058"), E("xPais", "Brasil"), d.Emit.Fone is null ? null : E("fone", d.Emit.Fone)),
            E("IE", d.Emit.Ie), E("CRT", d.Emit.Crt));

        XElement? dest = null;
        if (!string.IsNullOrEmpty(d.CpfCnpjDest))
            dest = new XElement(Ns + "dest",
                E(d.CpfCnpjDest.Length == 14 ? "CNPJ" : "CPF", d.CpfCnpjDest),
                homolog ? E("xNome", "NF-E EMITIDA EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL") : null,
                E("indIEDest", 9));

        var dets = new List<XElement>();
        decimal vProdTot = 0, vDescTot = 0, vPisTot = 0, vCofinsTot = 0, tribEst = 0, tribFed = 0;
        var n = 0;
        foreach (var i in d.Itens)
        {
            n++;
            var vProd = R(i.Quantidade * i.PrecoUnitario);
            var vDesc = R(i.Desconto);
            var baseTrib = vProd - vDesc;
            vProdTot += vProd; vDescTot += vDesc;

            var (pis, cofins, vPis, vCofins) = PisCofins(i.CstPisCofins, baseTrib, d.Emit.PcSimples);
            vPisTot += vPis; vCofinsTot += vCofins;
            var est = R(baseTrib * (d.Emit.PcSimples * (i.Csosn == "102" ? 34m : 0m)) / 100 / 100);
            var fed = R(baseTrib * d.Emit.PcSimples * 5.5m / 100 / 100) + R(baseTrib * d.Emit.PcSimples * 3.5m / 100 / 100)
                    + vPis + vCofins + R(baseTrib * d.Emit.PcSimples * 41.5m / 100 / 100);
            tribEst += est; tribFed += fed;
            var gtin = string.IsNullOrWhiteSpace(i.Gtin) ? "SEM GTIN" : i.Gtin;

            dets.Add(new XElement(Ns + "det", new XAttribute("nItem", n),
                new XElement(Ns + "prod",
                    E("cProd", i.ProdutoId), E("cEAN", gtin),
                    E("xProd", homolog && n == 1 ? TextoHomologacao : Corta(Limpa(i.Nome), 120)),
                    E("NCM", i.Ncm), string.IsNullOrEmpty(i.Cest) ? null : E("CEST", i.Cest), E("CFOP", i.Cfop),
                    E("uCom", Corta(i.Unidade, 6)), E("qCom", F4(i.Quantidade)), E("vUnCom", F10(i.PrecoUnitario)),
                    E("vProd", F2(vProd)), E("cEANTrib", gtin), E("uTrib", Corta(i.Unidade, 6)),
                    E("qTrib", F4(i.Quantidade)), E("vUnTrib", F10(i.PrecoUnitario)),
                    vDesc > 0 ? E("vDesc", F2(vDesc)) : null, E("indTot", 1)),
                new XElement(Ns + "imposto",
                    E("vTotTrib", F2(est + fed)),
                    new XElement(Ns + "ICMS", Icms(i.Csosn)),
                    pis, cofins)));
        }

        var vNF = vProdTot - vDescTot;
        var total = new XElement(Ns + "total", new XElement(Ns + "ICMSTot",
            E("vBC", "0.00"), E("vICMS", "0.00"), E("vICMSDeson", "0.00"), E("vFCP", "0.00"), E("vBCST", "0.00"),
            E("vST", "0.00"), E("vFCPST", "0.00"), E("vFCPSTRet", "0.00"), E("vProd", F2(vProdTot)), E("vFrete", "0.00"),
            E("vSeg", "0.00"), E("vDesc", F2(vDescTot)), E("vII", "0.00"), E("vIPI", "0.00"), E("vIPIDevol", "0.00"),
            E("vPIS", F2(vPisTot)), E("vCOFINS", F2(vCofinsTot)), E("vOutro", "0.00"), E("vNF", F2(vNF)),
            E("vTotTrib", F2(tribEst + tribFed))));

        var pag = new XElement(Ns + "pag");
        foreach (var p in d.Pagamentos)
            pag.Add(new XElement(Ns + "detPag",
                E("tPag", p.TPag), p.TPag == "99" ? E("xPag", "Outros") : null, E("vPag", F2(p.Valor)),
                p.TPag is "03" or "04" or "17" ? new XElement(Ns + "card", E("tpIntegra", 2)) : null));

        var tribTot = tribEst + tribFed;
        var pct = (decimal v) => vNF > 0 ? (v / vNF * 100).ToString("0.00", Inv) : "0.00";
        var infCpl = $"Total aproximado de tributos: R$ {F2(tribTot)} ({pct(tribTot)}%) Federais R$ {F2(tribFed)} ({pct(tribFed)}%) " +
                     $"Estaduais R$ {F2(tribEst)} ({pct(tribEst)}%). Fonte IBPT. DOCUMENTO EMITIDO POR ME OU EPP OPTANTE PELO SIMPLES NACIONAL, " +
                     "NAO GERA DIREITO A CREDITO FISCAL DE ICMS E DE IPI.";

        var infNFe = new XElement(Ns + "infNFe", new XAttribute("Id", "NFe" + chave), new XAttribute("versao", "4.00"),
            ide, emit, dest, dets, total,
            new XElement(Ns + "transp", E("modFrete", 9)),
            pag,
            new XElement(Ns + "infAdic", E("infCpl", infCpl)));

        return (new XElement(Ns + "NFe", infNFe), chave, vNF);
    }

    /// <summary>Acrescenta infNFeSupl (QR Code e URL de consulta) — deve ficar após infNFe e antes da Signature.</summary>
    public static void AdicionarSupl(XElement nfe, string qrCode, string urlChave)
    {
        var supl = new XElement(Ns + "infNFeSupl", new XElement(Ns + "qrCode", new XCData(qrCode)), E("urlChave", urlChave));
        nfe.Element(Ns + "infNFe")!.AddAfterSelf(supl);
    }

    // ------------------------------------------------------------------ tributos

    private static XElement Icms(string csosn) => csosn == "500"
        ? new XElement(Ns + "ICMSSN500", E("orig", 0), E("CSOSN", "500"))
        : new XElement(Ns + "ICMSSN102", E("orig", 0), E("CSOSN", csosn));

    /// <summary>PIS/COFINS como no sistema atual (notas autorizadas): 04-09 não tributado; 01/02 alíquota; demais "outras".</summary>
    private static (XElement Pis, XElement Cofins, decimal VPis, decimal VCofins) PisCofins(string cst, decimal baseCalc, decimal pcSimples)
    {
        if (cst is "04" or "05" or "06" or "07" or "08" or "09")
            return (new XElement(Ns + "PIS", new XElement(Ns + "PISNT", E("CST", cst))),
                    new XElement(Ns + "COFINS", new XElement(Ns + "COFINSNT", E("CST", cst))), 0, 0);

        const decimal pPis = 2.76m, pCofins = 12.74m;
        var vPis = R(baseCalc * (pcSimples * pPis) / 100 / 100);
        var vCofins = R(baseCalc * (pcSimples * pCofins) / 100 / 100);
        var (gPis, gCof) = cst is "01" or "02" ? ("PISAliq", "COFINSAliq") : ("PISOutr", "COFINSOutr");
        return (new XElement(Ns + "PIS", new XElement(Ns + gPis, E("CST", cst), E("vBC", F2(baseCalc)), E("pPIS", F4(pPis)), E("vPIS", F2(vPis)))),
                new XElement(Ns + "COFINS", new XElement(Ns + gCof, E("CST", cst), E("vBC", F2(baseCalc)), E("pCOFINS", F4(pCofins)), E("vCOFINS", F2(vCofins)))),
                vPis, vCofins);
    }

    // ------------------------------------------------------------------ validação prévia (antes de numerar)

    public static readonly string[] CsosnAceitos = ["102", "103", "300", "400", "500"];
    public static readonly string[] CstPisAceitos = ["01", "02", "04", "05", "06", "07", "08", "09", "49", "99"];

    public static List<string> Validar(IEnumerable<(string Produto, string? Ncm, string? Cfop, string? Csosn, string? Cst)> itens)
    {
        var erros = new List<string>();
        foreach (var i in itens)
        {
            var problemas = new List<string>();
            if (i.Ncm is null || i.Ncm.Length != 8 || !i.Ncm.All(char.IsDigit)) problemas.Add("NCM");
            if (i.Cfop is null || i.Cfop.Length != 4 || !i.Cfop.All(char.IsDigit)) problemas.Add("CFOP");
            if (i.Csosn is null || !CsosnAceitos.Contains(i.Csosn)) problemas.Add("CSOSN");
            if (i.Cst is null || !CstPisAceitos.Contains(i.Cst)) problemas.Add("CST PIS/COFINS");
            // regra que gerou rejeições no sistema atual: CFOP x CSOSN
            if (i.Csosn == "500" && i.Cfop is not null && !i.Cfop.EndsWith("405") && i.Cfop != "5656" && i.Cfop != "5667")
                problemas.Add($"CFOP {i.Cfop} incompatível com CSOSN 500 (use 5405)");
            if (i.Csosn is "102" or "103" or "300" or "400" && i.Cfop?.EndsWith("405") == true)
                problemas.Add($"CFOP {i.Cfop} incompatível com CSOSN {i.Csosn} (use 5102)");
            if (problemas.Count > 0) erros.Add($"{i.Produto}: {string.Join(", ", problemas)}");
        }
        return erros;
    }

    // ------------------------------------------------------------------ util

    private static XElement E(string nome, object valor) => new(Ns + nome, Convert.ToString(valor, Inv));
    public static string DataHora(DateTime d) => d.ToString("yyyy-MM-dd'T'HH:mm:ss", Inv) + "-03:00";
    private static decimal R(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
    public static string F2(decimal v) => v.ToString("0.00", Inv);
    private static string F4(decimal v) => v.ToString("0.0000", Inv);
    private static string F10(decimal v) => v.ToString("0.00########", Inv);
    private static string Corta(string s, int max) => s.Length <= max ? s : s[..max];

    /// <summary>Remove acentos e caracteres fora do padrão aceito pela SEFAZ.</summary>
    public static string Limpa(string s)
    {
        var semAcento = Impressao.EscPos.SemAcentos(s).ToUpperInvariant();
        var sb = new StringBuilder();
        foreach (var ch in semAcento)
            sb.Append(char.IsLetterOrDigit(ch) || " .,-/+()%".Contains(ch) ? ch : ' ');
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
