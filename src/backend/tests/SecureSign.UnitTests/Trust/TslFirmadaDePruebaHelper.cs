using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;
using SecureSign.Trust;

namespace SecureSign.UnitTests.Trust;

/// <summary>
/// Firma de verdad (SignedXml, no un mock) un documento TSL sintético con el mismo perfil XAdES confirmado
/// contra la TSL real de INDECOPI: SigningCertificate/CertDigest/IssuerSerial en SignedProperties, cubierto por
/// su propia ds:Reference (RUNBOOK.md 12.51). Extraído de VerificadorFirmaTslTests para reutilizarse también en
/// las pruebas de ActualizadorTsl (RUNBOOK.md 12.52) — ambas necesitan una TSL sintética que verifique de
/// verdad, no solo un XML con apariencia de firma.
/// </summary>
internal static class TslFirmadaDePruebaHelper
{
    private const string XadesNs = "http://uri.etsi.org/01903/v1.3.2#";
    private const string XadesSignedPropertiesTypeUri = "http://uri.etsi.org/01903#SignedProperties";
    private const string XmlDsigNs = "http://www.w3.org/2000/09/xmldsig#";

    static TslFirmadaDePruebaHelper()
    {
        // Mismo registro que el constructor estático de VerificadorFirmaTsl — hace falta ANTES de firmar.
        CryptoConfig.AddAlgorithm(typeof(RsaPkcs1Sha256SignatureDescription), "http://www.w3.org/2001/04/xmldsig-more#rsa-sha256");
        CryptoConfig.AddAlgorithm(typeof(XmlDsigC14NTransform), "http://www.w3.org/TR/2001/REC-xml-c14n-20010315");
    }

    /// <param name="contenidoSchemeInformation">XML crudo a insertar DENTRO de tsl:SchemeInformation (p. ej. ListIssueDateTime/NextUpdate).</param>
    /// <param name="contenidoRaizAdicional">XML crudo adicional como hijo directo de la raíz, después de SchemeInformation (p. ej. TSPServices).</param>
    /// <param name="certificadoParaElHash">Si se pasa, el CertDigest de SigningCertificate se calcula sobre ESTE certificado en vez del firmante real — para simular un SigningCertificate falso.</param>
    internal static string Escribir(
        RSA llavePrivada, X509Certificate2 certificadoFirmante,
        string contenidoSchemeInformation = "", string contenidoRaizAdicional = "",
        X509Certificate2? certificadoParaElHash = null, string? numeroDeSerieTextual = null, string? emisorTextual = null)
    {
        var doc = new XmlDocument();
        doc.LoadXml(
            "<tsl:TrustServiceStatusList xmlns:tsl=\"http://uri.etsi.org/02231/v2#\" Id=\"root-id\">" +
            $"<tsl:SchemeInformation>{contenidoSchemeInformation}</tsl:SchemeInformation>{contenidoRaizAdicional}" +
            "</tsl:TrustServiceStatusList>");

        const string idPropiedades = "signedProperties-0";
        var signedProperties = ConstruirSignedProperties(doc, idPropiedades, certificadoFirmante, certificadoParaElHash, numeroDeSerieTextual, emisorTextual);
        doc.DocumentElement!.AppendChild(signedProperties);

        var signedXml = new SignedXml(doc) { SigningKey = llavePrivada };
        signedXml.SignedInfo!.CanonicalizationMethod = "http://www.w3.org/TR/2001/REC-xml-c14n-20010315";
        signedXml.SignedInfo.SignatureMethod = "http://www.w3.org/2001/04/xmldsig-more#rsa-sha256";

        var referencia = new Reference { Uri = "#root-id", DigestMethod = "http://www.w3.org/2001/04/xmlenc#sha256" };
        referencia.AddTransform(new XmlDsigEnvelopedSignatureTransform());
        referencia.AddTransform(new XmlDsigC14NTransform());
        signedXml.AddReference(referencia);

        var referenciaPropiedades = new Reference { Uri = "#" + idPropiedades, Type = XadesSignedPropertiesTypeUri, DigestMethod = "http://www.w3.org/2001/04/xmlenc#sha256" };
        referenciaPropiedades.AddTransform(new XmlDsigC14NTransform());
        signedXml.AddReference(referenciaPropiedades);

        var keyInfo = new KeyInfo();
        keyInfo.AddClause(new KeyInfoX509Data(certificadoFirmante));
        signedXml.KeyInfo = keyInfo;

        signedXml.ComputeSignature();
        doc.DocumentElement!.AppendChild(doc.ImportNode(signedXml.GetXml(), true));

        // Sin esto, XmlDocument.Save indenta (Formatting.Indented es el default con PreserveWhitespace=false) —
        // al recargar luego con PreserveWhitespace=true (como VerificadorFirmaTsl), esa indentación se vuelve
        // texto significativo y arruina el C14N, dando un falso "no verifica". No es un bug del verificador.
        doc.PreserveWhitespace = true;

        string ruta = Path.Combine(Path.GetTempPath(), $"tsl-firmada-prueba-{Guid.NewGuid():N}.xml");
        doc.Save(ruta);
        return ruta;
    }

