using SecureSign.Documents.Application.RegistrarDocumento;

namespace SecureSign.Documents.Infrastructure.Storage;

/// <summary>
/// Implementación de referencia que persiste en disco local, únicamente para
/// desarrollo. La implementación de producción debe usar almacenamiento de
/// objetos S3-compatible con cifrado del lado del servidor (ver docs/07-seguridad).
/// </summary>
public sealed class AlmacenamientoDocumentalLocal(string rutaBase) : IAlmacenamientoDocumental
{
    public async Task<string> GuardarAsync(Guid tenantId, string nombreArchivo, byte[] contenido, CancellationToken ct = default)
    {
        var directorioTenant = Path.Combine(rutaBase, tenantId.ToString());
        Directory.CreateDirectory(directorioTenant);

        var nombreUnico = $"{Guid.NewGuid()}_{Path.GetFileName(nombreArchivo)}";
        var rutaCompleta = Path.Combine(directorioTenant, nombreUnico);

        await File.WriteAllBytesAsync(rutaCompleta, contenido, ct);
        return rutaCompleta;
    }

    public Task<byte[]> LeerAsync(string urlAlmacenamiento, CancellationToken ct = default)
        => File.ReadAllBytesAsync(urlAlmacenamiento, ct);
}
