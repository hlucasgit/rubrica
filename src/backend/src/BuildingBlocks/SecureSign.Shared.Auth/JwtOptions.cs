namespace SecureSign.Shared.Auth;

/// <summary>
/// Configuración del emisor/validador JWT compartido entre el Gateway (emite)
/// y los servicios de dominio (validan). En este scaffold se usa una llave
/// simétrica HS256 leída de configuración/variable de entorno para que el
/// sistema sea operativo de punta a punta sin depender de un IdP externo.
///
/// PRODUCCIÓN: reemplazar por un Identity Provider real (Keycloak, Duende
/// IdentityServer, Azure AD B2C) con llaves asimétricas (RS256) y rotación
/// de llaves vía JWKS — ver docs/07-seguridad/modelo-seguridad.md sección 6.
/// </summary>
public sealed class JwtOptions
{
    public const string SeccionConfiguracion = "Jwt";

    public string Issuer { get; set; } = "https://api.securesign.pe";
    public string Audience { get; set; } = "securesign-platform";

    /// <summary>
    /// Audiencia de los tokens de intercambio (RFC 8693) usados exclusivamente
    /// en llamadas servicio-a-servicio (ver TokenExchangeService). Un servicio
    /// que solo atiende tráfico externo vía Gateway nunca ve esta audiencia
    /// en la práctica, pero todos los servicios la aceptan como válida porque
    /// cualquiera de ellos puede ser llamado internamente por otro (p. ej.
    /// Documentos y Evidencia son llamados por Firma).
    /// </summary>
    public string InternalAudience { get; set; } = "securesign-internal-services";

    /// <summary>Llave simétrica compartida. Mínimo 32 bytes (256 bits) para HS256.</summary>
    public string SigningKey { get; set; } = default!;

    public int MinutosExpiracion { get; set; } = 60;

    /// <summary>Vida del token de intercambio interno — deliberadamente corta (RFC 8693 recomienda tokens de vida acotada para reducir la ventana de uso indebido si se filtran).</summary>
    public int MinutosExpiracionInterno { get; set; } = 2;
}
