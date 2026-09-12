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
    public string ClientSecret { get; set; } = default!;
    public Guid TenantId { get; set; }
    public Guid ClienteIntegradorId { get; set; }
    public string Scopes { get; set; } = default!;
}

public sealed class ClientesDemoOptions
{
    public const string SeccionConfiguracion = "ClientesDemo";
    public List<ClienteDemo> Clientes { get; set; } = new();
}
