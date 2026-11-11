using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;
using System.Xml.Linq;

namespace JetPoa.Api.Fiscal;

/// <summary>Assinatura XMLDSig enveloped (RSA-SHA1 + C14N), padrão do Manual de Orientação do Contribuinte.</summary>
public static class Assinador
{
    /// <summary>
    /// UserKeySet (e não EphemeralKeySet): o TLS do Windows (SChannel) não aceita chave efêmera na
    /// autenticação do cliente ("platform does not support ephemeral keys"). Sem PersistKeySet a chave
    /// temporária é descartada quando o certificado é liberado.
    /// </summary>
    public static X509Certificate2 CarregarCertificado(string arquivo, string? senha) =>
        new(arquivo, senha, X509KeyStorageFlags.UserKeySet);

    /// <summary>
    /// Assina o elemento filho de <paramref name="raiz"/> que tem o atributo Id (infNFe, infEvento, infInut)
    /// e acrescenta a Signature como último filho de <paramref name="raiz"/>. Retorna o DigestValue (base64).
    /// </summary>
    public static (XElement Assinado, string DigestValue) Assinar(XElement raiz, string nomeElementoAssinado, X509Certificate2 cert)
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        using (var r = raiz.CreateReader()) doc.Load(r);

        var alvo = (XmlElement)doc.GetElementsByTagName(nomeElementoAssinado, NfceXml.Ns.NamespaceName)[0]!;
        var id = alvo.GetAttribute("Id");

        var rsa = cert.GetRSAPrivateKey() ?? throw new InvalidOperationException("Certificado sem chave privada RSA.");
        var signed = new SignedXml(doc) { SigningKey = rsa };
        signed.SignedInfo!.CanonicalizationMethod = SignedXml.XmlDsigC14NTransformUrl;
        signed.SignedInfo.SignatureMethod = SignedXml.XmlDsigRSASHA1Url;

        var reference = new Reference("#" + id) { DigestMethod = SignedXml.XmlDsigSHA1Url };
        reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
        reference.AddTransform(new XmlDsigC14NTransform());
        signed.AddReference(reference);

        var keyInfo = new KeyInfo();
        keyInfo.AddClause(new KeyInfoX509Data(cert));
        signed.KeyInfo = keyInfo;

        signed.ComputeSignature();
        var assinatura = signed.GetXml();
        doc.DocumentElement!.AppendChild(doc.ImportNode(assinatura, true));

        var digest = Convert.ToBase64String(reference.DigestValue!);
        return (XElement.Parse(doc.OuterXml, LoadOptions.PreserveWhitespace), digest);
    }

    /// <summary>Confere a assinatura (usado nos testes e antes de transmitir).</summary>
    public static bool Verificar(string xml)
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(xml);
        var sig = (XmlElement)doc.GetElementsByTagName("Signature", SignedXml.XmlDsigNamespaceUrl)[0]!;
        var signed = new SignedXml(doc);
        signed.LoadXml(sig);
        return signed.CheckSignature();
    }

    /// <summary>Certificado autoassinado só para testes locais (nunca vai para a SEFAZ).</summary>
    public static X509Certificate2 CertificadoDeTeste()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=TESTE JETPOA:52822987000123", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var cert = req.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddDays(1));
        return new X509Certificate2(cert.Export(X509ContentType.Pfx), (string?)null, X509KeyStorageFlags.Exportable | X509KeyStorageFlags.EphemeralKeySet);
    }
}
