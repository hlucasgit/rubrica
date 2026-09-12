using Microsoft.EntityFrameworkCore;
using SecureSign.Identity.Domain;

namespace SecureSign.Identity.Infrastructure.Persistence;

public sealed class UsuarioIdentidadRepositoryEfCore(IdentityDbContext db) : IUsuarioIdentidadRepository
{
    public Task<UsuarioIdentidad?> ObtenerAsync(Guid tenantId, Guid usuarioId, CancellationToken ct = default)
        => db.UsuariosIdentidad.FirstOrDefaultAsync(u => u.TenantId == tenantId && u.UsuarioId == usuarioId, ct);

    public async Task AgregarAsync(UsuarioIdentidad usuario, CancellationToken ct = default)
    {
        db.UsuariosIdentidad.Add(usuario);
        await db.SaveChangesAsync(ct);
    }

    public async Task ActualizarAsync(UsuarioIdentidad usuario, CancellationToken ct = default)
    {
        db.UsuariosIdentidad.Update(usuario);
        await db.SaveChangesAsync(ct);
    }
}
