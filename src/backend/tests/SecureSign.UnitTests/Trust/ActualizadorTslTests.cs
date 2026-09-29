using System.Formats.Asn1;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Ocsp;
using Org.BouncyCastle.Security;
using SecureSign.Trust;
using BcCertificate = Org.BouncyCastle.X509.X509Certificate;

namespace SecureSign.UnitTests.Trust;

/// <summary>
/// RUNBOOK.md 12.52 (informe de preauditoría INDECOPI/IOFE, hallazgo P2-01): <see cref="ActualizadorTsl"/>
/// descarga a un archivo temporal, verifica TODO (firma XAdES, cadena, cobertura, SigningCertificate —
/// RUNBOOK.md 12.38/12.51 — y revocación del firmante — RUNBOOK.md 12.50) y solo entonces reemplaza el archivo
/// vigente. Certificados de prueba autofirmados (raíz = firmante, como ya hace VerificadorFirmaTslTests) con
/// extensiones CRL/AIA construidas a mano vía AsnWriter — más simple que una cadena BouncyCastle completa
/// cuando el único certificado que hace falta revocar es el propio firmante de la TSL.
/// </summary>
public sealed class ActualizadorTslTests : IDisposable
{
    private readonly List<string> _archivos = [];
    private readonly List<string> _directorios = [];

    public void Dispose()
    {
        foreach (var a in _archivos) { try { File.Delete(a); } catch { } }
        foreach (var d in _directorios) { try { Directory.Delete(d, recursive: true); } catch { } }
    }

