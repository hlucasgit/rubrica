using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SecureSign.Domain.Primitives;

namespace SecureSign.Evidence.Domain;

public enum TipoEvidencia
{
    Carga,
    Visualizacion,
    ValidacionIdentidad,
    Firma,
    Rechazo,
    RevalidacionPorContinuidad
}

/// <summary>
/// Un eslabón de la cadena de evidencia (innovación #1, ver
/// docs/02-innovacion-patente/analisis-innovaciones.md). Cada evento incorpora
/// el hash del evento inmediatamente anterior DEL MISMO TENANT, de modo que
/// alterar retroactivamente un evento rompe visiblemente la cadena posterior
/// — sin necesidad de una autoridad central que garantice la integridad.
/// </summary>
public sealed class EventoEvidencia : Entity
{
    public Guid TenantId { get; private set; }
    public Guid DocumentoId { get; private set; }
    public Guid? FirmaId { get; private set; }
    public TipoEvidencia TipoEvidencia { get; private set; }
    public string? HashEventoAnterior { get; private set; }
    public string HashEvento { get; private set; } = default!;
    public DatosContextuales DatosContextuales { get; private set; } = default!;
    public DateTimeOffset RegistradoEn { get; private set; }

    private EventoEvidencia() { }

    public static EventoEvidencia Crear(
        Guid tenantId,
        Guid documentoId,
        TipoEvidencia tipoEvidencia,
        DatosContextuales datosContextuales,
        string? hashEventoAnterior,
        Guid? firmaId = null)
    {
        var evento = new EventoEvidencia
        {
            TenantId = tenantId,
            DocumentoId = documentoId,
            FirmaId = firmaId,
            TipoEvidencia = tipoEvidencia,
            DatosContextuales = datosContextuales,
            HashEventoAnterior = hashEventoAnterior,
            // Truncado a precisión de microsegundo (no solo redondeado): es la
            // precisión máxima que conserva una columna PostgreSQL
            // "timestamp with time zone". Si el hash se calculara sobre los
            // ticks completos (100ns) y luego el valor se releyera desde la
            // base de datos ya truncado, EsConsistenteCon() recalcularía un
            // hash distinto y reportaría manipulación donde no la hubo —
            // encontrado precisamente al probar contra PostgreSQL real, no
            // con el repositorio en memoria.
            RegistradoEn = TruncarAMicrosegundos(DateTimeOffset.UtcNow)
        };

        evento.HashEvento = evento.CalcularHashPropio();
        return evento;
    }

    private static DateTimeOffset TruncarAMicrosegundos(DateTimeOffset valor)
    {
        const long ticksPorMicrosegundo = 10; // 1 tick = 100ns
        var ticksTruncados = valor.Ticks - (valor.Ticks % ticksPorMicrosegundo);
        return new DateTimeOffset(ticksTruncados, valor.Offset);
    }

    /// <summary>
    /// HashEvento(n) = SHA256( Datos(n) || HashEvento(n-1) || Timestamp(n) )
    /// — ver docs/02-innovacion-patente/documento-tecnico-patente.md sección 6.1.
    /// </summary>
    private string CalcularHashPropio()
    {
        var payload = new
        {
            TenantId,
            DocumentoId,
            FirmaId,
            TipoEvidencia = TipoEvidencia.ToString(),
            DatosContextuales,
            RegistradoEn
        };

        var json = JsonSerializer.Serialize(payload);
        var entrada = $"{HashEventoAnterior ?? string.Empty}|{json}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(entrada));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Verifica que este eslabón sea consistente con el hash del evento anterior
    /// dado, y que su propio HashEvento no haya sido alterado. Usado por el
    /// verificador de cadena (VerificadorCadenaEvidencia) y por el endpoint
    /// público de validación.
    /// </summary>
    public bool EsConsistenteCon(string? hashEventoAnteriorEsperado)
    {
        if (HashEventoAnterior != hashEventoAnteriorEsperado) return false;
        return HashEvento == CalcularHashPropio();
    }
}

/// <summary>
/// Metadatos de contexto capturados por evento: IP, dispositivo, navegador,
/// geolocalización opcional. Deliberadamente NO incluye contenido semántico
/// de interacción del usuario (ver docs/03-legal-normativo, minimización de datos).
///
/// Se persiste completo como JSON (ver EvidenceDbContext) — por eso
/// Geolocalizacion es un record propio (GeoPunto) y no una tupla: las tuplas
/// de C# no tienen una representación estable en System.Text.Json.
/// </summary>
public sealed record DatosContextuales(
    string? IpOrigen,
    string? UserAgent,
    string? Dispositivo,
    GeoPunto? Geolocalizacion,
    IReadOnlyDictionary<string, string>? Extra = null);

public sealed record GeoPunto(double Lat, double Lon);
