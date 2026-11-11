using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml.Linq;

namespace JetPoa.Api.Fiscal;

/// <summary>Chamadas SOAP 1.2 aos web services da SEFAZ-RS (NFC-e 4.00), com certificado A1 no TLS.</summary>
public sealed class SefazClient : IDisposable
{
    private static readonly XNamespace Soap = "http://www.w3.org/2003/05/soap-envelope";
    private const string WsdlBase = "http://www.portalfiscal.inf.br/nfe/wsdl/";
    private readonly HttpClient _http;

    /// <summary>Erro de comunicação (sem resposta da SEFAZ): dispara a contingência.</summary>
    public sealed class FalhaComunicacao(string msg, Exception? inner = null) : Exception(msg, inner);

    public SefazClient(X509Certificate2 cert, int timeoutSegundos)
    {
        var handler = new HttpClientHandler
        {
            ClientCertificateOptions = ClientCertificateOption.Manual,
            SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
        };
        handler.ClientCertificates.Add(cert);
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(timeoutSegundos) };
    }

    public Task<XElement> AutorizarAsync(string url, XElement enviNFe, CancellationToken ct) =>
        ChamarAsync(url, "NFeAutorizacao4", enviNFe, ct);
    public Task<XElement> ConsultarAsync(string url, XElement consSitNFe, CancellationToken ct) =>
        ChamarAsync(url, "NFeConsultaProtocolo4", consSitNFe, ct);
    public Task<XElement> StatusAsync(string url, XElement consStatServ, CancellationToken ct) =>
        ChamarAsync(url, "NFeStatusServico4", consStatServ, ct);
    public Task<XElement> EventoAsync(string url, XElement envEvento, CancellationToken ct) =>
        ChamarAsync(url, "NFeRecepcaoEvento4", envEvento, ct);
    public Task<XElement> InutilizarAsync(string url, XElement inutNFe, CancellationToken ct) =>
        ChamarAsync(url, "NFeInutilizacao4", inutNFe, ct);

    private async Task<XElement> ChamarAsync(string url, string servico, XElement dados, CancellationToken ct)
    {
        var env = new XElement(Soap + "Envelope", new XAttribute(XNamespace.Xmlns + "soap12", Soap),
            new XElement(Soap + "Body",
                new XElement(XName.Get("nfeDadosMsg", WsdlBase + servico), dados)));
        var corpo = "<?xml version=\"1.0\" encoding=\"utf-8\"?>" + env.ToString(SaveOptions.DisableFormatting);

        HttpResponseMessage resp;
        try
        {
            using var content = new StringContent(corpo, Encoding.UTF8, "application/soap+xml");
            resp = await _http.PostAsync(url, content, ct);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            var causa = e.InnerException is null ? "" : " → " + string.Join(" → ", Causas(e.InnerException));
            throw new FalhaComunicacao($"Sem resposta da SEFAZ ({servico}): {e.Message}{causa}", e);
        }

        var texto = await resp.Content.ReadAsStringAsync(ct);
        if (resp.StatusCode >= HttpStatusCode.InternalServerError && !texto.Contains("Envelope"))
            throw new FalhaComunicacao($"SEFAZ indisponível ({(int)resp.StatusCode}).");

        XElement xml;
        try { xml = XElement.Parse(texto); }
        catch (Exception e) { throw new FalhaComunicacao($"Resposta inválida da SEFAZ ({(int)resp.StatusCode}).", e); }

        // primeiro elemento de dentro de nfeResultMsg
        var result = xml.Descendants().FirstOrDefault(x => x.Name.LocalName == "nfeResultMsg")?.Elements().FirstOrDefault();
        return result ?? throw new FalhaComunicacao($"Resposta sem nfeResultMsg ({(int)resp.StatusCode}): {Trunca(texto)}");
    }

    private static IEnumerable<string> Causas(Exception? e)
    {
        for (; e is not null; e = e.InnerException) yield return e.Message;
    }

    private static string Trunca(string s) => s.Length > 300 ? s[..300] : s;

    public void Dispose() => _http.Dispose();
}
