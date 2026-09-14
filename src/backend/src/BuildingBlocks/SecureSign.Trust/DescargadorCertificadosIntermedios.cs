using System.Collections.Concurrent;
using System.Security.Cryptography.X509Certificates;

namespace SecureSign.Trust;

/// <summary>
/// Descarga (y cachea en memoria del proceso) los certificados intermedios
/// que le falten a una cadena, siguiendo el enlace "CA Issuers" de
/// Authority Information Access (RFC 5280 §4.2.2.1) de cada certificado —
/// el mismo mecanismo que cualquier navegador usa para completar cadenas
/// incompletas. Se detiene al llegar a una raíz autofirmada o a la
/// profundidad máxima, lo que ocurra primero.
/// </summary>
public sealed class DescargadorCertificadosIntermedios(HttpClient http)
{
    private readonly ConcurrentDictionary<string, X509Certificate2?> _cache = new();

    public async Task<IReadOnlyList<X509Certificate2>> DescargarCadenaAsync(
        X509Certificate2 hoja, int profundidadMaxima = 5, CancellationToken ct = default)
    {
        var resultado = new List<X509Certificate2>();
        var actual = hoja;

        for (int i = 0; i < profundidadMaxima; i++)
        {
            if (actual.Subject == actual.Issuer) break; // autofirmado: no hay emisor que buscar.

            string? url = ExtensionesX509.ObtenerUrlEmisorCa(actual);
            if (url is null) break;

            var emisor = await ObtenerOCachearAsync(url, ct);
            if (emisor is null) break;

            resultado.Add(emisor);
            actual = emisor;
        }

        return resultado;
    }

    private async Task<X509Certificate2?> ObtenerOCachearAsync(string url, CancellationToken ct)
    {
        if (_cache.TryGetValue(url, out var cacheado)) return cacheado;

        X509Certificate2? descargado;
        try
        {
            byte[] bytes = await http.GetByteArrayAsync(url, ct);
            descargado = new X509Certificate2(bytes);
        }
        catch
        {
            descargado = null;
        }

        _cache[url] = descargado;
        return descargado;
    }
}
