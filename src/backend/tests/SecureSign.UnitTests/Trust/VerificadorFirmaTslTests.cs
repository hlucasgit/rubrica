using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;
using SecureSign.Trust;

namespace SecureSign.UnitTests.Trust;

/// <summary>
/// Prueba la verificación XAdES/XML-DSig de la TSL (RUNBOOK.md 12.22) con
/// documentos de prueba firmados de verdad con <c>SignedXml</c> — no solo
/// contra el archivo real de INDECOPI, que hoy NO verifica (hallazgo real,
/// documentado en <see cref="VerificadorFirmaTsl"/>, confirmado con tres
/// métodos independientes y una descarga fresca byte-idéntica). Estas
/// pruebas demuestran que el MECANISMO en sí es correcto — el problema
/// está en el archivo publicado, no en este código.
/// </summary>
public sealed class VerificadorFirmaTslTests : IDisposable
{
    private readonly List<string> _archivos = [];

    public void Dispose()
    {
        foreach (var archivo in _archivos) { try { File.Delete(archivo); } catch { /* mejor esfuerzo */ } }
    }

    static VerificadorFirmaTslTests()
    {
        // Mismo registro que el constructor estático de VerificadorFirmaTsl
        // (internal a SecureSign.Trust, no visible desde aquí) — hace falta
        // ANTES de firmar el fixture de prueba, así que se repite aquí con
        // una copia local del describer (ver comentario de la clase al
        // final del archivo).
        CryptoConfig.AddAlgorithm(typeof(RsaPkcs1Sha256SignatureDescriptionDePrueba), "http://www.w3.org/2001/04/xmldsig-more#rsa-sha256");
        CryptoConfig.AddAlgorithm(typeof(XmlDsigC14NTransform), "http://www.w3.org/TR/2001/REC-xml-c14n-20010315");
    }

    private string EscribirTslFirmada(RSA llavePrivada, X509Certificate2 certificadoFirmante)
    {
        var doc = new XmlDocument();
        doc.LoadXml("<tsl:TrustServiceStatusList xmlns:tsl=\"http://uri.etsi.org/02231/v2#\" Id=\"root-id\"><tsl:SchemeInformation/></tsl:TrustServiceStatusList>");

        var signedXml = new SignedXml(doc) { SigningKey = llavePrivada };
        signedXml.SignedInfo!.CanonicalizationMethod = "http://www.w3.org/TR/2001/REC-xml-c14n-20010315";
        signedXml.SignedInfo.SignatureMethod = "http://www.w3.org/2001/04/xmldsig-more#rsa-sha256";

        var referencia = new Reference { Uri = "#root-id", DigestMethod = "http://www.w3.org/2001/04/xmlenc#sha256" };
        referencia.AddTransform(new XmlDsigEnvelopedSignatureTransform());
        referencia.AddTransform(new XmlDsigC14NTransform());
        signedXml.AddReference(referencia);

        var keyInfo = new KeyInfo();
        keyInfo.AddClause(new KeyInfoX509Data(certificadoFirmante));
        signedXml.KeyInfo = keyInfo;

        signedXml.ComputeSignature();
        doc.DocumentElement!.AppendChild(doc.ImportNode(signedXml.GetXml(), true));

        // Sin esto, XmlDocument.Save indenta el XML (Formatting.Indented es
        // el default cuando PreserveWhitespace es false) — al recargarlo
        // luego con PreserveWhitespace=true (como hace VerificadorFirmaTsl,
        // igual que con la TSL real), esa indentación se vuelve texto
        // significativo y arruina el C14N, dando un falso "no verifica".
        // No es un bug del verificador: es este fixture reproduciendo cómo
        // se guardó el archivo. Confirmado aislando las tres variantes
        // (en memoria / recarga sin preservar / recarga preservando).
        doc.PreserveWhitespace = true;

        string ruta = Path.Combine(Path.GetTempPath(), $"tsl-firmada-prueba-{Guid.NewGuid():N}.xml");
        doc.Save(ruta);
        _archivos.Add(ruta);
        return ruta;
    }

    private string EscribirTslSinFirmar()
    {
        string ruta = Path.Combine(Path.GetTempPath(), $"tsl-sin-firmar-{Guid.NewGuid():N}.xml");
        File.WriteAllText(ruta, "<tsl:TrustServiceStatusList xmlns:tsl=\"http://uri.etsi.org/02231/v2#\"/>");
        _archivos.Add(ruta);
        return ruta;
    }

