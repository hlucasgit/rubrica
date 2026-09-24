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
    /// SOLO servicios emisores (los que piden tokens al Gateway): su nombre
    /// (p. ej. <c>securesign-signature-api</c>), enviado en
    /// <c>X-Internal-Service</c>. El Gateway lo usa para buscar SU entrada en
    /// <see cref="ServiciosEmisores"/> y aplicar la política de emisión de esa
    /// identidad (ver PoliticaEmisionInterna).
    /// </summary>
    public string NombreServicio { get; set; } = default!;

    /// <summary>
    /// SOLO servicios emisores: el secreto PROPIO de este servicio, no
    /// compartido con ningún otro (RUNBOOK.md 12.28). Autentica la llamada a
    /// <c>POST /api/auth/interno/emitir</c>; quien lo tiene puede pedirle al
    /// Gateway un token dentro de la política de su servicio, nunca firmar
    /// nada por sí mismo ni pedir claims fuera de esa política.
    /// </summary>
    public string SecretoClienteInterno { get; set; } = default!;

    /// <summary>
    /// SOLO el Gateway: catálogo de servicios autorizados a pedir tokens, con
    /// sus secretos y permisos. Rotación sin corte de un servicio: agregar el
    /// secreto nuevo a <see cref="ServicioEmisorOpciones.Secretos"/> junto al
    /// viejo, cambiar <see cref="SecretoClienteInterno"/> en ese servicio, y
    /// retirar el viejo (RUNBOOK.md 12.27/12.28).
    /// </summary>
    public List<ServicioEmisorOpciones> ServiciosEmisores { get; set; } = [];

    /// <summary>Todos los secretos internos no vacíos que esta instancia usa o acepta.</summary>
    public IEnumerable<string> SecretosInternosAceptados() =>
        new[] { SecretoClienteInterno }
            .Concat(ServiciosEmisores.SelectMany(s => s.Secretos))
            .Where(s => !string.IsNullOrEmpty(s));

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

/// <summary>Un servicio interno autorizado a pedirle tokens al Gateway.</summary>
public sealed class ServicioEmisorOpciones
{
    public string Nombre { get; set; } = default!;

    /// <summary>Secretos vigentes de este servicio (más de uno solo durante una rotación).</summary>
    public string[] Secretos { get; set; } = [];

    /// <summary>Solo Signature.Api: puede pedir tickets del Firmador Local (audiencia externa, scope firmar:local).</summary>
    public bool PuedeEmitirTickets { get; set; }
}
