using System.Security.Claims;

namespace SecureSign.Shared.Auth;

/// <summary>
/// Contexto de tenant/actor resuelto a partir de los claims del JWT validado
/// por el Gateway. Cada servicio de dominio confía en estos claims porque el
/// JWT llega firmado por el emisor compartido (ver JwtOptions) — nunca se
/// acepta un TenantId enviado en el cuerpo de la petición.
/// </summary>
public sealed record TenantContext(Guid TenantId, Guid? UsuarioId, Guid? ClienteIntegradorId, IReadOnlyList<string> Scopes)
{
    public bool TieneScope(string scope) => Scopes.Contains(scope);
}

public static class ClaimsPrincipalExtensions
{
    public static TenantContext ObtenerTenantContext(this ClaimsPrincipal user)
    {
        var tenantIdRaw = user.FindFirstValue(ClaimsSecureSign.TenantId)
            ?? throw new InvalidOperationException("El token no contiene el claim tenant_id.");

        var usuarioIdRaw = user.FindFirstValue(ClaimsSecureSign.UsuarioId);
        var clienteIntegradorRaw = user.FindFirstValue(ClaimsSecureSign.ClienteIntegradorId);
        var scopeRaw = user.FindFirstValue(ClaimsSecureSign.Scope) ?? string.Empty;

        return new TenantContext(
            Guid.Parse(tenantIdRaw),
            string.IsNullOrEmpty(usuarioIdRaw) ? null : Guid.Parse(usuarioIdRaw),
            string.IsNullOrEmpty(clienteIntegradorRaw) ? null : Guid.Parse(clienteIntegradorRaw),
            scopeRaw.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
