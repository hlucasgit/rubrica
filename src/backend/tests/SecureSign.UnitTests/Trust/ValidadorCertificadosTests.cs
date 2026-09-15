using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Ocsp;
using SecureSign.Trust;
using BcCertificate = Org.BouncyCastle.X509.X509Certificate;
using NodoCadena = SecureSign.UnitTests.Trust.CadenaDePruebaHelper.NodoCadena;

namespace SecureSign.UnitTests.Trust;

/// <summary>
/// Prueba ValidadorCertificados de punta a punta SIN red real, usando una
/// cadena de certificados de un solo uso (CadenaDePruebaHelper) y un
/// HttpMessageHandler falso (HandlerHttpFalso) inyectado en los mismos
/// HttpClient que reciben DescargadorCertificadosIntermedios,
/// VerificadorRevocacionCrl y VerificadorRevocacionOcsp en producción — no
/// hizo falta ningún cambio de diseño para poder mockearlos.
///
/// Escrita al hilo de encontrar un hallazgo real (ver RUNBOOK.md 12.17):
/// ni VerificadorRevocacionCrl ni VerificadorRevocacionOcsp verificaban la
/// firma de la CRL/respuesta OCSP antes de confiar en su contenido — un
/// MITM en esa URL podía forjar "no revocado". Los casos "forjada"/"delegado"
/// de abajo prueban exactamente ese arreglo.
/// </summary>
public sealed class ValidadorCertificadosTests : IDisposable
{
    private readonly List<string> _archivosTemporales = [];
    private readonly List<string> _directoriosTemporales = [];

    public void Dispose()
    {
        foreach (var archivo in _archivosTemporales) { try { File.Delete(archivo); } catch { /* mejor esfuerzo */ } }
        foreach (var dir in _directoriosTemporales) { try { Directory.Delete(dir, recursive: true); } catch { /* mejor esfuerzo */ } }
    }

    private sealed record Escenario(ValidadorCertificados Validador, X509Certificate2 Hoja);

    private Escenario Construir(
        bool raizConfiableParaCadena = true,
        bool intermediaEnTsl = true,
        DateTime? notBeforeHoja = null, DateTime? notAfterHoja = null,
        bool hojaEsCa = false, int keyUsageHoja = KeyUsage.NonRepudiation | KeyUsage.DigitalSignature,
        Action<HandlerHttpFalso, string, string, NodoCadena, X509Certificate2>? configurarRevocacion = null)
    {
        string sufijo = Guid.NewGuid().ToString("N");
        string urlEmisorRaiz = $"https://ca.prueba.local/raiz-{sufijo}.cer";
        string urlEmisorIntermedia = $"https://ca.prueba.local/intermedia-{sufijo}.cer";
        string urlOcsp = $"https://ca.prueba.local/ocsp-{sufijo}";
        string urlCrl = $"https://ca.prueba.local/crl-{sufijo}.crl";

        var raiz = CadenaDePruebaHelper.GenerarRaiz($"Raiz De Prueba {sufijo}");
        var intermedia = CadenaDePruebaHelper.GenerarIntermedia(raiz, $"Intermedia De Prueba {sufijo}", urlEmisorRaiz);
        var hoja = CadenaDePruebaHelper.GenerarHoja(intermedia, $"Firmante De Prueba {sufijo}", urlEmisorIntermedia, urlOcsp, urlCrl,
            notBeforeHoja, notAfterHoja, hojaEsCa, keyUsageHoja);
        var hojaDotNet = hoja.ComoDotNet();

        var handler = new HandlerHttpFalso();
        handler.ResponderBytes(urlEmisorRaiz, raiz.CertificadoBc.GetEncoded(), "application/pkix-cert");
        handler.ResponderBytes(urlEmisorIntermedia, intermedia.CertificadoBc.GetEncoded(), "application/pkix-cert");

        if (configurarRevocacion is not null)
            configurarRevocacion(handler, urlCrl, urlOcsp, intermedia, hojaDotNet);
        else
        {
            handler.ResponderBytes(urlCrl, CadenaDePruebaHelper.GenerarCrl(intermedia, []), "application/pkix-crl");
            handler.Responder(urlOcsp, req => ResponderOcsp(req, intermedia, CertificateStatus.Good));
        }

        string dirRaices = Path.Combine(Path.GetTempPath(), $"securesign-raices-{sufijo}");
        Directory.CreateDirectory(dirRaices);
        _directoriosTemporales.Add(dirRaices);
        if (raizConfiableParaCadena)
            File.WriteAllBytes(Path.Combine(dirRaices, "raiz.cer"), raiz.CertificadoBc.GetEncoded());
        var almacenRaices = AlmacenRaicesConfiables.CargarDesdeDirectorio(dirRaices);

        string archivoTsl = TslDePruebaHelper.EscribirArchivoTemporal(
            intermediaEnTsl ? [("Intermedia De Prueba Acreditada", intermedia.CertificadoBc.GetEncoded())] : []);
        _archivosTemporales.Add(archivoTsl);
        var listaIofe = ListaConfianzaIofe.CargarDesdeArchivo(archivoTsl);

        var httpClient = new HttpClient(handler);
        var validador = new ValidadorCertificados(
            almacenRaices, listaIofe,
            new DescargadorCertificadosIntermedios(httpClient),
            new VerificadorRevocacionCrl(httpClient),
            new VerificadorRevocacionOcsp(httpClient));

        return new Escenario(validador, hojaDotNet);
    }

