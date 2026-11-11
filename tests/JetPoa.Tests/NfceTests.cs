using System.Xml.Linq;
using JetPoa.Api.Fiscal;
using Xunit;

namespace JetPoa.Tests;

public class NfceTests
{
    private static readonly XNamespace Ns = NfceXml.Ns;

    // Chaves de NFC-e AUTORIZADAS pela SEFAZ-RS (sistema atual, série 1)
    [Theory]
    [InlineData("43261052822987000123650010000012121776307720")]
    [InlineData("43261052822987000123650010000012101127481377")]
    [InlineData("43261052822987000123650010000012111701702820")]
    public void DigitoVerificador_confere_com_chaves_autorizadas(string chave) =>
        Assert.Equal(chave[43] - '0', NfceXml.DigitoVerificador(chave[..43]));

    [Fact]
    public void Chave_montada_igual_a_autorizada()
    {
        var chave = NfceXml.Chave("52822987000123", new DateTime(2026, 10, 28), 1, 1212, 1, "77630772");
        Assert.Equal("43261052822987000123650010000012121776307720", chave);
    }

    /// <summary>
    /// Confere o hash do QR Code contra uma nota autorizada. Precisa do CSC real em JETPOA_TEST_CSC
    /// (não fica no repositório); sem ele o teste só valida o formato.
    /// </summary>
    [Fact]
    public void QrCode_online_igual_ao_autorizado()
    {
        const string chave = "43261052822987000123650010000012121776307720";
        var csc = Environment.GetEnvironmentVariable("JETPOA_TEST_CSC");
        var qr = NfceXml.QrCodeOnline("https://www.sefaz.rs.gov.br/NFCE/NFCE-COM.aspx", chave, 1, "1", csc ?? "TESTE");
        Assert.StartsWith($"https://www.sefaz.rs.gov.br/NFCE/NFCE-COM.aspx?p={chave}|2|1|1|", qr);
        Assert.Matches("[0-9A-F]{40}$", qr);
        if (csc is not null)
            Assert.EndsWith("|F6300E94107B66D8021F51E4A1553405A2B249C9", qr);
    }

    private static NfceXml.Dados Exemplo(int tpEmis = 1, int ambiente = 2, string? cpf = "52998224725") => new(
        new NfceXml.Emitente("52822987000123", "0963986503", "JETPOA COMPLEXO NAUTICO DE LAZER E TURISMO LTDA", "JETPOA",
            "R DO PESCADOR", "21", "ARQUIPELAGO", "4314902", "Porto Alegre", "RS", "90090170", "5189409094", "1", 4),
        2, 15, "12345678", new DateTime(2026, 11, 11, 14, 30, 0), ambiente, tpEmis,
        tpEmis == 9 ? new DateTime(2026, 11, 11, 14, 30, 0) : null, tpEmis == 9 ? "Falha de comunicacao com a SEFAZ" : null, cpf,
        [
            new(1092, "CORONA 330 ML", "22030000", null, "5405", "500", "04", null, "UN", 3, 18m, 5.40m),
            new(1167, "ISCAS DE PEIXE 330 G", "21069090", null, "5102", "102", "49", null, "UN", 1, 48m, 4.80m),
        ],
        [new("17", 50m), new("01", 41.80m)]);

    private static (XElement NFe, string Chave, decimal VNF, string Digest) MontarAssinado(NfceXml.Dados d)
    {
        using var cert = Assinador.CertificadoDeTeste();
        var (nfe, chave, vNF) = NfceXml.Montar(d);
        var (assinado, digest) = Assinador.Assinar(nfe, "infNFe", cert);
        var qr = d.TpEmis == 9
            ? NfceXml.QrCodeOffline("https://www.sefaz.rs.gov.br/NFCE/NFCE-COM.aspx", chave, d.Ambiente, d.Emissao, vNF, digest, "1", "TESTE")
            : NfceXml.QrCodeOnline("https://www.sefaz.rs.gov.br/NFCE/NFCE-COM.aspx", chave, d.Ambiente, "1", "TESTE");
        NfceXml.AdicionarSupl(assinado, qr, "https://www.sefaz.rs.gov.br/NFCE/NFCE-COM.aspx");
        return (assinado, chave, vNF, digest);
    }

    [Fact]
    public void Assinatura_valida_e_continua_valida_apos_infNFeSupl()
    {
        var (nfe, _, _, _) = MontarAssinado(Exemplo());
        Assert.True(Assinador.Verificar(nfe.ToString(SaveOptions.DisableFormatting)));
        // ordem exigida pelo schema: infNFe, infNFeSupl, Signature
        Assert.Equal(["infNFe", "infNFeSupl", "Signature"], nfe.Elements().Select(e => e.Name.LocalName));
    }

