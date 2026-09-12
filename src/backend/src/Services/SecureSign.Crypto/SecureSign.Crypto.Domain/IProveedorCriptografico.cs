namespace SecureSign.Crypto.Domain;

public enum AlgoritmoFirma
{
    RsaSha256,
    EcdsaSha256,
    EcdsaSha384
}

public sealed record ResultadoFirmaCriptografica(byte[] Firma, AlgoritmoFirma Algoritmo, string ReferenciaLlaveUsada);

/// <summary>
/// Abstracción central del Servicio Criptográfico (ver
/// docs/01-arquitectura/arquitectura-general.md, principio de independencia
/// criptográfica del proveedor). Toda operación de firma pasa por esta
/// interfaz; el material privado NUNCA existe fuera de la implementación
/// concreta (HSM PKCS#11 / Azure Key Vault / AWS KMS).
///
/// IMPORTANTE: la única implementación incluida en este scaffold
/// (ProveedorCriptograficoSoftware) es exclusivamente para desarrollo local
/// y pruebas automatizadas. NO debe usarse en producción — ver
/// docs/07-seguridad/modelo-seguridad.md sección 3.
/// </summary>
public interface IProveedorCriptografico
{
    Task<string> GenerarParClavesAsync(Guid usuarioId, AlgoritmoFirma algoritmo, CancellationToken ct = default);

    Task<ResultadoFirmaCriptografica> FirmarAsync(string referenciaLlave, byte[] hashDocumento, AlgoritmoFirma algoritmo, CancellationToken ct = default);

    Task<bool> VerificarFirmaAsync(string referenciaLlave, byte[] hashDocumento, byte[] firma, AlgoritmoFirma algoritmo, CancellationToken ct = default);
}
