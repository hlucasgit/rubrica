namespace SecureSign.Crypto.Domain;

public enum AlgoritmoFirma
{
    RsaSha256,
    EcdsaSha256,
    EcdsaSha384
}

public sealed record ResultadoFirmaCriptografica(byte[] Firma, AlgoritmoFirma Algoritmo, string ReferenciaLlaveUsada);

/// <summary>Una operación de firma dentro de un lote — ver <see cref="IProveedorCriptografico.FirmarLoteAsync"/>.</summary>
public sealed record OperacionFirmaLote(string ReferenciaLlave, byte[] HashDocumento, AlgoritmoFirma Algoritmo);

/// <summary>
/// Abstracción central del Servicio Criptográfico (ver
/// docs/01-arquitectura/arquitectura-general.md, principio de independencia
/// criptográfica del proveedor). Toda operación de firma pasa por esta
/// interfaz; el material privado NUNCA existe fuera de la implementación
/// concreta (HSM PKCS#11 / Azure Key Vault / AWS KMS / smart card real).
///
/// Implementaciones incluidas en este scaffold:
/// - <c>ProveedorCriptograficoSoftware</c>: llaves ECDSA en memoria del
///   proceso — SOLO desarrollo/pruebas automatizadas.
/// - <c>ProveedorCriptograficoPkcs11</c>: habla con un token PKCS#11 real
///   (probado con el DNIe peruano vía el middleware de IDEMIA) — la llave
///   privada nunca sale de la tarjeta. Requiere <paramref name="credencial"/>
///   (el PIN) en cada operación de firma; no se persiste ni se cachea nunca.
/// </summary>
public interface IProveedorCriptografico
{
    /// <summary>
    /// Resuelve la referencia de llave a usar para un usuario. Con el
    /// proveedor de software esto GENERA un par de llaves nuevo; con el
    /// proveedor PKCS#11 no genera nada — DESCUBRE el certificado de firma
    /// ya emitido (por RENIEC u otra EC) en el token conectado y devuelve
    /// una referencia a él. No requiere PIN: leer certificados es una
    /// operación pública del token.
    /// </summary>
    Task<string> GenerarParClavesAsync(Guid usuarioId, AlgoritmoFirma algoritmo, CancellationToken ct = default);

    /// <param name="credencial">
    /// PIN/credencial necesaria para autorizar la operación de firma con la
    /// llave privada. Ignorada por proveedores que no la necesitan (p. ej.
    /// el de software). NUNCA debe loguearse, persistirse ni incluirse en
    /// evidencia — se usa una sola vez, de forma transitoria, para esta
    /// operación exclusivamente.
    /// </param>
    Task<ResultadoFirmaCriptografica> FirmarAsync(string referenciaLlave, byte[] hashDocumento, AlgoritmoFirma algoritmo, string? credencial = null, CancellationToken ct = default);

    /// <summary>
    /// Modo masivo (ver docs/RUNBOOK.md): firma N documentos con UNA sola
    /// credencial/sesión, en vez de exigir al firmante ingresar su PIN N
    /// veces. La implementación de software simplemente firma una por una
    /// (no tiene costo real de sesión); <c>ProveedorCriptograficoPkcs11</c>
    /// la sobreescribe para abrir un único login contra la tarjeta y firmar
    /// todo el lote antes de cerrar sesión — por eso todas las operaciones
    /// de un lote deben compartir la misma <see cref="OperacionFirmaLote.ReferenciaLlave"/>
    /// (mismo firmante, misma tarjeta física).
    /// </summary>
    async Task<IReadOnlyList<ResultadoFirmaCriptografica>> FirmarLoteAsync(
        IReadOnlyList<OperacionFirmaLote> operaciones, string? credencial = null, CancellationToken ct = default)
    {
        var resultados = new List<ResultadoFirmaCriptografica>(operaciones.Count);
        foreach (var operacion in operaciones)
            resultados.Add(await FirmarAsync(operacion.ReferenciaLlave, operacion.HashDocumento, operacion.Algoritmo, credencial, ct));
        return resultados;
    }

    Task<bool> VerificarFirmaAsync(string referenciaLlave, byte[] hashDocumento, byte[] firma, AlgoritmoFirma algoritmo, CancellationToken ct = default);
}
