using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;

namespace SecureSign.Trust;

/// <summary>
/// Verifica la firma XAdES-BES (envolvente, XML-DSig estándar por debajo —
/// ver ETSI TS 101 903) de la TSL de INDECOPI antes de que
/// <see cref="ListaConfianzaIofe"/> confíe en una sola línea de su
/// contenido. Ver RUNBOOK.md 12.22.
///
/// QUÉ prueba esto: que el archivo XML es EXACTAMENTE el que firmó el
/// titular de <c>raizConfiableFirmaTsl</c> (o una cadena que cuelga de
/// esa raíz) — ningún byte cambió desde que se firmó, ni en tránsito
/// (descarga por HTTP en vez de HTTPS, un proxy que lo altere) ni en
/// reposo (el archivo local corrupto o modificado). Verifica los TRES
/// <c>ds:Reference</c> del documento (el certificado firmante, las
/// propiedades XAdES firmadas, y el documento TSL completo con la firma
/// removida — transform "enveloped-signature").
///
/// QUÉ NO prueba: validación XAdES-BES completa en el sentido estricto de
/// ETSI TS 101 903 (por ejemplo, no cruza el digest de
/// <c>SigningCertificate</c> dentro de <c>SignedProperties</c> contra el
/// certificado real de <c>KeyInfo</c> — esa es una comprobación XAdES
/// adicional, no XML-DSig puro). Tampoco verifica marcas de tiempo ni
/// revocación del propio certificado firmante de la TSL.
///
/// HALLAZGO REAL (ver RUNBOOK.md 12.22, NO ocultado): la TSL real que
/// INDECOPI publica hoy en https://iofe.indecopi.gob.pe/TSL/tsl-pe.xml NO
/// verifica contra su propio certificado embebido con esta implementación
/// — confirmado con tres métodos independientes (SignedXml.CheckSignature,
/// replicación manual de C14N+SHA256, y el método interno
/// SignedXml.GetC14NDigest vía reflexión) contra una descarga fresca
/// byte-idéntica al archivo del repositorio, descartando corrupción local.
/// El propio motor XML-DSig de .NET se probó correcto firmando y
/// verificando un documento análogo. Por eso <see cref="ListaConfianzaIofe.CargarDesdeArchivoFirmado"/>
/// NO se usa todavía en producción (`Signature.Api` sigue con
/// `CargarDesdeArchivo`, sin verificar) — activar esto como bloqueante
/// tumbaría el arranque del servicio contra un dato real y públicamente
/// servido, no contra un archivo corrupto localmente.
/// </summary>
internal static class VerificadorFirmaTsl
{
    private const string XmlDsigNamespace = "http://www.w3.org/2000/09/xmldsig#";

    static VerificadorFirmaTsl()
    {
        // Ninguno de los dos viene registrado por defecto en el paquete
        // System.Security.Cryptography.Xml 8.0.4 standalone (sí lo están,
        // por otras vías, dentro del framework compartido de .NET
        // Framework clásico) — sin esto, SignedXml.CheckSignature lanza
        // NullReferenceException (falta el algoritmo de firma) o
        // simplemente no encuentra cómo verificar. Confirmado con
        // CryptoConfig.CreateFromName devolviendo null para ambas URIs
        // antes de este registro.
        CryptoConfig.AddAlgorithm(typeof(RsaPkcs1Sha256SignatureDescription), "http://www.w3.org/2001/04/xmldsig-more#rsa-sha256");
        CryptoConfig.AddAlgorithm(typeof(XmlDsigC14NTransform), "http://www.w3.org/TR/2001/REC-xml-c14n-20010315");
    }

    public static void VerificarOLanzar(string rutaXml, X509Certificate2 raizConfiable)
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.Load(rutaXml);

        var nodosFirma = doc.GetElementsByTagName("Signature", XmlDsigNamespace);
        if (nodosFirma.Count == 0)
            throw new InvalidOperationException($"'{rutaXml}' no trae ninguna firma XML-DSig (<ds:Signature>) — no se puede confiar en su contenido.");

        var nodoFirma = (XmlElement)nodosFirma[0]!;

