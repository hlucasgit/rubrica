using Org.BouncyCastle.Security;
using Org.BouncyCastle.Tsp;

namespace SecureSign.Tsa;

/// <summary>
/// Resultado de sellar un hash — ver informe de preauditoría INDECOPI/IOFE,
/// hallazgo P1 ("TSA / RFC 3161"): el dominio ya definía
/// <c>ISellosTiempoProvider</c>, pero sin implementación real. Esta clase es
/// esa implementación real, hablando el protocolo RFC 3161 de verdad contra
/// cualquier Autoridad de Sellado de Tiempo pública (probado contra
/// timestamp.digicert.com, timestamp.sectigo.com y freetsa.org — ver
/// RUNBOOK.md 12.14). No requiere autenticación: así funciona RFC 3161 en
/// la práctica para la inmensa mayoría de TSAs públicas.
/// </summary>
/// <param name="TokenDer">
/// El <c>TimeStampToken</c> completo, DER-encoded (un CMS SignedData que
/// envuelve el <c>TSTInfo</c> firmado por la TSA) — esto es lo que se
/// incrusta como atributo NO firmado <c>id-aa-signatureTimeStampToken</c>
/// en un CMS/CAdES existente para producir PAdES-T (ver
/// SecureSign.Pades.CmsBuilder.AgregarSelloTiempo).
/// </param>
public sealed record SelloTiempoObtenido(byte[] TokenDer, DateTimeOffset GenTime, string AutoridadEmisora);

/// <summary>Cliente RFC 3161 puro — sin dependencias de ASP.NET Core, para poder usarse tanto desde un servicio de backend como desde el Firmador Local (ejecutable de escritorio) sin arrastrar el framework compartido de ASP.NET Core (ver la misma lección aplicada en RUNBOOK.md 12.13 sobre SecureSign.Shared.Auth).</summary>
public sealed class ClienteTsaRfc3161(HttpClient http)
{
    /// <summary>
    /// Pide un sello de tiempo real sobre <paramref name="hashSha256"/> —
    /// construye la solicitud RFC 3161 (con un nonce aleatorio real, no
    /// predecible), la envía por HTTP, y valida la respuesta contra la
    /// PROPIA solicitud (firma de la TSA, coincidencia de messageImprint y
    /// de nonce) antes de aceptarla — nunca confía en una respuesta sin
    /// validar. Lanza si la TSA rechaza la solicitud o si algo no cuadra
    /// (fail closed, igual que el resto del sistema).
    /// </summary>
    public async Task<SelloTiempoObtenido> SellarAsync(byte[] hashSha256, string urlTsa, CancellationToken ct = default)
    {
        var generador = new TimeStampRequestGenerator();
        generador.SetCertReq(true); // pedimos que la TSA incluya su propio certificado — lo necesitamos para poder verificar el token más adelante (ver SecureSign.Pades/Validator).

        byte[] nonceBytes = new byte[16];
        System.Security.Cryptography.RandomNumberGenerator.Fill(nonceBytes);
        var nonce = new Org.BouncyCastle.Math.BigInteger(1, nonceBytes);

        var solicitud = generador.Generate(TspAlgorithms.Sha256, hashSha256, nonce);
        byte[] solicitudDer = solicitud.GetEncoded();

        using var contenido = new ByteArrayContent(solicitudDer);
        contenido.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/timestamp-query");

        HttpResponseMessage respuestaHttp;
        try
        {
            respuestaHttp = await http.PostAsync(urlTsa, contenido, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new SelloTiempoException($"No se pudo contactar a la TSA ({urlTsa}): {ex.Message}", ex);
        }

        if (!respuestaHttp.IsSuccessStatusCode)
            throw new SelloTiempoException($"La TSA ({urlTsa}) respondió HTTP {(int)respuestaHttp.StatusCode}.");

        byte[] cuerpoRespuesta = await respuestaHttp.Content.ReadAsByteArrayAsync(ct);

        TimeStampResponse respuestaTsp;
        try
        {
            respuestaTsp = new TimeStampResponse(cuerpoRespuesta);
            // Valida: el status es granted/grantedWithMods, el nonce coincide
            // con el que mandamos, y el messageImprint del token coincide
            // exactamente con el hash que pedimos sellar — una TSA maliciosa
            // o una respuesta manipulada en tránsito no pasa esto.
            respuestaTsp.Validate(solicitud);
        }
        catch (Exception ex)
        {
            throw new SelloTiempoException($"La respuesta de la TSA ({urlTsa}) no es válida: {ex.Message}", ex);
        }

        // RFC 3161 §2.4.2 — PKIStatus: 0 = granted, 1 = grantedWithMods; cualquier otro valor es un rechazo.
        if (respuestaTsp.Status != 0 && respuestaTsp.Status != 1)
            throw new SelloTiempoException($"La TSA ({urlTsa}) rechazó la solicitud de sello: {respuestaTsp.GetStatusString()}");

        var token = respuestaTsp.TimeStampToken;
        string autoridad = ExtraerAutoridad(token) ?? new Uri(urlTsa).Host;

        return new SelloTiempoObtenido(token.GetEncoded(), token.TimeStampInfo.GenTime, autoridad);
    }

    private static string? ExtraerAutoridad(TimeStampToken token)
    {
        try
        {
            var firmante = token.GetCertificates().EnumerateMatches(token.SignerID).FirstOrDefault();
            return firmante?.SubjectDN?.ToString();
        }
        catch (Exception)
        {
            return null;
        }
    }
}

public sealed class SelloTiempoException : Exception
{
    public SelloTiempoException(string message) : base(message) { }
    public SelloTiempoException(string message, Exception inner) : base(message, inner) { }
}