    private static async Task<HttpResponseMessage> ResponderOcsp(
        HttpRequestMessage request, NodoCadena firmante, CertificateStatus estado, IEnumerable<BcCertificate>? certificadosAIncluir = null)
    {
        byte[] cuerpo = await request.Content!.ReadAsByteArrayAsync();
        var certId = new OcspReq(cuerpo).GetRequestList()[0].GetCertID();

        var generadorBasico = new BasicOcspRespGenerator(firmante.Llaves.Public);
        generadorBasico.AddResponse(certId, estado);

        var basico = generadorBasico.Generate(
            new Asn1SignatureFactory("SHA256WITHRSA", firmante.Llaves.Private),
            (certificadosAIncluir ?? []).ToArray(),
            DateTime.UtcNow);
        var respuesta = new OCSPRespGenerator().Generate(OCSPRespGenerator.Successful, basico);

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(respuesta.GetEncoded()) { Headers = { ContentType = new MediaTypeHeaderValue("application/ocsp-response") } },
        };
    }

    [Fact]
    public async Task Certificado_valido_en_todos_los_aspectos_es_confiable()
    {
        var e = Construir();
        var resultado = await e.Validador.ValidarAsync(e.Hoja, DateTimeOffset.UtcNow);
        Assert.True(resultado.EstadoFinal, string.Join(" | ", resultado.Evidencia));
    }

    [Fact]
    public async Task Certificado_expirado_no_es_vigente()
    {
        var e = Construir(notBeforeHoja: DateTime.UtcNow.AddYears(-2), notAfterHoja: DateTime.UtcNow.AddYears(-1));
        var resultado = await e.Validador.ValidarAsync(e.Hoja, DateTimeOffset.UtcNow);
        Assert.False(resultado.CertificadoVigente);
        Assert.False(resultado.EstadoFinal);
    }

    [Fact]
    public async Task Certificado_marcado_como_CA_no_tiene_proposito_de_firma()
    {
        var e = Construir(hojaEsCa: true);
        var resultado = await e.Validador.ValidarAsync(e.Hoja, DateTimeOffset.UtcNow);
        Assert.False(resultado.PropositoValido);
        Assert.False(resultado.EstadoFinal);
    }

    [Fact]
    public async Task Certificado_sin_NonRepudio_ni_FirmaDigital_no_tiene_proposito_de_firma()
    {
        var e = Construir(keyUsageHoja: KeyUsage.KeyEncipherment);
        var resultado = await e.Validador.ValidarAsync(e.Hoja, DateTimeOffset.UtcNow);
        Assert.False(resultado.PropositoValido);
        Assert.False(resultado.EstadoFinal);
    }

    [Fact]
    public async Task Raiz_no_configurada_como_confiable_invalida_la_cadena()
    {
        var e = Construir(raizConfiableParaCadena: false);
        var resultado = await e.Validador.ValidarAsync(e.Hoja, DateTimeOffset.UtcNow);
        Assert.False(resultado.CadenaValida);
        Assert.False(resultado.EstadoFinal);
    }

    [Fact]
    public async Task Cadena_confiable_pero_fuera_de_la_TSL_IOFE_no_es_acreditada()
    {
        var e = Construir(intermediaEnTsl: false);
        var resultado = await e.Validador.ValidarAsync(e.Hoja, DateTimeOffset.UtcNow);
        Assert.True(resultado.CadenaValida);
        Assert.False(resultado.RaizConfiableIofe);
        Assert.False(resultado.EstadoFinal);
    }

    [Fact]
    public async Task Certificado_revocado_en_CRL_firmada_correctamente_se_detecta()
    {
        var e = Construir(configurarRevocacion: (handler, urlCrl, urlOcsp, intermedia, hoja) =>
        {
            var serialRevocado = new BigInteger(hoja.SerialNumber, 16);
            handler.ResponderBytes(urlCrl, CadenaDePruebaHelper.GenerarCrl(intermedia, [serialRevocado]), "application/pkix-crl");
            handler.Responder(urlOcsp, req => ResponderOcsp(req, intermedia, CertificateStatus.Good));
        });

        var resultado = await e.Validador.ValidarAsync(e.Hoja, DateTimeOffset.UtcNow);
        Assert.Equal(EstadoRevocacion.Revoked, resultado.Revocacion.Crl);
        Assert.Equal(EstadoRevocacion.Revoked, resultado.Revocacion.Combinado);
        Assert.False(resultado.EstadoFinal);
    }

    [Fact]
    public async Task Certificado_revocado_segun_OCSP_se_detecta()
    {
        var e = Construir(configurarRevocacion: (handler, urlCrl, urlOcsp, intermedia, hoja) =>
        {
            handler.ResponderBytes(urlCrl, CadenaDePruebaHelper.GenerarCrl(intermedia, []), "application/pkix-crl");
            handler.Responder(urlOcsp, req => ResponderOcsp(req, intermedia, new RevokedStatus(DateTime.UtcNow.AddDays(-1), 0)));
        });

        var resultado = await e.Validador.ValidarAsync(e.Hoja, DateTimeOffset.UtcNow);
        Assert.Equal(EstadoRevocacion.Revoked, resultado.Revocacion.Ocsp);
        Assert.Equal(EstadoRevocacion.Revoked, resultado.Revocacion.Combinado);
        Assert.False(resultado.EstadoFinal);
    }

    [Fact]
    public async Task CRL_firmada_por_una_llave_que_no_es_el_emisor_se_descarta_sin_confiar_en_su_contenido()
    {
        var e = Construir(configurarRevocacion: (handler, urlCrl, urlOcsp, intermedia, hoja) =>
        {
            var impostor = CadenaDePruebaHelper.GenerarLlaves();
            byte[] crlForjada = CadenaDePruebaHelper.GenerarCrl(intermedia, [], llaveFirmante: impostor.Private);
            handler.ResponderBytes(urlCrl, crlForjada, "application/pkix-crl");
            handler.Responder(urlOcsp, req => ResponderOcsp(req, intermedia, CertificateStatus.Good));
        });

        var resultado = await e.Validador.ValidarAsync(e.Hoja, DateTimeOffset.UtcNow);
        Assert.Equal(EstadoRevocacion.Unavailable, resultado.Revocacion.Crl);
    }

    [Fact]
    public async Task OCSP_firmado_por_una_llave_que_no_es_el_emisor_ni_un_delegado_valido_se_descarta()
    {
        var e = Construir(configurarRevocacion: (handler, urlCrl, urlOcsp, intermedia, hoja) =>
        {
            handler.ResponderBytes(urlCrl, CadenaDePruebaHelper.GenerarCrl(intermedia, []), "application/pkix-crl");
            var impostor = CadenaDePruebaHelper.GenerarRaiz("Impostor OCSP De Prueba");
            handler.Responder(urlOcsp, req => ResponderOcsp(req, impostor, CertificateStatus.Good));
        });

        var resultado = await e.Validador.ValidarAsync(e.Hoja, DateTimeOffset.UtcNow);
        Assert.Equal(EstadoRevocacion.Unavailable, resultado.Revocacion.Ocsp);
    }

    [Fact]
    public async Task OCSP_firmado_por_un_delegado_valido_con_EKU_OCSPSigning_se_acepta()
    {
        var e = Construir(configurarRevocacion: (handler, urlCrl, urlOcsp, intermedia, hoja) =>
        {
            var delegado = CadenaDePruebaHelper.GenerarFirmanteOcspDelegado(intermedia, "OCSP Delegado De Prueba");
            handler.ResponderBytes(urlCrl, CadenaDePruebaHelper.GenerarCrl(intermedia, []), "application/pkix-crl");
            handler.Responder(urlOcsp, req => ResponderOcsp(req, delegado, CertificateStatus.Good, [delegado.CertificadoBc]));
        });

        var resultado = await e.Validador.ValidarAsync(e.Hoja, DateTimeOffset.UtcNow);
        Assert.Equal(EstadoRevocacion.Good, resultado.Revocacion.Ocsp);
        Assert.True(resultado.EstadoFinal, string.Join(" | ", resultado.Evidencia));
    }

    [Fact]
    public async Task OCSP_firmado_por_un_certificado_sin_EKU_OCSPSigning_se_descarta_aunque_lo_emitio_la_CA()
    {
        var e = Construir(configurarRevocacion: (handler, urlCrl, urlOcsp, intermedia, hoja) =>
        {
            // Certificado normal (sin id-kp-OCSPSigning) emitido por la misma
            // CA — reutiliza GenerarHoja porque produce exactamente eso.
            var certificadoSinEku = CadenaDePruebaHelper.GenerarHoja(intermedia, "Sin EKU OCSP De Prueba", urlOcsp, urlOcsp, urlCrl);
            handler.ResponderBytes(urlCrl, CadenaDePruebaHelper.GenerarCrl(intermedia, []), "application/pkix-crl");
            handler.Responder(urlOcsp, req => ResponderOcsp(req, certificadoSinEku, CertificateStatus.Good, [certificadoSinEku.CertificadoBc]));
        });

        var resultado = await e.Validador.ValidarAsync(e.Hoja, DateTimeOffset.UtcNow);
        Assert.Equal(EstadoRevocacion.Unavailable, resultado.Revocacion.Ocsp);
    }
}