        var certificadoFirmante = ExtraerCertificadoFirmante(nodoFirma)
            ?? throw new InvalidOperationException("La firma de la TSL no trae un certificado en <ds:KeyInfo>/<ds:X509Certificate> — no hay con qué verificarla.");

        if (!CertificadoEmitidoPor(certificadoFirmante, raizConfiable))
            throw new InvalidOperationException(
                $"El certificado que firmó la TSL (\"{certificadoFirmante.Subject}\") no fue emitido por el ancla de confianza configurada (\"{raizConfiable.Subject}\") — se rechaza sin verificar la firma criptográfica: confiar en la firma de un certificado no emitido por la autoridad esperada sería tan inseguro como no verificar nada.");

        var signedXml = new SignedXml(doc);
        signedXml.LoadXml(nodoFirma);

        // verifySignatureOnly=true: la validación de la CADENA del
        // certificado ya se hizo arriba, contra el ancla real de INDECOPI
        // (X509Chain con CustomRootTrust, igual que ValidadorCertificados
        // para certificados de firmante) — no delegar esa parte al
        // almacén de certificados de confianza del sistema operativo, que
        // no tiene ninguna razón para conocer la raíz de INDECOPI.
        bool firmaValida;
        try { firmaValida = signedXml.CheckSignature(certificadoFirmante, verifySignatureOnly: true); }
        catch (Exception ex) { throw new InvalidOperationException($"No se pudo verificar la firma de la TSL: {ex.Message}", ex); }

        if (!firmaValida)
            throw new InvalidOperationException("La firma XAdES de la TSL NO verifica — el archivo pudo haberse alterado después de firmarse. Se rechaza sin cargar ningún servicio acreditado de su contenido.");
    }

    private static X509Certificate2? ExtraerCertificadoFirmante(XmlElement nodoFirma)
    {
        var nodosCert = nodoFirma.GetElementsByTagName("X509Certificate", XmlDsigNamespace);
        if (nodosCert.Count == 0) return null;

        byte[] der = Convert.FromBase64String(nodosCert[0]!.InnerText.Trim());
        return new X509Certificate2(der);
    }

    private static bool CertificadoEmitidoPor(X509Certificate2 hoja, X509Certificate2 raiz)
    {
        var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(raiz);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck; // sin infraestructura de revocación propia para la PKI de firma de TSL — fuera de alcance, ver RUNBOOK 12.22.
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
        chain.ChainPolicy.VerificationTime = DateTime.UtcNow;

        return chain.Build(hoja);
    }
}

/// <summary>
/// RSA-PKCS1 con SHA-256 para XML-DSig (URI "xmldsig-more#rsa-sha256",
/// RFC 6931) — la implementación equivalente de Microsoft
/// (<c>System.Security.Cryptography.Xml.RSAPKCS1SHA256SignatureDescription</c>)
/// es <c>internal</c> en el paquete <c>System.Security.Cryptography.Xml</c>
/// 8.0.4 standalone, y <c>CryptoConfig.AddAlgorithm</c> exige un tipo
/// público — de ahí esta reimplementación mínima, con los mismos valores
/// (confirmados por reflexión contra el tipo interno real antes de
/// escribir esta clase, no adivinados).
/// </summary>
public sealed class RsaPkcs1Sha256SignatureDescription : SignatureDescription
{
    public RsaPkcs1Sha256SignatureDescription()
    {
        KeyAlgorithm = typeof(RSA).AssemblyQualifiedName;
        DigestAlgorithm = "SHA256";
        FormatterAlgorithm = typeof(RSAPKCS1SignatureFormatter).AssemblyQualifiedName;
        DeformatterAlgorithm = typeof(RSAPKCS1SignatureDeformatter).AssemblyQualifiedName;
    }

    public override AsymmetricSignatureDeformatter CreateDeformatter(AsymmetricAlgorithm key)
    {
        var deformatter = new RSAPKCS1SignatureDeformatter(key);
        deformatter.SetHashAlgorithm("SHA256");
        return deformatter;
    }

    public override AsymmetricSignatureFormatter CreateFormatter(AsymmetricAlgorithm key)
    {
        var formatter = new RSAPKCS1SignatureFormatter(key);
        formatter.SetHashAlgorithm("SHA256");
        return formatter;
    }
}