    [Fact]
    public void Totais_desconto_e_pagamentos_fecham()
    {
        var (nfe, chave, vNF, _) = MontarAssinado(Exemplo());
        var tot = nfe.Descendants(Ns + "ICMSTot").Single();
        Assert.Equal("102.00", tot.Element(Ns + "vProd")!.Value);   // 54 + 48
        Assert.Equal("10.20", tot.Element(Ns + "vDesc")!.Value);
        Assert.Equal("91.80", tot.Element(Ns + "vNF")!.Value);
        Assert.Equal(91.80m, vNF);
        var somaPag = nfe.Descendants(Ns + "vPag").Sum(v => decimal.Parse(v.Value, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(vNF, somaPag);
        // tPag correto (não mais 99) e grupo card para PIX
        Assert.Equal(["17", "01"], nfe.Descendants(Ns + "tPag").Select(t => t.Value));
        Assert.Single(nfe.Descendants(Ns + "card"));
        Assert.Equal(44, chave.Length);
        Assert.Equal("NFe" + chave, nfe.Element(Ns + "infNFe")!.Attribute("Id")!.Value);
    }

    [Fact]
    public void Homologacao_usa_textos_obrigatorios()
    {
        var (nfe, _, _, _) = MontarAssinado(Exemplo(ambiente: 2));
        Assert.Equal("NOTA FISCAL EMITIDA EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL", nfe.Descendants(Ns + "xProd").First().Value);
        Assert.Equal("NF-E EMITIDA EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL", nfe.Descendants(Ns + "dest").Single().Element(Ns + "xNome")!.Value);
        var (prod, _, _, _) = MontarAssinado(Exemplo(ambiente: 1));
        Assert.Equal("CORONA 330 ML", prod.Descendants(Ns + "xProd").First().Value);
        Assert.Null(prod.Descendants(Ns + "dest").Single().Element(Ns + "xNome"));
    }

    [Fact]
    public void Ordem_dos_elementos_igual_a_nota_autorizada()
    {
        var (nfe, _, _, _) = MontarAssinado(Exemplo(ambiente: 1, cpf: null));
        var inf = nfe.Element(Ns + "infNFe")!;
        Assert.Equal(["ide", "emit", "det", "det", "total", "transp", "pag", "infAdic"], inf.Elements().Select(e => e.Name.LocalName));
        Assert.Equal(
            ["cUF", "cNF", "natOp", "mod", "serie", "nNF", "dhEmi", "tpNF", "idDest", "cMunFG", "tpImp", "tpEmis", "cDV", "tpAmb",
             "finNFe", "indFinal", "indPres", "indIntermed", "procEmi", "verProc"],
            inf.Element(Ns + "ide")!.Elements().Select(e => e.Name.LocalName));
        var prod = inf.Element(Ns + "det")!.Element(Ns + "prod")!;
        Assert.Equal(
            ["cProd", "cEAN", "xProd", "NCM", "CFOP", "uCom", "qCom", "vUnCom", "vProd", "cEANTrib", "uTrib", "qTrib", "vUnTrib", "vDesc", "indTot"],
            prod.Elements().Select(e => e.Name.LocalName));
        Assert.Equal("ICMSSN500", inf.Element(Ns + "det")!.Descendants(Ns + "ICMS").Single().Elements().Single().Name.LocalName);
    }

    [Fact]
    public void Contingencia_offline_tem_tpEmis_9_dhCont_e_qrcode_offline()
    {
        var (nfe, chave, _, digest) = MontarAssinado(Exemplo(tpEmis: 9));
        var ide = nfe.Descendants(Ns + "ide").Single();
        Assert.Equal("9", ide.Element(Ns + "tpEmis")!.Value);
        Assert.Equal("9", chave[34].ToString()); // tpEmis faz parte da chave
        Assert.NotNull(ide.Element(Ns + "dhCont"));
        Assert.Equal("verProc", ide.Element(Ns + "dhCont")!.ElementsBeforeSelf().Last().Name.LocalName);
        var qr = nfe.Descendants(Ns + "qrCode").Single().Value;
        var p = qr.Split("?p=")[1].Split('|');
        Assert.Equal(8, p.Length);                 // chave|2|amb|dia|vNF|digVal|cIdToken|hash
        Assert.Equal("11", p[3]);
        Assert.Equal("91.80", p[4]);
        Assert.Equal(Convert.ToHexString(System.Text.Encoding.ASCII.GetBytes(digest)).ToLowerInvariant(), p[5]);
        Assert.True(Assinador.Verificar(nfe.ToString(SaveOptions.DisableFormatting)));
    }

    [Theory]
    [InlineData("22030000", "5405", "500", "04", true)]
    [InlineData("21069090", "5102", "102", "49", true)]
    [InlineData("22030000", "5102", "500", "04", false)]   // CFOP x CSOSN: gerava rejeição no sistema atual
    [InlineData("22030000", "5405", "102", "04", false)]
    [InlineData("111318", "5102", "102", "49", false)]     // NCM inválido (existe no cadastro atual)
    [InlineData(null, "5102", "102", "49", false)]
    public void Validacao_previa_dos_dados_fiscais(string? ncm, string cfop, string csosn, string cst, bool ok)
    {
        var erros = NfceXml.Validar([("PRODUTO", ncm, cfop, csosn, cst)]);
        Assert.Equal(ok, erros.Count == 0);
    }

    [Fact]
    public void Danfe_le_xml_e_gera_html_e_escpos()
    {
        var (nfe, chave, _, _) = MontarAssinado(Exemplo());
        var d = Danfe.Ler(nfe.ToString(SaveOptions.DisableFormatting), "143260000000001", DateTime.Now);
        Assert.Equal(chave, d.Chave);
        Assert.Equal(2, d.Itens.Count);
        Assert.Equal(91.80m, d.VNF);
        Assert.Contains("HOMOLOGA", Danfe.Html(d));
        Assert.Contains("<svg", Danfe.Html(d));
        Assert.True(Danfe.EscPos(d).Length > 500);
    }
}
