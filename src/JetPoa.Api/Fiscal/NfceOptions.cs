namespace JetPoa.Api.Fiscal;

/// <summary>
/// Configuração da NFC-e (seção "Nfce" do appsettings). Segredos via variáveis de ambiente:
///   Nfce__CertificadoArquivo, Nfce__CertificadoSenha, Nfce__CscId, Nfce__Csc
/// </summary>
public sealed class NfceOptions
{
    /// <summary>1 = produção, 2 = homologação (testes, sem valor fiscal).</summary>
    public int Ambiente { get; set; } = 2;
    public int Serie { get; set; } = 2;
    public string? CertificadoArquivo { get; set; }
    public string? CertificadoSenha { get; set; }
    public string? CscId { get; set; }
    public string? Csc { get; set; }
    public int TimeoutSegundos { get; set; } = 15;
    /// <summary>Emitir automaticamente ao fechar a comanda.</summary>
    public bool EmitirNoFechamento { get; set; } = true;

    public Dictionary<string, SefazUrls> Urls { get; set; } = new()
    {
        ["1"] = new SefazUrls("https://nfce.sefazrs.rs.gov.br/ws"),
        ["2"] = new SefazUrls("https://nfce-homologacao.sefazrs.rs.gov.br/ws"),
    };

    public string UrlQrCode { get; set; } = "https://www.sefaz.rs.gov.br/NFCE/NFCE-COM.aspx";
    public string UrlConsultaChave { get; set; } = "https://www.sefaz.rs.gov.br/NFCE/NFCE-COM.aspx";

    public bool Configurada =>
        !string.IsNullOrWhiteSpace(CertificadoArquivo) && File.Exists(CertificadoArquivo)
        && !string.IsNullOrWhiteSpace(CscId) && !string.IsNullOrWhiteSpace(Csc);

    public SefazUrls UrlsAtuais => Urls[Ambiente.ToString()];
}

public sealed class SefazUrls
{
    public SefazUrls() { }
    public SefazUrls(string baseUrl)
    {
        Autorizacao = $"{baseUrl}/NfeAutorizacao/NFeAutorizacao4.asmx";
        Consulta = $"{baseUrl}/NfeConsulta/NfeConsulta4.asmx";
        Status = $"{baseUrl}/NfeStatusServico/NfeStatusServico4.asmx";
        Evento = $"{baseUrl}/recepcaoevento/recepcaoevento4.asmx";
        Inutilizacao = $"{baseUrl}/nfeinutilizacao/nfeinutilizacao4.asmx";
    }

    public string Autorizacao { get; set; } = "";
    public string Consulta { get; set; } = "";
    public string Status { get; set; } = "";
    public string Evento { get; set; } = "";
    public string Inutilizacao { get; set; } = "";
}
