// SecureSign .NET SDK (referencia) — cliente delgado sobre la API REST
// documentada en docs/05-integracion/manual-integracion-api.md.
//
// Este archivo es una referencia de diseño del SDK oficial; para producción
// debe empaquetarse como proyecto NuGet independiente (SecureSign.Sdk) con
// versionado semántico propio, separado del backend (src/backend).

using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace SecureSign.Sdk;

public sealed class SecureSignClient
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _tenant;
    private string? _accessToken;
    private DateTimeOffset _expiraEn;

    public DocumentosResource Documentos { get; }
    public FirmasResource Firmas { get; }

    public SecureSignClient(string apiKey, string tenant, string baseUrl = "https://api.securesign.pe/v1")
    {
        _apiKey = apiKey;
        _tenant = tenant;
        _http = new HttpClient { BaseAddress = new Uri(baseUrl) };

        Documentos = new DocumentosResource(this);
        Firmas = new FirmasResource(this);
    }

    internal async Task<HttpClient> ObtenerClienteAutenticadoAsync(CancellationToken ct)
    {
        if (_accessToken is null || DateTimeOffset.UtcNow >= _expiraEn)
        {
            var respuesta = await _http.PostAsync("/auth/token", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _tenant,
                ["client_secret"] = _apiKey
            }), ct);
            respuesta.EnsureSuccessStatusCode();

            var token = await respuesta.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: ct)
                ?? throw new InvalidOperationException("Respuesta de token inválida.");

            _accessToken = token.access_token;
            _expiraEn = DateTimeOffset.UtcNow.AddSeconds(token.expires_in - 30);
        }

        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        return _http;
    }

    private sealed record TokenResponse(string access_token, int expires_in);
}

public sealed record Firmante(string Nombre, string DocumentoIdentidad, string Correo, int Orden);

public sealed record SolicitudFirmaRequest
{
    public required Guid DocumentoId { get; init; }
    public required string TipoFirma { get; init; }
    public bool RequiereOrdenSecuencial { get; init; }
    public required IReadOnlyList<Firmante> Firmantes { get; init; }
    public DateTimeOffset? FechaLimite { get; init; }
}

public sealed record SolicitudFirmaResponse(Guid SolicitudFirmaId, string CodigoVerificacionPublico, string UrlFirma);
public sealed record RegistrarDocumentoResponse(Guid IdDocumento, string HashDocumento, string Estado);

public sealed class DocumentosResource(SecureSignClient client)
{
    public async Task<RegistrarDocumentoResponse> RegistrarAsync(string rutaArchivo, string? codigoExterno = null, CancellationToken ct = default)
    {
        var http = await client.ObtenerClienteAutenticadoAsync(ct);

        using var contenido = new MultipartFormDataContent();
        using var stream = File.OpenRead(rutaArchivo);
        contenido.Add(new StreamContent(stream), "archivo", Path.GetFileName(rutaArchivo));
        if (codigoExterno is not null) contenido.Add(new StringContent(codigoExterno), "codigoExterno");

        var respuesta = await http.PostAsync("/documentos", contenido, ct);
        respuesta.EnsureSuccessStatusCode();
        return await respuesta.Content.ReadFromJsonAsync<RegistrarDocumentoResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Respuesta inválida al registrar documento.");
    }
}

public sealed class FirmasResource(SecureSignClient client)
{
    public async Task<SolicitudFirmaResponse> CrearSolicitudAsync(SolicitudFirmaRequest request, CancellationToken ct = default)
    {
        var http = await client.ObtenerClienteAutenticadoAsync(ct);
        var respuesta = await http.PostAsJsonAsync("/firmas/solicitudes", request, ct);
        respuesta.EnsureSuccessStatusCode();
        return await respuesta.Content.ReadFromJsonAsync<SolicitudFirmaResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Respuesta inválida al crear la solicitud de firma.");
    }
}
