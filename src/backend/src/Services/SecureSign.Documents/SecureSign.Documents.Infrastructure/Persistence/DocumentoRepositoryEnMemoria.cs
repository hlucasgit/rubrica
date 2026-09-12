using System.Collections.Concurrent;
using SecureSign.Documents.Domain;

namespace SecureSign.Documents.Infrastructure.Persistence;

/// <summary>
/// Implementación en memoria para desarrollo local y pruebas.
/// La implementación de producción usa PostgreSQL/EF Core (ver docs/01-arquitectura/modelo-datos.md)
/// y NO está incluida en este scaffold — requiere DbContext, migraciones y
/// configuración de particionamiento por TenantId específicas del entorno destino.
/// </summary>
public sealed class DocumentoRepositoryEnMemoria : IDocumentoRepository
{
    private readonly ConcurrentDictionary<Guid, Documento> _documentos = new();

    public Task AgregarAsync(Documento documento, CancellationToken ct = default)
    {
        _documentos[documento.Id] = documento;
        return Task.CompletedTask;
    }

    public Task<Documento?> ObtenerPorIdAsync(Guid tenantId, Guid documentoId, CancellationToken ct = default)
    {
        _documentos.TryGetValue(documentoId, out var documento);
        return Task.FromResult(documento?.TenantId == tenantId ? documento : null);
    }

    public Task<Documento?> ObtenerPorHashAsync(Guid tenantId, string hashSha256, CancellationToken ct = default)
    {
        var documento = _documentos.Values.FirstOrDefault(d => d.TenantId == tenantId && d.Hash.ValorHex == hashSha256);
        return Task.FromResult(documento);
    }

    public Task ActualizarAsync(Documento documento, CancellationToken ct = default)
    {
        _documentos[documento.Id] = documento;
        return Task.CompletedTask;
    }
}
