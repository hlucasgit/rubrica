namespace SecureSign.Signature.Application.Clients;

public sealed record FirmaCriptografica(string FirmaBase64, string Algoritmo, string ReferenciaLlaveUsada);

public interface ICriptografiaServiceClient
{
    /// <summary>Obtiene (generando si es la primera vez) la referencia de llave del firmante.</summary>
    Task<string> ObtenerOGenerarLlaveAsync(Guid usuarioId, CancellationToken ct = default);

    /// <param name="pin">
    /// Requerido cuando Criptografía usa el proveedor PKCS#11 (tarjeta
    /// real); ignorado con el proveedor de software. Viaja solo dentro de
    /// esta llamada puntual — nunca se persiste ni se loguea.
    /// </param>
    Task<FirmaCriptografica> FirmarAsync(string referenciaLlave, string hashDocumentoHex, string? pin = null, CancellationToken ct = default);

    /// <summary>
    /// Modo masivo: firma N hashes con una sola credencial. Todas las
    /// operaciones deben corresponder al mismo firmante (misma referencia de
    /// llave) — ver IProveedorCriptografico.FirmarLoteAsync. La respuesta
    /// preserva el orden de <paramref name="operaciones"/>.
    /// </summary>
    Task<IReadOnlyList<FirmaCriptografica>> FirmarLoteAsync(
        IReadOnlyList<(string ReferenciaLlave, string HashDocumentoHex)> operaciones, string? pin = null, CancellationToken ct = default);
}
