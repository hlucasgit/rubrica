// SecureSign .NET SDK — cliente real sobre la API REST documentada en
// docs/05-integracion/manual-integracion-api.md, auditado línea por línea
// contra src/backend/src/Gateway y los controladores reales de
// src/backend/src/Services/*, y probado en la práctica siguiendo el mismo
// flujo que src/backend/ejemplos-integracion/firmar-documento.sh (la fuente
// de verdad del manual). Reescrito para cerrar el hallazgo P0-05 del informe
// de preauditoría INDECOPI/IOFE del 27/09/2026 — la versión anterior de este
// archivo describía una API distinta (/v1, apiKey único, Firmante con
// nombre/documento/correo) que nunca existió.
//
// Este archivo es una referencia de diseño de un SDK real; para producción
// conviene empaquetarlo como proyecto NuGet independiente (SecureSign.Sdk)
// con versionado semántico propio, separado del backend (src/backend) —
// esa empaquetadura queda fuera de este cambio.

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SecureSign.Sdk;

/// <summary>
/// Tipo de firma — ver docs/03-legal-normativo/marco-legal-peru.md. Solo
/// <see cref="Digital"/> goza de presunción legal de autenticidad e
/// integridad sin prueba adicional (Ley N.º 27269).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TipoFirma { Simple, Avanzada, Digital }

/// <summary>Estado de una solicitud de firma o de un firmante dentro de ella (mismos valores en ambos casos).</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EstadoFirma { Pendiente, Visualizado, ValidandoIdentidad, Firmado, Rechazado, Cancelado, Expirado }

/// <summary>
/// Excepción que envuelve el formato real de error de la API
/// (<c>{ "error": "...", "mensaje": "..." }</c>, manual capítulo 6).
/// <see cref="ReintentarEn"/> viene del encabezado <c>Retry-After</c> cuando
/// el Gateway responde 429 (límite de tasa de <c>/api/auth/token</c>, manual
/// capítulo 3).
/// </summary>
public sealed class SecureSignException(string mensaje, string? codigo, int statusCode, TimeSpan? reintentarEn = null)
    : Exception(mensaje)
{
    public string? Codigo { get; } = codigo;
    public int StatusCode { get; } = statusCode;
    public TimeSpan? ReintentarEn { get; } = reintentarEn;
}

public sealed record RegistrarDocumentoResponse(Guid IdDocumento, string HashDocumento, string Estado);
public sealed record MetadataDocumentoResponse(Guid IdDocumento, string Estado, string? CodigoExterno);

public sealed record Firmante(Guid UsuarioId, int Orden);

public sealed record CrearSolicitudFirmaRequest
{
    public required Guid DocumentoId { get; init; }
    public required TipoFirma TipoFirma { get; init; }
    public bool RequiereOrdenSecuencial { get; init; }
    public required IReadOnlyList<Firmante> Firmantes { get; init; }
    public DateTimeOffset? FechaLimite { get; init; }
}

public sealed record SolicitudFirmaResponse(Guid SolicitudFirmaId, string CodigoVerificacionPublico, string UrlFirma);

public sealed record EstadoFirmanteResponse(Guid FlujoFirmaId, Guid FirmanteUsuarioId, int Orden, EstadoFirma Estado);

public sealed record EstadoSolicitudResponse(
    Guid SolicitudFirmaId,
    Guid DocumentoId,
    EstadoFirma Estado,
    string CodigoVerificacionPublico,
    IReadOnlyList<EstadoFirmanteResponse> Firmantes);

public sealed record PosicionFirma(int NumeroPagina, double X, double Y, double Ancho, double Alto);

/// <summary>Resultado de descargar el documento firmado — bytes más los encabezados que el Gateway agrega (manual 5.8).</summary>
public sealed record DocumentoFirmadoResponse(byte[] Contenido, string? ContentType, string? HashDocumento, string? EvidenciaUrl);

public sealed record ValidacionPublicaFirmante(int Orden, EstadoFirma Estado);
public sealed record ValidacionPublicaResponse(bool DocumentoValido, EstadoFirma Estado, IReadOnlyList<ValidacionPublicaFirmante> Firmantes);

