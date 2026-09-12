using Microsoft.EntityFrameworkCore;
using SecureSign.Signature.Domain;

namespace SecureSign.Signature.Infrastructure.Persistence;

public sealed class SolicitudFirmaRepositoryEfCore(SignatureDbContext db) : ISolicitudFirmaRepository
{
    public async Task AgregarAsync(SolicitudFirma solicitud, CancellationToken ct = default)
    {
        db.SolicitudesFirma.Add(solicitud);
        await db.SaveChangesAsync(ct);
    }

    public Task<SolicitudFirma?> ObtenerPorIdAsync(Guid tenantId, Guid solicitudId, CancellationToken ct = default)
        => db.SolicitudesFirma
            .Include(s => s.Flujos)
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Id == solicitudId, ct);

    public Task<SolicitudFirma?> ObtenerPorCodigoVerificacionAsync(string codigo, CancellationToken ct = default)
        => db.SolicitudesFirma
            .Include(s => s.Flujos)
            .FirstOrDefaultAsync(s => s.CodigoVerificacionPublico == codigo, ct);

    public async Task ActualizarAsync(SolicitudFirma solicitud, CancellationToken ct = default)
    {
        db.SolicitudesFirma.Update(solicitud);
        await db.SaveChangesAsync(ct);
    }
}
