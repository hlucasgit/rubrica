namespace SecureSign.Shared.Auth;

/// <summary>Nombres de claim propios de la plataforma, embebidos en el JWT emitido por el Gateway.</summary>
public static class ClaimsSecureSign
{
    public const string TenantId = "tenant_id";
    public const string ClienteIntegradorId = "cliente_integrador_id";
    public const string UsuarioId = "usuario_id";
    public const string Scope = "scope";
}