/// <summary>Expediente completo de UNA firma /Sig del PDF — ver <c>POST /api/validador/pdf</c>, manual 5.9.</summary>
public sealed record FirmaValidadaDto(
    string? NombreFirmante,
    bool FirmaCriptograficaValida,
    string? CertificadoSujeto,
    string? CertificadoEmisor,
    DateTimeOffset? CertificadoVigenteDesde,
    DateTimeOffset? CertificadoVigenteHasta,
    DateTimeOffset? InstanteFirmaDeclarado,
    bool InstanteFirmaConfiable,
    DateTimeOffset? InstanteSelloTiempo,
    string? SelloTiempoAutoridad,
    bool CertificadoVigente,
    bool CadenaValida,
    bool RaizConfiableIofe,
    bool PropositoValido,
    string EstadoRevocacionOcsp,
    string EstadoRevocacionCrl,
    string EstadoRevocacionCombinado,
    bool EstadoFinal,
    IReadOnlyList<string> Evidencia,
    string? Error,
    IReadOnlyList<string> ExtendedKeyUsages,
    IReadOnlyList<string> PoliticasCertificado,
    string EstadoPolitica);

public sealed record SelloArchivoDto(bool Valido, DateTimeOffset? GenTime, string? AutoridadTsa, string? Error);

public sealed record ResultadoValidarPadesResponse(
    int TotalFirmas,
    bool DocumentoValido,
    IReadOnlyList<FirmaValidadaDto> Firmas,
    IReadOnlyList<SelloArchivoDto> SellosDeArchivo);

/// <summary>
/// Cliente OAuth2 (client_credentials) + recursos de la API real de
/// SecureSign. Ver el ejemplo funcional al final del archivo.
/// </summary>
public sealed class SecureSignClient : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _expiraEn;

    public DocumentosResource Documentos { get; }
    public FirmasResource Firmas { get; }
    public IdentidadResource Identidad { get; }
    public ValidacionResource Validacion { get; }

    /// <param name="clientId">Asignado por el operador de SecureSign — no hay autoservicio de alta (manual capítulo 3).</param>
    /// <param name="clientSecret">Nunca exponerlo en frontend/móvil — solo desde el backend del sistema integrador.</param>
    /// <param name="baseUrl">URL del Gateway. Por defecto, la de un stack Docker local (<c>docker-compose.yml</c>); no existe una URL de producción pública todavía.</param>
    /// <param name="timeout">Timeout por petición HTTP (por defecto 30s).</param>
    public SecureSignClient(string clientId, string clientSecret, string baseUrl = "http://localhost:8080", TimeSpan? timeout = null)
    {
        if (string.IsNullOrWhiteSpace(clientId)) throw new ArgumentException("clientId es requerido.", nameof(clientId));
        if (string.IsNullOrWhiteSpace(clientSecret)) throw new ArgumentException("clientSecret es requerido.", nameof(clientSecret));

        _clientId = clientId;
        _clientSecret = clientSecret;
        _http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = timeout ?? TimeSpan.FromSeconds(30) };

        Documentos = new DocumentosResource(this);
        Firmas = new FirmasResource(this);
        Identidad = new IdentidadResource(this);
        Validacion = new ValidacionResource(this);
    }

    internal async Task<string> ObtenerTokenAsync(CancellationToken ct)
    {
        if (_accessToken is not null && DateTimeOffset.UtcNow < _expiraEn) return _accessToken;

        await _tokenLock.WaitAsync(ct);
        try
        {
            if (_accessToken is not null && DateTimeOffset.UtcNow < _expiraEn) return _accessToken;

            using var cuerpo = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _clientId,
                ["client_secret"] = _clientSecret,
            });
            using var respuesta = await _http.PostAsync("/api/auth/token", cuerpo, ct);
            await LanzarSiErrorAsync(respuesta, ct);

            var token = await respuesta.Content.ReadFromJsonAsync<TokenResponse>(Json, ct)
                ?? throw new SecureSignException("Respuesta de token vacía.", null, (int)respuesta.StatusCode);

            _accessToken = token.access_token;
            _expiraEn = DateTimeOffset.UtcNow.AddSeconds(Math.Max(token.expires_in - 30, 0));
            return _accessToken;
        }
        finally { _tokenLock.Release(); }
    }

    internal async Task<HttpResponseMessage> EnviarAutenticadaAsync(HttpMethod metodo, string ruta, HttpContent? cuerpo, CancellationToken ct)
    {
        var token = await ObtenerTokenAsync(ct);
        using var peticion = new HttpRequestMessage(metodo, ruta) { Content = cuerpo };
        peticion.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var respuesta = await _http.SendAsync(peticion, ct);
        await LanzarSiErrorAsync(respuesta, ct);
        return respuesta;
    }

    internal async Task<HttpResponseMessage> EnviarPublicaAsync(HttpMethod metodo, string ruta, HttpContent? cuerpo, CancellationToken ct)
    {
        using var peticion = new HttpRequestMessage(metodo, ruta) { Content = cuerpo };
        var respuesta = await _http.SendAsync(peticion, ct);
        await LanzarSiErrorAsync(respuesta, ct);
        return respuesta;
    }

    internal static async Task<T> LeerJsonAsync<T>(HttpResponseMessage respuesta, CancellationToken ct)
        => await respuesta.Content.ReadFromJsonAsync<T>(Json, ct)
           ?? throw new SecureSignException("Respuesta vacía o con formato inesperado.", null, (int)respuesta.StatusCode);

    private static async Task LanzarSiErrorAsync(HttpResponseMessage respuesta, CancellationToken ct)
    {
        if (respuesta.IsSuccessStatusCode) return;

        string? codigo = null;
        var mensaje = $"Error {(int)respuesta.StatusCode} en {respuesta.RequestMessage?.RequestUri}.";
        try
        {
            var error = await respuesta.Content.ReadFromJsonAsync<ErrorEnvelope>(Json, ct);
            if (error is not null)
            {
                codigo = error.error;
                if (!string.IsNullOrEmpty(error.mensaje)) mensaje = error.mensaje;
            }
        }
        catch { /* el cuerpo no traía el envelope { error, mensaje } esperado — se conserva el mensaje genérico */ }

        throw new SecureSignException(mensaje, codigo, (int)respuesta.StatusCode, respuesta.Headers.RetryAfter?.Delta);
    }

    public void Dispose() => _http.Dispose();

    private sealed record TokenResponse(string access_token, string token_type, int expires_in, string? scope);
    private sealed record ErrorEnvelope(string? error, string? mensaje);
}

