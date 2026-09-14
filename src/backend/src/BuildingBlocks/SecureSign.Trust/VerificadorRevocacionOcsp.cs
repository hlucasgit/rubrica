using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Ocsp;
using Org.BouncyCastle.X509;

namespace SecureSign.Trust;

/// <summary>
/// Revocación vía OCSP (RFC 6960) — para RENIEC es un servicio "restringido,
/// cuyo acceso estará regulado conforme al TUPA del RENIEC" (a diferencia de
/// la CRL, de acceso libre). Mientras SecureSign no tenga ese acceso
/// autorizado, esto devolverá <see cref="EstadoRevocacion.Unavailable"/> de
/// forma consistente — que es precisamente el comportamiento correcto (NO
/// se traduce en "válido"), no un error del código. El diseño queda listo
/// para cuando exista ese acceso.
/// </summary>
public sealed class VerificadorRevocacionOcsp(HttpClient http)
{
    public async Task<(EstadoRevocacion Estado, string Detalle)> VerificarAsync(
        X509Certificate2 certificado, X509Certificate2 emisor, CancellationToken ct = default)
    {
        string? url = ExtensionesX509.ObtenerUrlOcsp(certificado);
        if (url is null)
            return (EstadoRevocacion.Unavailable, "El certificado no declara ningún responder OCSP (AIA, método 1.3.6.1.5.5.7.48.1).");

        try
        {
            var parser = new X509CertificateParser();
            var emisorBc = parser.ReadCertificate(emisor.RawData);

            // certificado.SerialNumber ya es hexadecimal en orden "de lectura" (big-endian) —
            // a diferencia de GetSerialNumber(), que .NET devuelve en little-endian por
            // razones históricas de interop con CryptoAPI; usar el string evita esa trampa.
            var serial = new BigInteger(certificado.SerialNumber, 16);
            var certId = new CertificateID(CertificateID.DigestSha1, emisorBc, serial);
            var generador = new OcspReqGenerator();
            generador.AddRequest(certId);
            byte[] cuerpoSolicitud = generador.Generate().GetEncoded();

            using var contenido = new ByteArrayContent(cuerpoSolicitud);
            contenido.Headers.ContentType = new MediaTypeHeaderValue("application/ocsp-request");

            using var respuesta = await http.PostAsync(url, contenido, ct);
            if (!respuesta.IsSuccessStatusCode)
                return (EstadoRevocacion.Unavailable, $"El responder OCSP {url} devolvió HTTP {(int)respuesta.StatusCode}.");

            byte[] cuerpoRespuesta = await respuesta.Content.ReadAsByteArrayAsync(ct);
            var ocspResp = new OcspResp(cuerpoRespuesta);
            if (ocspResp.Status != OcspRespStatus.Successful)
                return (EstadoRevocacion.Unavailable, $"El responder OCSP {url} devolvió estado {ocspResp.Status}, no Successful.");

            var basica = (BasicOcspResp)ocspResp.GetResponseObject();
            var individual = basica.Responses.FirstOrDefault(r => r.GetCertID().Equals(certId));
            if (individual is null)
                return (EstadoRevocacion.Unavailable, $"El responder OCSP {url} no incluyó una respuesta para este certificado.");

            return individual.GetCertStatus() switch
            {
                null => (EstadoRevocacion.Good, $"OCSP {url}: certificado no revocado."),
                RevokedStatus revocado => (EstadoRevocacion.Revoked, $"OCSP {url}: revocado el {revocado.RevocationTime:o}."),
                _ => (EstadoRevocacion.Unknown, $"OCSP {url}: el responder no tiene información sobre este certificado (unknown).")
            };
        }
        catch (Exception ex)
        {
            return (EstadoRevocacion.Unavailable, $"No se pudo completar la consulta OCSP a {url}: {ex.Message}");
        }
    }
}
