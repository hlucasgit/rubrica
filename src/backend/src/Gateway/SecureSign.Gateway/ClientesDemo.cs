namespace SecureSign.Gateway;

/// <summary>
/// Catálogo de clientes integradores para el entorno de desarrollo/demo,
/// leído de configuración (appsettings / variables de entorno). En
/// producción esto se reemplaza por la tabla ClientesIntegradores
/// (ver src/backend/database/schema.sql) gestionada por el futuro Servicio
/// de Tenancy/Integraciones — aquí se resuelve estáticamente para que el
/// flujo completo sea operable sin ese servicio todavía implementado.
/// </summary>
public sealed class ClienteDemo
{
    public string ClientId { get; set; } = default!;

    /// <summary>
    /// Hash(es) Argon2id del secreto (ver Argon2idSecretHasher), NUNCA el
    /// secreto en texto plano — RUNBOOK.md 12.23. Una lista, no un solo
    /// valor: permite rotar sin downtime (agregar el hash del secreto
    /// nuevo, dejar el viejo activo durante la ventana de gracia, y
    /// después quitarlo) sin necesitar ningún cambio de código ni de
    /// esquema para soportarlo.
    /// </summary>
    public List<string> SecretosHash { get; set; } = [];

    public Guid TenantId { get; set; }
    public Guid ClienteIntegradorId { get; set; }
    public string Scopes { get; set; } = default!;
}

public sealed class ClientesDemoOptions
{
    public const string SeccionConfiguracion = "ClientesDemo";
    public List<ClienteDemo> Clientes { get; set; } = new();
}