/// <summary>Manual 5.1, 5.8, 5.11.</summary>
public sealed class DocumentosResource(SecureSignClient client)
{
    /// <summary>POST /api/documentos — multipart. No hay campo "metadata"; cualquier dato adicional lo guarda el sistema integrador.</summary>
    public async Task<RegistrarDocumentoResponse> RegistrarAsync(
        Stream archivo, string nombreArchivo, string? codigoExterno = null, Guid? usuarioSolicitanteId = null, CancellationToken ct = default)
    {
        using var contenido = new MultipartFormDataContent
        {
            { new StreamContent(archivo), "archivo", nombreArchivo },
        };
        if (codigoExterno is not null) contenido.Add(new StringContent(codigoExterno), "codigoExterno");
        if (usuarioSolicitanteId is not null) contenido.Add(new StringContent(usuarioSolicitanteId.Value.ToString()), "usuarioSolicitanteId");

        using var respuesta = await client.EnviarAutenticadaAsync(HttpMethod.Post, "/api/documentos", contenido, ct);
        return await SecureSignClient.LeerJsonAsync<RegistrarDocumentoResponse>(respuesta, ct);
    }

    /// <summary>GET /api/documentos/{id} — metadata, no el contenido.</summary>
    public async Task<MetadataDocumentoResponse> ObtenerMetadataAsync(Guid idDocumento, CancellationToken ct = default)
    {
        using var respuesta = await client.EnviarAutenticadaAsync(HttpMethod.Get, $"/api/documentos/{idDocumento}", null, ct);
        return await SecureSignClient.LeerJsonAsync<MetadataDocumentoResponse>(respuesta, ct);
    }

    /// <summary>GET /api/documentos/{id}/contenido — el archivo original, sin firmar.</summary>
    public async Task<byte[]> ObtenerContenidoAsync(Guid idDocumento, CancellationToken ct = default)
    {
        using var respuesta = await client.EnviarAutenticadaAsync(HttpMethod.Get, $"/api/documentos/{idDocumento}/contenido", null, ct);
        return await respuesta.Content.ReadAsByteArrayAsync(ct);
    }