    /// <summary>Firma VÁLIDA, pero solo sobre un hijo (#hijo-id): el resto del documento —que es lo que luego se lee— queda sin firmar.</summary>
    internal static string EscribirSoloUnFragmentoFirmado(RSA llavePrivada, X509Certificate2 certificadoFirmante, bool duplicarIdDeLaRaiz = false)
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml("<tsl:TrustServiceStatusList xmlns:tsl=\"http://uri.etsi.org/02231/v2#\" Id=\"root-id\"><tsl:SchemeInformation Id=\"hijo-id\"/><tsl:Sin-firmar/></tsl:TrustServiceStatusList>");

        var signedXml = new SignedXml(doc) { SigningKey = llavePrivada };
        signedXml.SignedInfo!.CanonicalizationMethod = "http://www.w3.org/TR/2001/REC-xml-c14n-20010315";
        signedXml.SignedInfo.SignatureMethod = "http://www.w3.org/2001/04/xmldsig-more#rsa-sha256";
        var referencia = new Reference { Uri = "#hijo-id", DigestMethod = "http://www.w3.org/2001/04/xmlenc#sha256" };
        referencia.AddTransform(new XmlDsigC14NTransform());
        signedXml.AddReference(referencia);
        var keyInfo = new KeyInfo();
        keyInfo.AddClause(new KeyInfoX509Data(certificadoFirmante));
        signedXml.KeyInfo = keyInfo;
        signedXml.ComputeSignature();
        doc.DocumentElement!.AppendChild(doc.ImportNode(signedXml.GetXml(), true));

        if (duplicarIdDeLaRaiz)
        {
            var copia = doc.CreateElement("tsl", "Duplicado", "http://uri.etsi.org/02231/v2#");
            copia.SetAttribute("Id", "root-id");
            doc.DocumentElement.AppendChild(copia);
        }

        string ruta = Path.Combine(Path.GetTempPath(), $"tsl-fragmento-{Guid.NewGuid():N}.xml");
        doc.Save(ruta);
        return ruta;
    }

    private static XmlElement ConstruirSignedProperties(
        XmlDocument doc, string idPropiedades, X509Certificate2 certificadoFirmante,
        X509Certificate2? certificadoParaElHash, string? numeroDeSerieTextual, string? emisorTextual)
    {
        var certParaHash = certificadoParaElHash ?? certificadoFirmante;
        string digest = Convert.ToBase64String(SHA256.HashData(certParaHash.RawData));
        string serie = numeroDeSerieTextual ?? System.Numerics.BigInteger.Parse("0" + certificadoFirmante.SerialNumber, System.Globalization.NumberStyles.HexNumber).ToString();
        string emisor = emisorTextual ?? certificadoFirmante.Issuer;

        var signedProperties = doc.CreateElement("etsi", "SignedProperties", XadesNs);
        signedProperties.SetAttribute("Id", idPropiedades);
        signedProperties.InnerXml =
            $"<etsi:SignedSignatureProperties xmlns:etsi=\"{XadesNs}\">" +
            $"<etsi:SigningCertificate><etsi:Cert>" +
            $"<etsi:CertDigest><ds:DigestMethod xmlns:ds=\"{XmlDsigNs}\" Algorithm=\"http://www.w3.org/2001/04/xmlenc#sha256\"/><ds:DigestValue xmlns:ds=\"{XmlDsigNs}\">{digest}</ds:DigestValue></etsi:CertDigest>" +
            $"<etsi:IssuerSerial><ds:X509IssuerName xmlns:ds=\"{XmlDsigNs}\">{System.Security.SecurityElement.Escape(emisor)}</ds:X509IssuerName><ds:X509SerialNumber xmlns:ds=\"{XmlDsigNs}\">{serie}</ds:X509SerialNumber></etsi:IssuerSerial>" +
            $"</etsi:Cert></etsi:SigningCertificate>" +
            $"</etsi:SignedSignatureProperties>";
        return signedProperties;
    }
}
