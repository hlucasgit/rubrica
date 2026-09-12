namespace SecureSign.Signature.Application.Clients;

public sealed record FirmaCriptografica(string FirmaBase64, string Algoritmo, string ReferenciaLlaveUsada);

public interface ICriptografiaServiceClient
{
    /// <summary>Obtiene (generando si es la primera vez) la referencia de llave del firmante.</summary>
    Task<string> ObtenerOGenerarLlaveAsync(Guid usuarioId, CancellationToken ct = default);

    Task<FirmaCriptografica> FirmarAsync(string referenciaLlave, string hashDocumentoHex, CancellationToken ct = default);
}