    /// <summary>GET /api/documentos/{id}/firmado — PDF firmado (PAdES real) más X-Hash-Documento / X-Evidencia-Url (manual 5.8).</summary>
    public async Task<DocumentoFirmadoResponse> DescargarFirmadoAsync(Guid idDocumento, CancellationToken ct = default)
    {
        using var respuesta = await client.EnviarAutenticadaAsync(HttpMethod.Get, $"/api/documentos/{idDocumento}/firmado", null, ct);
        var bytes = await respuesta.Content.ReadAsByteArrayAsync(ct);
        return new DocumentoFirmadoResponse(
            bytes,
            respuesta.Content.Headers.ContentType?.ToString(),
            respuesta.Headers.TryGetValues("X-Hash-Documento", out var hash) ? hash.FirstOrDefault() : null,
            respuesta.Headers.TryGetValues("X-Evidencia-Url", out var url) ? url.FirstOrDefault() : null);
    }
}

/// <summary>Manual 5.2 a 5.7, 5.10, 5.11.</summary>
public sealed class FirmasResource(SecureSignClient client)
{
    public async Task<SolicitudFirmaResponse> CrearSolicitudAsync(CrearSolicitudFirmaRequest solicitud, CancellationToken ct = default)
    {
        using var contenido = JsonContent.Create(solicitud, options: new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var respuesta = await client.EnviarAutenticadaAsync(HttpMethod.Post, "/api/firmas/solicitudes", contenido, ct);
        return await SecureSignClient.LeerJsonAsync<SolicitudFirmaResponse>(respuesta, ct);
    }

    public async Task<EstadoSolicitudResponse> ConsultarEstadoAsync(Guid solicitudFirmaId, CancellationToken ct = default)
    {
        using var respuesta = await client.EnviarAutenticadaAsync(HttpMethod.Get, $"/api/firmas/{solicitudFirmaId}/estado", null, ct);
        return await SecureSignClient.LeerJsonAsync<EstadoSolicitudResponse>(respuesta, ct);
    }

    /// <summary>GET /api/firmas/pendientes/{firmanteId} — solicitudes pendientes de un firmante.</summary>
    public async Task<IReadOnlyList<EstadoSolicitudResponse>> ConsultarPendientesAsync(Guid firmanteId, CancellationToken ct = default)
    {
        using var respuesta = await client.EnviarAutenticadaAsync(HttpMethod.Get, $"/api/firmas/pendientes/{firmanteId}", null, ct);
        return await SecureSignClient.LeerJsonAsync<IReadOnlyList<EstadoSolicitudResponse>>(respuesta, ct);
    }

    public async Task VisualizarAsync(Guid solicitudFirmaId, Guid flujoFirmaId, CancellationToken ct = default)
    {
        using var respuesta = await client.EnviarAutenticadaAsync(HttpMethod.Post, $"/api/firmas/{solicitudFirmaId}/flujos/{flujoFirmaId}/visualizar", null, ct);
        respuesta.Dispose();
    }

    /// <summary>Coordenadas normalizadas (0..1), origen arriba-izquierda — solo PDF con firma visible (manual 5.6).</summary>
    public async Task EstablecerPosicionAsync(Guid solicitudFirmaId, Guid flujoFirmaId, PosicionFirma posicion, CancellationToken ct = default)
    {
        using var contenido = JsonContent.Create(posicion, options: new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var respuesta = await client.EnviarAutenticadaAsync(HttpMethod.Post, $"/api/firmas/{solicitudFirmaId}/flujos/{flujoFirmaId}/posicion", contenido, ct);
        respuesta.Dispose();
    }

    /// <param name="pin">Solo hace falta con un proveedor PKCS#11 real; con el proveedor de software se ignora. Nunca se persiste (manual 5.7).</param>
    public async Task FirmarAsync(Guid solicitudFirmaId, Guid flujoFirmaId, string? pin = null, CancellationToken ct = default)
    {
        using var contenido = JsonContent.Create(new { pin }, options: new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var respuesta = await client.EnviarAutenticadaAsync(HttpMethod.Post, $"/api/firmas/{solicitudFirmaId}/flujos/{flujoFirmaId}/firmar", contenido, ct);
        respuesta.Dispose();
    }

    public async Task RechazarAsync(Guid solicitudFirmaId, Guid flujoFirmaId, string motivo, CancellationToken ct = default)
    {
        using var contenido = JsonContent.Create(new { motivo }, options: new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var respuesta = await client.EnviarAutenticadaAsync(HttpMethod.Post, $"/api/firmas/{solicitudFirmaId}/flujos/{flujoFirmaId}/rechazar", contenido, ct);
        respuesta.Dispose();
    }
}

/// <summary>
/// Manual 5.5 — atajo del scaffold para disparar la señal de validación de
/// identidad que en producción dispararía el propio flujo de OTP/biometría,
/// no una llamada explícita del integrador (ver src/backend/README.md).
/// </summary>
public sealed class IdentidadResource(SecureSignClient client)
{
    public async Task EnviarSenalValidacionExitosaAsync(Guid usuarioId, CancellationToken ct = default)
    {
        using var contenido = JsonContent.Create(new { tipoSenal = "ValidacionExitosa" }, options: new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var respuesta = await client.EnviarAutenticadaAsync(HttpMethod.Post, $"/api/interno/identidad/{usuarioId}/senales", contenido, ct);
        respuesta.Dispose();
    }
}

/// <summary>Manual 5.9 — ambos endpoints son públicos, sin token.</summary>
public sealed class ValidacionResource(SecureSignClient client)
{
    /// <summary>GET /api/validacion/{codigo} — veredicto simple, sin certificados ni cadena de confianza.</summary>
    public async Task<ValidacionPublicaResponse> ValidarPorCodigoAsync(string codigoVerificacionPublico, CancellationToken ct = default)
    {
        using var respuesta = await client.EnviarPublicaAsync(HttpMethod.Get, $"/api/validacion/{codigoVerificacionPublico}", null, ct);
        return await SecureSignClient.LeerJsonAsync<ValidacionPublicaResponse>(respuesta, ct);
    }

    /// <summary>POST /api/validador/pdf — expediente PAdES completo (certificados, cadena IOFE, revocación, sello de tiempo) de un PDF, generado por SecureSign o por un tercero.</summary>
    public async Task<ResultadoValidarPadesResponse> ValidarPdfAsync(Stream pdf, string nombreArchivo = "documento.pdf", CancellationToken ct = default)
    {
        using var contenido = new MultipartFormDataContent
        {
            { new StreamContent(pdf), "documento", nombreArchivo },
        };
        using var respuesta = await client.EnviarPublicaAsync(HttpMethod.Post, "/api/validador/pdf", contenido, ct);
        return await SecureSignClient.LeerJsonAsync<ResultadoValidarPadesResponse>(respuesta, ct);
    }
}

/*
Ejemplo — mismo flujo que src/backend/ejemplos-integracion/firmar-documento.sh:

using var cliente = new SecureSign.Sdk.SecureSignClient("sgd-demo", "demo-secret-not-for-production", "http://localhost:5000");

await using var archivo = File.OpenRead("contrato.pdf");
var documento = await cliente.Documentos.RegistrarAsync(archivo, "contrato.pdf", codigoExterno: "EXP-2026-00123",
    usuarioSolicitanteId: Guid.Parse("33333333-3333-3333-3333-333333333333"));

var solicitud = await cliente.Firmas.CrearSolicitudAsync(new()
{
    DocumentoId = documento.IdDocumento,
    TipoFirma = TipoFirma.Avanzada,
    Firmantes = [new Firmante(Guid.Parse("44444444-4444-4444-4444-444444444444"), 1)],
});

var estado = await cliente.Firmas.ConsultarEstadoAsync(solicitud.SolicitudFirmaId);
var flujoId = estado.Firmantes[0].FlujoFirmaId;

await cliente.Firmas.VisualizarAsync(solicitud.SolicitudFirmaId, flujoId);
await cliente.Identidad.EnviarSenalValidacionExitosaAsync(Guid.Parse("44444444-4444-4444-4444-444444444444")); // atajo del scaffold
await cliente.Firmas.FirmarAsync(solicitud.SolicitudFirmaId, flujoId);

var firmado = await cliente.Documentos.DescargarFirmadoAsync(documento.IdDocumento);
File.WriteAllBytes("firmado.pdf", firmado.Contenido);

var validacion = await cliente.Validacion.ValidarPorCodigoAsync(solicitud.CodigoVerificacionPublico);
Console.WriteLine($"documentoValido={validacion.DocumentoValido}");
*/
