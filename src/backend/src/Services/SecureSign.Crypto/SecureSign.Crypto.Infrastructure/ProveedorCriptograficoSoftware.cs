using System.Collections.Concurrent;
using System.Security.Cryptography;
using SecureSign.Crypto.Domain;

namespace SecureSign.Crypto.Infrastructure;

/// <summary>
/// ADVERTENCIA: implementación SOLO para desarrollo local y pruebas.
///
/// Mantiene las llaves privadas en memoria del proceso, lo cual es
/// precisamente lo que la arquitectura de producción prohíbe (ver
/// docs/07-seguridad/modelo-seguridad.md sección 3: "ninguna llave privada
/// de firma se genera, transporta ni persiste fuera del límite del HSM/KMS").
///
/// La implementación de producción debe reemplazar esta clase por un
/// adaptador PKCS#11 (HSM físico) o el SDK de Azure Key Vault Managed HSM /
/// AWS CloudHSM, sin cambiar la interfaz IProveedorCriptografico ni ningún
/// consumidor de la misma.
/// </summary>
public sealed class ProveedorCriptograficoSoftware : IProveedorCriptografico
{
    private readonly ConcurrentDictionary<string, ECDsa> _llaves = new();

    public Task<string> GenerarParClavesAsync(Guid usuarioId, AlgoritmoFirma algoritmo, CancellationToken ct = default)
    {
        var curva = algoritmo == AlgoritmoFirma.EcdsaSha384 ? ECCurve.NamedCurves.nistP384 : ECCurve.NamedCurves.nistP256;
        var llave = ECDsa.Create(curva);
        var referencia = $"dev-key-{usuarioId:N}-{Guid.NewGuid():N}";
        _llaves[referencia] = llave;
        return Task.FromResult(referencia);
    }

    public Task<ResultadoFirmaCriptografica> FirmarAsync(string referenciaLlave, byte[] hashDocumento, AlgoritmoFirma algoritmo, string? credencial = null, CancellationToken ct = default)
    {
        // credencial se ignora: las llaves de software no requieren PIN.
        if (!_llaves.TryGetValue(referenciaLlave, out var llave))
            throw new InvalidOperationException($"Referencia de llave no encontrada: {referenciaLlave}");

        var firma = llave.SignHash(hashDocumento, DSASignatureFormat.Rfc3279DerSequence);

        return Task.FromResult(new ResultadoFirmaCriptografica(firma, algoritmo, referenciaLlave));
    }

    public Task<bool> VerificarFirmaAsync(string referenciaLlave, byte[] hashDocumento, byte[] firma, AlgoritmoFirma algoritmo, CancellationToken ct = default)
    {
        if (!_llaves.TryGetValue(referenciaLlave, out var llave))
            return Task.FromResult(false);

        var valido = llave.VerifyHash(hashDocumento, firma, DSASignatureFormat.Rfc3279DerSequence);
        return Task.FromResult(valido);
    }
}
