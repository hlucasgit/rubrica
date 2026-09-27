using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;
using SecureSign.Trust;

namespace SecureSign.UnitTests.Trust;

/// <summary>
/// Prueba la verificación XAdES/XML-DSig de la TSL (RUNBOOK.md 12.22) con
/// documentos de prueba firmados de verdad con <c>SignedXml</c> — no solo
/// contra el archivo real de INDECOPI. Hasta RUNBOOK.md 12.37 esa prueba
/// afirmaba lo contrario (que la TSL real "no verifica"): era un error propio
/// del verificador, corregido en 12.38 — ahora la prueba real exige que SÍ
/// verifique, y que un solo byte alterado la haga fallar.
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
        // Mismo registro que el constructor estático de VerificadorFirmaTsl —
        // hace falta ANTES de firmar el fixture de prueba. RsaPkcs1Sha256SignatureDescription
        // es public en SecureSign.Trust (CryptoConfig.AddAlgorithm lo exige),
        // así que se reutiliza directamente en vez de duplicarla aquí.
        CryptoConfig.AddAlgorithm(typeof(RsaPkcs1Sha256SignatureDescription), "http://www.w3.org/2001/04/xmldsig-more#rsa-sha256");
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

    /// <summary>Firma VÁLIDA, pero solo sobre un hijo (#hijo-id): el resto del documento —que es lo que luego se lee— queda sin firmar.</summary>
    private string EscribirTslFirmadaSoloUnFragmento(RSA llavePrivada, X509Certificate2 certificadoFirmante, bool duplicarIdDeLaRaiz = false)
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
            // Un segundo elemento con el mismo Id de la raíz: la referencia podría resolver a otro elemento que el leído.
            var copia = doc.CreateElement("tsl", "Duplicado", "http://uri.etsi.org/02231/v2#");
            copia.SetAttribute("Id", "root-id");
            doc.DocumentElement.AppendChild(copia);
        }

        string ruta = Path.Combine(Path.GetTempPath(), $"tsl-fragmento-{Guid.NewGuid():N}.xml");
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
    public void Una_firma_valida_que_no_cubre_el_documento_completo_se_rechaza()
    {
        using var llave = RSA.Create(2048);
        var raiz = GenerarAutofirmado("Raiz Que Firma Un Fragmento", llave);
        string ruta = EscribirTslFirmadaSoloUnFragmento(llave, raiz);

        var ex = Assert.Throws<InvalidOperationException>(() => ListaConfianzaIofe.CargarDesdeArchivoFirmado(ruta, raiz));
        Assert.Contains("no cubre el documento completo", ex.Message);
    }

    [Fact]
    public void Un_Id_repetido_en_la_raiz_se_rechaza_como_posible_envoltura_de_firma()
    {
        // La firma sí cubre la raíz (Id "root-id" con transformación envolvente)… pero el Id aparece dos veces.
        using var llave = RSA.Create(2048);
        var raiz = GenerarAutofirmado("Raiz Con Id Duplicado", llave);
        string ruta = EscribirTslFirmada(llave, raiz);
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.Load(ruta);
        var duplicado = doc.CreateElement("tsl", "Duplicado", "http://uri.etsi.org/02231/v2#");
        duplicado.SetAttribute("Id", "root-id");
        doc.DocumentElement!.PrependChild(duplicado);
        doc.Save(ruta);

        var ex = Assert.Throws<InvalidOperationException>(() => ListaConfianzaIofe.CargarDesdeArchivoFirmado(ruta, raiz));
        Assert.Contains("más de una vez", ex.Message);
    }

    private static (string Tsl, string Ancla)? RutasDeLaTslReal()
    {
        string baseDir = AppContext.BaseDirectory;
        string dir = Path.Combine(baseDir, "..", "..", "..", "..", "..", "src", "Services", "SecureSign.Signature", "SecureSign.Signature.Api", "ConfianzaIofe");
        string tsl = Path.Combine(dir, "tsl-pe.xml"), ancla = Path.Combine(dir, "tsl-firmante-raiz.crt");
        return File.Exists(tsl) && File.Exists(ancla) ? (tsl, ancla) : null;
    }

    /// <summary>
    /// Regresión del error de 12.22 (corregido en 12.38): la TSL REAL de INDECOPI verifica contra su propio
    /// certificado embebido y contra el ancla oficial. Su firma es de un firmante externo que canonicaliza el
    /// SignedInfo EN CONTEXTO (con los espacios de nombres heredados de la raíz), justo el caso en que
    /// <c>new SignedXml(doc)</c> daba un falso negativo — ningún fixture propio, firmado con el mismo SignedXml,
    /// puede reproducir esa diferencia, por eso esta prueba usa el archivo real.
    /// </summary>
    [Fact]
    public void La_TSL_real_de_INDECOPI_verifica_contra_el_ancla_oficial_y_carga_sus_servicios()
    {
        if (RutasDeLaTslReal() is not { } rutas) return; // archivo real no presente en este entorno de build.

        var lista = ListaConfianzaIofe.CargarDesdeArchivoFirmado(rutas.Tsl, new X509Certificate2(rutas.Ancla));

        Assert.True(lista.FirmaVerificada);
        Assert.NotEmpty(lista.ServiciosAcreditados);
    }

    [Fact]
    public void Un_solo_byte_alterado_en_la_TSL_real_la_hace_fallar()
    {
        if (RutasDeLaTslReal() is not { } rutas) return;

        string contenido = File.ReadAllText(rutas.Tsl);
        const string marca = "undersupervision";
        Assert.Contains(marca, contenido);
        string alterado = contenido.Replace(marca, "supervisionceased", StringComparison.Ordinal); // un servicio deja de estar "bajo supervisión"
        string ruta = Path.Combine(Path.GetTempPath(), $"tsl-real-alterada-{Guid.NewGuid():N}.xml");
        File.WriteAllText(ruta, alterado);
        _archivos.Add(ruta);

        var ex = Assert.Throws<InvalidOperationException>(() => ListaConfianzaIofe.CargarDesdeArchivoFirmado(ruta, new X509Certificate2(rutas.Ancla)));
        Assert.Contains("NO verifica", ex.Message);
    }

    [Fact]
    public void La_TSL_real_verificada_carga_los_mismos_servicios_que_sin_verificar()
    {
        if (RutasDeLaTslReal() is not { } rutas) return;

        var verificada = ListaConfianzaIofe.CargarDesdeArchivoFirmado(rutas.Tsl, new X509Certificate2(rutas.Ancla));
        var sinVerificar = ListaConfianzaIofe.CargarDesdeArchivo(rutas.Tsl);

        Assert.Equal(sinVerificar.ServiciosAcreditados.Count, verificada.ServiciosAcreditados.Count);
    }
}
