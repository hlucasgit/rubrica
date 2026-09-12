namespace SecureSign.Documents.Domain;

public interface IDocumentoRepository
{
    Task AgregarAsync(Documento documento, CancellationToken ct = default);
    Task<Documento?> ObtenerPorIdAsync(Guid tenantId, Guid documentoId, CancellationToken ct = default);
    Task<Documento?> ObtenerPorHashAsync(Guid tenantId, string hashSha256, CancellationToken ct = default);
    Task ActualizarAsync(Documento documento, CancellationToken ct = default);
}
