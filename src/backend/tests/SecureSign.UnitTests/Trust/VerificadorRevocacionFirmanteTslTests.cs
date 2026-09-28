using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Ocsp;
using SecureSign.Trust;
using BcCertificate = Org.BouncyCastle.X509.X509Certificate;
using NodoCadena = SecureSign.UnitTests.Trust.CadenaDePruebaHelper.NodoCadena;

namespace SecureSign.UnitTests.Trust;

/// <summary>
/// RUNBOOK.md 12.50 (informe de preauditoría INDECOPI/IOFE, hallazgo P1-02): la firma XAdES de la TSL se
/// verifica contra el ancla oficial, pero eso nunca comprobó si el certificado firmante en sí está revocado.
/// Mismo patrón que ValidadorCertificadosTests (cadena de un solo uso + HandlerHttpFalso), pero de dos niveles
/// (raíz emite directo al firmante de la TSL) porque así es como VerificadorFirmaTsl.CertificadoEmitidoPor
/// construye la cadena real (CustomRootTrust con la raíz de INDECOPI directa).
/// </summary>
public sealed class VerificadorRevocacionFirmanteTslTests
{
    private sealed record Escenario(NodoCadena Raiz, NodoCadena Firmante, X509Certificate2 FirmanteDotNet, HandlerHttpFalso Handler, string UrlCrl, string UrlOcsp);

    private static Escenario Construir(Action<HandlerHttpFalso, string, string, NodoCadena, NodoCadena>? configurarRevocacion = null)
    {
        string sufijo = Guid.NewGuid().ToString("N");
        string urlOcsp = $"https://tsl.prueba.local/ocsp-{sufijo}";
        string urlCrl = $"https://tsl.prueba.local/crl-{sufijo}.crl";

        var raiz = CadenaDePruebaHelper.GenerarRaiz($"Raiz Firmante TSL De Prueba {sufijo}");
        var firmante = CadenaDePruebaHelper.GenerarHoja(raiz, $"Firmante TSL De Prueba {sufijo}", urlOcsp, urlOcsp, urlCrl);
        var firmanteDotNet = firmante.ComoDotNet();

        var handler = new HandlerHttpFalso();
        if (configurarRevocacion is not null)
            configurarRevocacion(handler, urlCrl, urlOcsp, raiz, firmante);
        else
        {
            handler.ResponderBytes(urlCrl, CadenaDePruebaHelper.GenerarCrl(raiz, []), "application/pkix-crl");
            handler.Responder(urlOcsp, req => ResponderOcsp(req, raiz, CertificateStatus.Good));
        }

        return new Escenario(raiz, firmante, firmanteDotNet, handler, urlCrl, urlOcsp);
    }

    private static async Task<HttpResponseMessage> ResponderOcsp(HttpRequestMessage request, NodoCadena firmanteCrl, CertificateStatus estado)
    {
        byte[] cuerpo = await request.Content!.ReadAsByteArrayAsync();
        var certId = new OcspReq(cuerpo).GetRequestList()[0].GetCertID();

        var generadorBasico = new BasicOcspRespGenerator(firmanteCrl.Llaves.Public);
        generadorBasico.AddResponse(certId, estado);
        var basico = generadorBasico.Generate(new Asn1SignatureFactory("SHA256WITHRSA", firmanteCrl.Llaves.Private), Array.Empty<BcCertificate>(), DateTime.UtcNow);
        var respuesta = new OCSPRespGenerator().Generate(OCSPRespGenerator.Successful, basico);

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(respuesta.GetEncoded()) { Headers = { ContentType = new MediaTypeHeaderValue("application/ocsp-response") } },
        };
    }

    [Fact]
    public async Task Certificado_firmante_no_revocado_da_Good()
    {
        var e = Construir();
        using var http = new HttpClient(e.Handler);

        var resultado = await VerificadorRevocacionFirmanteTsl.VerificarAsync(
            e.FirmanteDotNet, e.Raiz.ComoDotNet(), new VerificadorRevocacionOcsp(http), new VerificadorRevocacionCrl(http));

        Assert.Equal(EstadoRevocacion.Good, resultado.Combinado);
    }

    [Fact]
    public async Task Certificado_firmante_revocado_por_CRL_da_Revoked()
    {
        var e = Construir((handler, urlCrl, urlOcsp, raiz, firmante) =>
        {
            handler.ResponderBytes(urlCrl, CadenaDePruebaHelper.GenerarCrl(raiz, [firmante.CertificadoBc.SerialNumber]), "application/pkix-crl");
            handler.Responder(urlOcsp, req => ResponderOcsp(req, raiz, CertificateStatus.Good));
        });
        using var http = new HttpClient(e.Handler);

        var resultado = await VerificadorRevocacionFirmanteTsl.VerificarAsync(
            e.FirmanteDotNet, e.Raiz.ComoDotNet(), new VerificadorRevocacionOcsp(http), new VerificadorRevocacionCrl(http));

        Assert.Equal(EstadoRevocacion.Revoked, resultado.Combinado);
    }

    [Fact]
    public async Task Certificado_firmante_revocado_por_OCSP_da_Revoked()
    {
        var e = Construir((handler, urlCrl, urlOcsp, raiz, _) =>
        {
            handler.ResponderBytes(urlCrl, CadenaDePruebaHelper.GenerarCrl(raiz, []), "application/pkix-crl");
            handler.Responder(urlOcsp, req => ResponderOcspRevocado(req, raiz));
        });
        using var http = new HttpClient(e.Handler);

        var resultado = await VerificadorRevocacionFirmanteTsl.VerificarAsync(
            e.FirmanteDotNet, e.Raiz.ComoDotNet(), new VerificadorRevocacionOcsp(http), new VerificadorRevocacionCrl(http));

        Assert.Equal(EstadoRevocacion.Revoked, resultado.Combinado);
    }

    private static async Task<HttpResponseMessage> ResponderOcspRevocado(HttpRequestMessage request, NodoCadena firmanteCrl)
    {
        byte[] cuerpo = await request.Content!.ReadAsByteArrayAsync();
        var certId = new OcspReq(cuerpo).GetRequestList()[0].GetCertID();

        var generadorBasico = new BasicOcspRespGenerator(firmanteCrl.Llaves.Public);
        generadorBasico.AddResponse(certId, new RevokedStatus(DateTime.UtcNow.AddDays(-1), CrlReason.KeyCompromise));
        var basico = generadorBasico.Generate(new Asn1SignatureFactory("SHA256WITHRSA", firmanteCrl.Llaves.Private), Array.Empty<BcCertificate>(), DateTime.UtcNow);
        var respuesta = new OCSPRespGenerator().Generate(OCSPRespGenerator.Successful, basico);

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(respuesta.GetEncoded()) { Headers = { ContentType = new MediaTypeHeaderValue("application/ocsp-response") } },
        };
    }

    [Fact]
    public async Task Sin_CRL_ni_OCSP_disponibles_da_Unavailable_nunca_Good()
    {
        var e = Construir((_, _, _, _, _) => { /* no se registra ninguna ruta: ambas fuentes quedan 404 */ });
        using var http = new HttpClient(e.Handler);

        var resultado = await VerificadorRevocacionFirmanteTsl.VerificarAsync(
            e.FirmanteDotNet, e.Raiz.ComoDotNet(), new VerificadorRevocacionOcsp(http), new VerificadorRevocacionCrl(http));

        Assert.Equal(EstadoRevocacion.Unavailable, resultado.Combinado);
        Assert.NotEqual(EstadoRevocacion.Good, resultado.Combinado);
    }
}