    private static byte[] ConstruirExtensionCrlDistPoint(string urlCrl)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        using (writer.PushSequence())
        using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true)))
        using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true)))
            writer.WriteCharacterString(UniversalTagNumber.IA5String, urlCrl, new Asn1Tag(TagClass.ContextSpecific, 6, isConstructed: false));
        return writer.Encode();
    }

    private static byte[] ConstruirExtensionAiaOcsp(string urlOcsp)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        using (writer.PushSequence())
        {
            writer.WriteObjectIdentifier("1.3.6.1.5.5.7.48.1"); // id-ad-ocsp
            writer.WriteCharacterString(UniversalTagNumber.IA5String, urlOcsp, new Asn1Tag(TagClass.ContextSpecific, 6, isConstructed: false));
        }
        return writer.Encode();
    }

    private static X509Certificate2 NuevoCertificadoConCdpYOcsp(string cn, string urlCrl, string urlOcsp, out RSA llave)
    {
        llave = RSA.Create(2048);
        var req = new CertificateRequest($"CN={cn}", llave, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign | X509KeyUsageFlags.DigitalSignature, true));
        req.CertificateExtensions.Add(new System.Security.Cryptography.X509Certificates.X509Extension("2.5.29.31", ConstruirExtensionCrlDistPoint(urlCrl), critical: false));
        req.CertificateExtensions.Add(new System.Security.Cryptography.X509Certificates.X509Extension("1.3.6.1.5.5.7.1.1", ConstruirExtensionAiaOcsp(urlOcsp), critical: false));
        return new X509Certificate2(req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1)).Export(X509ContentType.Cert));
    }

    private static async Task<HttpResponseMessage> ResponderOcsp(HttpRequestMessage request, X509Certificate2 certDotNet, RSA llave, CertificateStatus estado)
    {
        byte[] cuerpo = await request.Content!.ReadAsByteArrayAsync();
        var certId = new OcspReq(cuerpo).GetRequestList()[0].GetCertID();
        var certBc = new Org.BouncyCastle.X509.X509CertificateParser().ReadCertificate(certDotNet.RawData);
        var llaveBc = DotNetUtilities.GetRsaKeyPair(llave).Private;

        var generadorBasico = new BasicOcspRespGenerator(DotNetUtilities.GetRsaKeyPair(llave).Public);
        generadorBasico.AddResponse(certId, estado);
        var basico = generadorBasico.Generate(new Asn1SignatureFactory("SHA256WITHRSA", llaveBc), Array.Empty<BcCertificate>(), DateTime.UtcNow);
        var respuesta = new OCSPRespGenerator().Generate(OCSPRespGenerator.Successful, basico);

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(respuesta.GetEncoded()) { Headers = { ContentType = new MediaTypeHeaderValue("application/ocsp-response") } },
        };
    }

    private sealed record Escenario(X509Certificate2 Raiz, RSA Llave, string DirectorioTsl, string RutaTslVigente, HandlerHttpFalso Handler, string UrlCrl, string UrlOcsp, string UrlDescarga);

    private Escenario Construir()
    {
        string sufijo = Guid.NewGuid().ToString("N");
        string urlOcsp = $"https://tsl-updater.prueba.local/ocsp-{sufijo}";
        string urlCrl = $"https://tsl-updater.prueba.local/crl-{sufijo}.crl";
        string urlDescarga = $"https://tsl-updater.prueba.local/tsl-{sufijo}.xml";

        var raiz = NuevoCertificadoConCdpYOcsp($"Raiz Actualizador TSL De Prueba {sufijo}", urlCrl, urlOcsp, out var llave);

        string dirTsl = Path.Combine(Path.GetTempPath(), $"actualizador-tsl-{sufijo}");
        Directory.CreateDirectory(dirTsl);
        _directorios.Add(dirTsl);
        string rutaVigente = Path.Combine(dirTsl, "tsl-pe.xml");

        var handler = new HandlerHttpFalso();
        handler.ResponderBytes(urlCrl, CrlVaciaFirmadaPor(raiz, llave), "application/pkix-crl");
        handler.Responder(urlOcsp, req => ResponderOcsp(req, raiz, llave, CertificateStatus.Good));

        return new Escenario(raiz, llave, dirTsl, rutaVigente, handler, urlCrl, urlOcsp, urlDescarga);
    }

    private static byte[] CrlVaciaFirmadaPor(X509Certificate2 certDotNet, RSA llave)
    {
        var certBc = new Org.BouncyCastle.X509.X509CertificateParser().ReadCertificate(certDotNet.RawData);
        var generador = new Org.BouncyCastle.X509.X509V2CrlGenerator();
        generador.SetIssuerDN(certBc.SubjectDN);
        generador.SetThisUpdate(DateTime.UtcNow.AddDays(-1));
        generador.SetNextUpdate(DateTime.UtcNow.AddDays(30));
        return generador.Generate(new Asn1SignatureFactory("SHA256WITHRSA", DotNetUtilities.GetRsaKeyPair(llave).Private)).GetEncoded();
    }

    private string EscribirTslCandidata(Escenario e, DateTimeOffset emitidaEn, DateTimeOffset proximaActualizacion)
    {
        string contenidoFechas =
            $"<tsl:ListIssueDateTime>{emitidaEn.UtcDateTime:o}</tsl:ListIssueDateTime>" +
            $"<tsl:NextUpdate><tsl:dateTime>{proximaActualizacion.UtcDateTime:o}</tsl:dateTime></tsl:NextUpdate>";
        string ruta = TslFirmadaDePruebaHelper.Escribir(e.Llave, e.Raiz, contenidoSchemeInformation: contenidoFechas);
        _archivos.Add(ruta);
        return ruta;
    }

    private ActualizadorTsl ConstruirActualizador(Escenario e)
    {
        var http = new HttpClient(e.Handler);
        return new ActualizadorTsl(http, new VerificadorRevocacionOcsp(http), new VerificadorRevocacionCrl(http));
    }

    [Fact]
    public async Task Una_TSL_mas_reciente_y_no_revocada_reemplaza_a_la_vigente_y_guarda_copia_de_respaldo()
    {
        var e = Construir();
        File.WriteAllText(e.RutaTslVigente, "contenido-vigente-de-prueba");
        var candidataRuta = EscribirTslCandidata(e, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(180));
        e.Handler.ResponderBytes(e.UrlDescarga, File.ReadAllBytes(candidataRuta), "application/xml");

        var actualizador = ConstruirActualizador(e);
        var resultado = await actualizador.ActualizarAsync(new Uri(e.UrlDescarga), e.RutaTslVigente, e.Raiz, listaActual: null);

        Assert.Equal(ResultadoActualizacionTsl.Actualizada, resultado.Resultado);
        Assert.NotNull(resultado.NuevaLista);
        Assert.True(File.Exists(e.RutaTslVigente));
        Assert.Equal("contenido-vigente-de-prueba", File.ReadAllText(e.RutaTslVigente + ".anterior"));
        Assert.NotEqual("contenido-vigente-de-prueba", File.ReadAllText(e.RutaTslVigente));
    }

    [Fact]
    public async Task Una_TSL_no_mas_reciente_que_la_vigente_no_la_reemplaza()
    {
        var e = Construir();
        var emitidaEn = DateTimeOffset.UtcNow.AddDays(-30);
        var proximaActualizacion = DateTimeOffset.UtcNow.AddDays(150);
        var vigenteRuta = EscribirTslCandidata(e, emitidaEn, proximaActualizacion);
        File.Copy(vigenteRuta, e.RutaTslVigente);
        var listaVigente = ListaConfianzaIofe.CargarDesdeArchivoFirmado(e.RutaTslVigente, e.Raiz);

        // La "descarga" trae la MISMA fecha de emisión que la vigente — no es más reciente.
        var candidataRuta = EscribirTslCandidata(e, emitidaEn, proximaActualizacion);
        e.Handler.ResponderBytes(e.UrlDescarga, File.ReadAllBytes(candidataRuta), "application/xml");

        var actualizador = ConstruirActualizador(e);
        byte[] contenidoAntes = File.ReadAllBytes(e.RutaTslVigente);
        var resultado = await actualizador.ActualizarAsync(new Uri(e.UrlDescarga), e.RutaTslVigente, e.Raiz, listaVigente);

        Assert.Equal(ResultadoActualizacionTsl.SinCambios, resultado.Resultado);
        Assert.Equal(contenidoAntes, File.ReadAllBytes(e.RutaTslVigente));
        Assert.False(File.Exists(e.RutaTslVigente + ".anterior"));
    }

    [Fact]
    public async Task Una_TSL_descargada_que_no_verifica_no_reemplaza_la_vigente()
    {
        var e = Construir();
        File.WriteAllText(e.RutaTslVigente, "contenido-vigente-de-prueba");

        var otraRaiz = NuevoCertificadoConCdpYOcsp("Otra Raiz No Relacionada", e.UrlCrl, e.UrlOcsp, out var otraLlave);
        string ruta = TslFirmadaDePruebaHelper.Escribir(otraLlave, otraRaiz); // firmada por OTRA raíz, no por e.Raiz
        _archivos.Add(ruta);
        e.Handler.ResponderBytes(e.UrlDescarga, File.ReadAllBytes(ruta), "application/xml");

        var actualizador = ConstruirActualizador(e);
        var resultado = await actualizador.ActualizarAsync(new Uri(e.UrlDescarga), e.RutaTslVigente, e.Raiz, listaActual: null);

        Assert.Equal(ResultadoActualizacionTsl.RechazadaFirmaInvalida, resultado.Resultado);
        Assert.Equal("contenido-vigente-de-prueba", File.ReadAllText(e.RutaTslVigente));
        Assert.False(File.Exists(e.RutaTslVigente + ".anterior"));
    }

    [Fact]
    public async Task Una_TSL_firmada_por_un_certificado_revocado_no_reemplaza_la_vigente()
    {
        var e = Construir();
        File.WriteAllText(e.RutaTslVigente, "contenido-vigente-de-prueba");
        e.Handler.Responder(e.UrlOcsp, req => ResponderOcsp(req, e.Raiz, e.Llave, CertificateStatus.Good));
        e.Handler.ResponderBytes(e.UrlCrl, CrlConSerialRevocado(e.Raiz, e.Llave), "application/pkix-crl");

        var candidataRuta = EscribirTslCandidata(e, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(180));
        e.Handler.ResponderBytes(e.UrlDescarga, File.ReadAllBytes(candidataRuta), "application/xml");

        var actualizador = ConstruirActualizador(e);
        var resultado = await actualizador.ActualizarAsync(new Uri(e.UrlDescarga), e.RutaTslVigente, e.Raiz, listaActual: null);

        Assert.Equal(ResultadoActualizacionTsl.RechazadaRevocada, resultado.Resultado);
        Assert.Equal("contenido-vigente-de-prueba", File.ReadAllText(e.RutaTslVigente));
        Assert.False(File.Exists(e.RutaTslVigente + ".anterior"));
    }

    private static byte[] CrlConSerialRevocado(X509Certificate2 certDotNet, RSA llave)
    {
        var certBc = new Org.BouncyCastle.X509.X509CertificateParser().ReadCertificate(certDotNet.RawData);
        var generador = new Org.BouncyCastle.X509.X509V2CrlGenerator();
        generador.SetIssuerDN(certBc.SubjectDN);
        generador.SetThisUpdate(DateTime.UtcNow.AddDays(-1));
        generador.SetNextUpdate(DateTime.UtcNow.AddDays(30));
        generador.AddCrlEntry(certBc.SerialNumber, DateTime.UtcNow.AddDays(-1), CrlReason.KeyCompromise);
        return generador.Generate(new Asn1SignatureFactory("SHA256WITHRSA", DotNetUtilities.GetRsaKeyPair(llave).Private)).GetEncoded();
    }

    [Fact]
    public async Task Una_URL_de_descarga_que_no_es_HTTPS_se_rechaza_sin_descargar_nada()
    {
        var e = Construir();
        File.WriteAllText(e.RutaTslVigente, "contenido-vigente-de-prueba");
        var actualizador = ConstruirActualizador(e);

        var resultado = await actualizador.ActualizarAsync(new Uri("http://tsl-updater.prueba.local/tsl.xml"), e.RutaTslVigente, e.Raiz, listaActual: null);

        Assert.Equal(ResultadoActualizacionTsl.RechazadaError, resultado.Resultado);
        Assert.Contains("HTTPS", resultado.Detalle);
        Assert.Equal("contenido-vigente-de-prueba", File.ReadAllText(e.RutaTslVigente));
    }

    [Fact]
    public async Task Si_la_descarga_falla_no_deja_archivos_temporales_ni_toca_la_vigente()
    {
        var e = Construir();
        File.WriteAllText(e.RutaTslVigente, "contenido-vigente-de-prueba");
        // No se registra ninguna ruta para UrlDescarga — HandlerHttpFalso responde 404.
        var actualizador = ConstruirActualizador(e);

        var resultado = await actualizador.ActualizarAsync(new Uri(e.UrlDescarga), e.RutaTslVigente, e.Raiz, listaActual: null);

        Assert.Equal(ResultadoActualizacionTsl.RechazadaError, resultado.Resultado);
        Assert.Equal("contenido-vigente-de-prueba", File.ReadAllText(e.RutaTslVigente));
        Assert.Empty(Directory.GetFiles(e.DirectorioTsl).Where(f => f.Contains(".tsl-descarga-")));
    }
}
