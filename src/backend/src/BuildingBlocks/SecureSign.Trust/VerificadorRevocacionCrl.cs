using System.Collections.Concurrent;
using System.Security.Cryptography.X509Certificates;
using Org.BouncyCastle.X509;

namespace SecureSign.Trust;

/// <summary>
/// Revocación vía CRL (RFC 5280) — para RENIEC es la vía SIN restricciones
/// (a diferencia de OCSP, que exige acceso regulado por su TUPA): gratuita,
/// se actualiza cada 24h, con SLA de disponibilidad publicado del 99.5%
/// anual. Una CRL de RENIEC ronda las 600-700 mil entradas y ~25 MB — se
/// descarga y parsea UNA vez por vigencia (respetando su propio
/// <c>NextUpdate</c>) y se consulta con un <see cref="HashSet{T}"/> en
/// memoria, nunca releyendo la CRL completa por cada certificado.
/// </summary>
public sealed class VerificadorRevocacionCrl(HttpClient http)
{
    private sealed record CrlCacheada(HashSet<string> SerialesRevocados, DateTimeOffset ValidaHasta, string IssuerDn, byte[] CrlDer);

    private readonly ConcurrentDictionary<string, Task<CrlCacheada?>> _cache = new();

    /// <param name="CrlDer">
    /// Los bytes DER exactos de la CRL usada para esta comprobación — solo
    /// no-null cuando <paramref name="Estado"/> es <c>Good</c> o <c>Revoked</c>
    /// (se obtuvo y verificó una CRL de verdad). Existe para que
    /// <c>PdfDssWriter</c> pueda embeber esta misma CRL como material de
    /// validación a largo plazo (PAdES-LT, RUNBOOK.md 12.24) — sin esto,
    /// el "no revocado" quedaría demostrado hoy pero sería imposible de
    /// volver a probar una vez que la CRL original ya no esté disponible.
    /// </param>
    public async Task<(EstadoRevocacion Estado, string Detalle, byte[]? CrlDer)> VerificarAsync(X509Certificate2 certificado, X509Certificate2? emisor, CancellationToken ct = default)
    {
        var urls = ExtensionesX509.ObtenerUrlsCrl(certificado);
        if (urls.Count == 0)
            return (EstadoRevocacion.Unavailable, "El certificado no declara ningún punto de distribución de CRL (2.5.29.31).", null);

        if (emisor is null)
            return (EstadoRevocacion.Unavailable, "No se pudo determinar el certificado emisor para verificar la firma de la CRL.", null);

        foreach (var url in urls)
        {
            var crl = await ObtenerOCachearAsync(url, emisor, ct);
            if (crl is null) continue; // probar el siguiente mirror

            if (DateTimeOffset.UtcNow > crl.ValidaHasta)
                return (EstadoRevocacion.Unavailable, $"La CRL de {url} está vencida (NextUpdate {crl.ValidaHasta:o}) — RENIEC publica una nueva cada 24h.", null);

            string serial = NormalizarSerial(certificado.SerialNumber);
            return crl.SerialesRevocados.Contains(serial)
                ? (EstadoRevocacion.Revoked, $"El número de serie {serial} figura en la CRL de {crl.IssuerDn} ({url}).", crl.CrlDer)
                : (EstadoRevocacion.Good, $"No revocado según la CRL de {crl.IssuerDn} ({url}), vigente hasta {crl.ValidaHasta:o}.", crl.CrlDer);
        }

        return (EstadoRevocacion.Unavailable, $"No se pudo descargar ninguna CRL de los {urls.Count} punto(s) de distribución declarados.", null);
    }

    private Task<CrlCacheada?> ObtenerOCachearAsync(string url, X509Certificate2 emisor, CancellationToken ct) =>
        _cache.AddOrUpdate(
            url,
            _ => DescargarYParsearAsync(url, emisor, ct),
            (_, tareaExistente) => EstaVigente(tareaExistente) ? tareaExistente : DescargarYParsearAsync(url, emisor, ct));

    private static bool EstaVigente(Task<CrlCacheada?> tarea) =>
        tarea.IsCompletedSuccessfully && tarea.Result is { } crl && DateTimeOffset.UtcNow <= crl.ValidaHasta;

    /// <summary>
    /// Un número de serie X.509 puede llevar (o no) un byte 0x00 inicial de
    /// relleno cuando el bit alto quedaría encendido (para que la
    /// codificación ASN.1 INTEGER siga siendo positiva) — .NET y
    /// BouncyCastle no siempre coinciden en si lo conservan al convertir a
    /// hexadecimal. Se recorta cualquier cero a la izquierda antes de
    /// comparar para no producir un falso "no revocado" por esa diferencia.
    /// </summary>
    private static string NormalizarSerial(string hex)
    {
        string mayus = hex.ToUpperInvariant();
        string sinCeros = mayus.TrimStart('0');
        return sinCeros.Length > 0 ? sinCeros : "0";
    }

    private async Task<CrlCacheada?> DescargarYParsearAsync(string url, X509Certificate2 emisor, CancellationToken ct)
    {
        try
        {
            byte[] bytes = await http.GetByteArrayAsync(url, ct);
            var crl = new X509CrlParser().ReadCrl(bytes);
            if (crl is null) return null;

            // RFC 5280 §5: nunca confiar en el contenido de una CRL sin
            // verificar que la firmó realmente el emisor del certificado
            // consultado — de lo contrario cualquiera que responda en esa
            // URL (MITM, DNS envenenado, servidor comprometido) podría forjar
            // un "no revocado". Esto NO se estaba haciendo antes.
            var emisorBc = new X509CertificateParser().ReadCertificate(emisor.RawData);
            if (!crl.IsSignatureValid(emisorBc.GetPublicKey())) return null;

            var seriales = new HashSet<string>();
            var revocados = crl.GetRevokedCertificates();
            if (revocados is not null)
            {
                foreach (var r in revocados)
                    seriales.Add(NormalizarSerial(r.SerialNumber.ToString(16)));
            }

            var validaHasta = crl.NextUpdate?.ToUniversalTime() is { } fecha
                ? new DateTimeOffset(fecha, TimeSpan.Zero)
                : DateTimeOffset.UtcNow.AddHours(1); // sin NextUpdate declarado: no cachear por mucho tiempo.

            return new CrlCacheada(seriales, validaHasta, crl.IssuerDN.ToString(), bytes);
        }
        catch
        {
            return null;
        }
    }
}
