namespace SecureSign.Shared.Auth;

/// <summary>
/// Configuración del emisor/validador JWT. Desde RUNBOOK.md 12.21, el Gateway
/// es el ÚNICO que firma (llave RSA asimétrica, ver RsaKeyStore) — todos los
/// demás servicios validan contra la llave PÚBLICA publicada en
/// /.well-known/jwks.json (descubierta automáticamente vía <see cref="Authority"/>),
/// nunca vuelven a tener material de firma en su configuración. Reemplaza el
/// diseño anterior (llave simétrica HS256 idéntica copiada en cada servicio,
/// donde cualquiera de ellos podía forjar un token de cualquier otro) — ver
/// informe de preauditoría INDECOPI/IOFE, hallazgo P1 "Autenticación productiva".
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

    /// <summary>
    /// URL base del Gateway (p. ej. <c>http://gateway:8080</c> dentro de
    /// Docker) — todo servicio la usa para DOS cosas: (1) descubrir
    /// automáticamente el documento OIDC y el JWKS del Gateway para validar
    /// tokens (<c>AddJwtBearer(o =&gt; o.Authority = ...)</c>, sin ninguna
    /// llave en su propia configuración); (2) como base address del
    /// HttpClient que llama a <c>POST /api/auth/interno/emitir</c> quien
    /// necesite un token interno (ver EmisorTokenInterno).
    /// </summary>
    public string Authority { get; set; } = default!;

    /// <summary>
    /// Secreto compartido que autentica la LLAMADA a
    /// <c>POST /api/auth/interno/emitir</c> — deliberadamente de alcance
    /// mucho más angosto que la llave HS256 que reemplaza: quien lo tiene
    /// puede PEDIRLE al Gateway que firme un token, pero nunca puede firmar
    /// nada por sí mismo (el material de firma nunca sale del Gateway). Ver
    /// RUNBOOK.md 12.21, "fuera de alcance" — un secreto por servicio en vez
    /// de uno compartido queda documentado como mejora futura.
    /// </summary>
    public string SecretoClienteInterno { get; set; } = default!;

    /// <summary>
    /// Directorio donde el Gateway persiste sus llaves RSA (ver RsaKeyStore)
    /// — SOLO lo usa el Gateway. Deliberadamente fuera del repositorio y
    /// fuera de este mismo archivo de configuración: es la pieza que cierra
    /// "secretos externalizados" para el material de firma en sí.
    /// </summary>
    public string DirectorioLlaves { get; set; } = default!;

    public int MinutosExpiracion { get; set; } = 60;

    /// <summary>Vida del token de intercambio interno — deliberadamente corta (RFC 8693 recomienda tokens de vida acotada para reducir la ventana de uso indebido si se filtran).</summary>
    public int MinutosExpiracionInterno { get; set; } = 2;
}
