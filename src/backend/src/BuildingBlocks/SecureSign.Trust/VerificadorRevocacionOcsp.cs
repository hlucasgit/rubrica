using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Ocsp;
using Org.BouncyCastle.X509;
using BcCertificate = Org.BouncyCastle.X509.X509Certificate;

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
    /// <param name="OcspRespuestaDer">
    /// Los bytes DER exactos de la respuesta OCSP completa (el <c>OCSPResponse</c>
    /// ya firmado, no solo el status) — solo no-null cuando la respuesta se
    /// obtuvo Y su firma se verificó como confiable (<see cref="FirmaEsConfiable"/>).
    /// Igual que <see cref="VerificadorRevocacionCrl"/>, existe para
    /// PAdES-LT (RUNBOOK.md 12.24) — en la práctica, RENIEC exige acceso
    /// regulado por TUPA para OCSP, así que hoy esto casi siempre es null
    /// (ver comentario de clase); el campo queda listo para cuando exista
    /// ese acceso.
    /// </param>
    public async Task<(EstadoRevocacion Estado, string Detalle, byte[]? OcspRespuestaDer)> VerificarAsync(
        X509Certificate2 certificado, X509Certificate2 emisor, CancellationToken ct = default)
    {
        string? url = ExtensionesX509.ObtenerUrlOcsp(certificado);
        if (url is null)
            return (EstadoRevocacion.Unavailable, "El certificado no declara ningún responder OCSP (AIA, método 1.3.6.1.5.5.7.48.1).", null);

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
                return (EstadoRevocacion.Unavailable, $"El responder OCSP {url} devolvió HTTP {(int)respuesta.StatusCode}.", null);

            byte[] cuerpoRespuesta = await respuesta.Content.ReadAsByteArrayAsync(ct);
            var ocspResp = new OcspResp(cuerpoRespuesta);
            if (ocspResp.Status != OcspRespStatus.Successful)
                return (EstadoRevocacion.Unavailable, $"El responder OCSP {url} devolvió estado {ocspResp.Status}, no Successful.", null);

            var basica = (BasicOcspResp)ocspResp.GetResponseObject();
            var individual = basica.Responses.FirstOrDefault(r => r.GetCertID().Equals(certId));
            if (individual is null)
                return (EstadoRevocacion.Unavailable, $"El responder OCSP {url} no incluyó una respuesta para este certificado.", null);

            // RFC 6960 §3.2: nunca confiar en el contenido de una respuesta
            // OCSP sin verificar su firma primero — de lo contrario cualquiera
            // que responda en esa URL (MITM, servidor comprometido) podría
            // forjar un "no revocado". Esto NO se estaba haciendo antes.
            if (!FirmaEsConfiable(basica, emisorBc))
                return (EstadoRevocacion.Unavailable, $"La respuesta OCSP de {url} no está firmada por el emisor ni por un firmante OCSP delegado válido — se descarta sin confiar en su contenido.", null);

            return individual.GetCertStatus() switch
            {
                null => (EstadoRevocacion.Good, $"OCSP {url}: certificado no revocado.", cuerpoRespuesta),
                RevokedStatus revocado => (EstadoRevocacion.Revoked, $"OCSP {url}: revocado el {revocado.RevocationTime:o}.", cuerpoRespuesta),
                _ => (EstadoRevocacion.Unknown, $"OCSP {url}: el responder no tiene información sobre este certificado (unknown).", null)
            };
        }
        catch (Exception ex)
        {
            return (EstadoRevocacion.Unavailable, $"No se pudo completar la consulta OCSP a {url}: {ex.Message}", null);
        }
    }

    /// <summary>
    /// Igual que <see cref="VerificarAsync"/> pero contra respuestas OCSP ya EMBEBIDAS en el documento
    /// (PAdES-LT), sin red — ver <see cref="VerificadorRevocacionCrl.VerificarEmbebida"/> para el criterio
    /// de tiempo (evaluado en <paramref name="instante"/>, no "ahora"). La firma de la respuesta se verifica
    /// con la misma regla que en la consulta en vivo (<see cref="FirmaEsConfiable"/>).
    /// </summary>
    public static (EstadoRevocacion Estado, string Detalle) VerificarEmbebida(
        X509Certificate2 certificado, X509Certificate2 emisor, IEnumerable<byte[]> ocspsDer, DateTimeOffset instante)
    {
        var emisorBc = new X509CertificateParser().ReadCertificate(emisor.RawData);
        var certId = new CertificateID(CertificateID.DigestSha1, emisorBc, new BigInteger(certificado.SerialNumber, 16));
        int descartadas = 0;

        foreach (var der in ocspsDer)
        {
            try
            {
                var ocspResp = new OcspResp(der);
                if (ocspResp.Status != OcspRespStatus.Successful) { descartadas++; continue; }

                var basica = (BasicOcspResp)ocspResp.GetResponseObject();
                var individual = basica.Responses.FirstOrDefault(r => r.GetCertID().Equals(certId));
                if (individual is null || !FirmaEsConfiable(basica, emisorBc)) { descartadas++; continue; }
                if (individual.NextUpdate?.ToUniversalTime() is { } siguiente && siguiente < instante.UtcDateTime) { descartadas++; continue; }

                return individual.GetCertStatus() switch
                {
                    null => (EstadoRevocacion.Good, "OCSP embebido: certificado no revocado (firma de la respuesta verificada)."),
                    RevokedStatus revocado => (EstadoRevocacion.Revoked, $"OCSP embebido: revocado el {revocado.RevocationTime:o}."),
                    _ => (EstadoRevocacion.Unknown, "OCSP embebido: el responder no tiene información sobre este certificado (unknown)."),
                };
            }
            catch { descartadas++; }
        }

        return (EstadoRevocacion.Unavailable, $"Ninguna de las {descartadas} respuesta(s) OCSP embebida(s) es utilizable (firma inválida, otro certificado o vencida antes de la firma).");
    }

    /// <summary>
    /// El firmante válido de una respuesta OCSP es o bien el propio emisor
    /// del certificado consultado (firma directa) o un certificado delegado,
    /// emitido por ese mismo emisor, que declare el EKU id-kp-OCSPSigning
    /// (RFC 6960 §4.2.2.2). Cualquier otra llave se descarta.
    /// </summary>
    private static bool FirmaEsConfiable(BasicOcspResp respuesta, BcCertificate emisor)
    {
        try
        {
            if (respuesta.Verify(emisor.GetPublicKey())) return true;
        }
        catch { /* no la firmó directamente el emisor: probar un firmante delegado */ }

        foreach (var candidato in respuesta.GetCerts())
        {
            try
            {
                candidato.Verify(emisor.GetPublicKey()); // ¿lo emitió el mismo emisor? lanza si no.
                var eku = candidato.GetExtendedKeyUsage();
                if (eku is not null && eku.Contains(KeyPurposeID.id_kp_OCSPSigning) && respuesta.Verify(candidato.GetPublicKey()))
                    return true;
            }
            catch { /* candidato inválido: probar el siguiente */ }
        }

        return false;
    }
}