    private static X509Certificate2 GenerarAutofirmado(string cn, RSA? llave = null)
    {
        llave ??= RSA.Create(2048);
        var req = new System.Security.Cryptography.X509Certificates.CertificateRequest($"CN={cn}", llave, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return new X509Certificate2(req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1)).Export(X509ContentType.Cert));
    }

    [Fact]
    public void Tsl_sin_firma_lanza()
    {
        string ruta = EscribirTslSinFirmar();
        var raizCualquiera = GenerarAutofirmado("Raiz De Prueba");

        var ex = Assert.Throws<InvalidOperationException>(() => ListaConfianzaIofe.CargarDesdeArchivoFirmado(ruta, raizCualquiera));
        Assert.Contains("no trae ninguna firma", ex.Message);
    }

    [Fact]
    public void Certificado_firmante_no_emitido_por_el_ancla_lanza_sin_intentar_la_criptografia()
    {
        // Autofirmado — "emitido por" a sí mismo, no por el ancla que le pasamos.
        using var llave = RSA.Create(2048);
        var certificadoFirmante = GenerarAutofirmado("Firmante Sin Relacion", llave);
        string ruta = EscribirTslFirmada(llave, certificadoFirmante);

        var anclaNoRelacionada = GenerarAutofirmado("Ancla Distinta De Prueba");

        var ex = Assert.Throws<InvalidOperationException>(() => ListaConfianzaIofe.CargarDesdeArchivoFirmado(ruta, anclaNoRelacionada));
        Assert.Contains("no fue emitido por el ancla", ex.Message);
    }

    [Fact]
    public void Tsl_firmada_correctamente_por_un_certificado_emitido_por_el_ancla_verifica_y_carga()
    {
        // Aquí el "ancla" y el "firmante" son el mismo certificado
        // autofirmado (raíz que firma directamente) — caso más simple
        // donde CertificadoEmitidoPor debe aceptar (una raíz siempre
        // "se emite a sí misma").
        using var llave = RSA.Create(2048);
        var raizQueTambienFirma = GenerarAutofirmado("Raiz Que Firma Directo", llave);
        string ruta = EscribirTslFirmada(llave, raizQueTambienFirma);

        var lista = ListaConfianzaIofe.CargarDesdeArchivoFirmado(ruta, raizQueTambienFirma);

        Assert.True(lista.FirmaVerificada);
        Assert.Empty(lista.ServiciosAcreditados); // el fixture no trae ningún TSPService — solo prueba que la firma verificó
    }

    [Fact]
    public void La_TSL_real_de_INDECOPI_hoy_no_verifica_contra_su_propio_certificado_embebido()
    {
        // Documenta el hallazgo real de RUNBOOK.md 12.22 como prueba de
        // regresión — si INDECOPI corrige su pipeline de publicación, esta
        // prueba empezará a fallar y hay que actualizarla (señal de que ya
        // se puede activar CargarDesdeArchivoFirmado en producción).
        string directorioTrust = AppContext.BaseDirectory;
        string rutaTsl = Path.Combine(directorioTrust, "..", "..", "..", "..", "..", "src", "Services", "SecureSign.Signature", "SecureSign.Signature.Api", "ConfianzaIofe", "tsl-pe.xml");
        string rutaAncla = Path.Combine(directorioTrust, "..", "..", "..", "..", "..", "src", "Services", "SecureSign.Signature", "SecureSign.Signature.Api", "ConfianzaIofe", "tsl-firmante-raiz.crt");

        if (!File.Exists(rutaTsl) || !File.Exists(rutaAncla))
            return; // archivo real no presente en este entorno de build — no es un fallo de la prueba.

        var anclaReal = new X509Certificate2(rutaAncla);
        var ex = Assert.Throws<InvalidOperationException>(() => ListaConfianzaIofe.CargarDesdeArchivoFirmado(rutaTsl, anclaReal));
        Assert.Contains("NO verifica", ex.Message);
    }
}

/// <summary>
/// Copia local, solo para firmar los fixtures de prueba de este archivo —
/// evita depender del tipo real de producción (<c>SecureSign.Trust.RsaPkcs1Sha256SignatureDescription</c>)
/// para mantener el test self-contained. Mismos valores en ambas
/// (confirmados por reflexión contra el tipo interno real de Microsoft,
/// ver comentario en VerificadorFirmaTsl.cs).
/// </summary>
public sealed class RsaPkcs1Sha256SignatureDescriptionDePrueba : System.Security.Cryptography.SignatureDescription
{
    public RsaPkcs1Sha256SignatureDescriptionDePrueba()
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
