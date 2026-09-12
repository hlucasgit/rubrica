using Microsoft.EntityFrameworkCore;
using SecureSign.Documents.Domain;
using SecureSign.Domain.ValueObjects;

namespace SecureSign.Documents.Infrastructure.Persistence;

public sealed class DocumentoRepositoryEfCore(DocumentsDbContext db) : IDocumentoRepository
{
    public async Task AgregarAsync(Documento documento, CancellationToken ct = default)
    {
        db.Documentos.Add(documento);
        await db.SaveChangesAsync(ct);
    }

    public Task<Documento?> ObtenerPorIdAsync(Guid tenantId, Guid documentoId, CancellationToken ct = default)
        => db.Documentos.FirstOrDefaultAsync(d => d.TenantId == tenantId && d.Id == documentoId, ct);

    public Task<Documento?> ObtenerPorHashAsync(Guid tenantId, string hashSha256, CancellationToken ct = default)
    {
        // Comparar el value object completo (no una sub-propiedad como
        // Hash.ValorHex): EF Core solo sabe traducir a SQL una comparación
        // sobre la propiedad mapeada en su totalidad a través del
        // conversor configurado en DocumentsDbContext.
        var hash = HashDocumental.DesdeValorConocido(hashSha256);
        return db.Documentos.FirstOrDefaultAsync(d => d.TenantId == tenantId && d.Hash == hash, ct);
    }

    public async Task ActualizarAsync(Documento documento, CancellationToken ct = default)
    {
        db.Documentos.Update(documento);
        await db.SaveChangesAsync(ct);
    }
}
