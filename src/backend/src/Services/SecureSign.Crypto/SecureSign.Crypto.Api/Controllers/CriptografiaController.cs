using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureSign.Crypto.Domain;

namespace SecureSign.Crypto.Api.Controllers;

public sealed record GenerarLlaveRequest(Guid UsuarioId, AlgoritmoFirma Algoritmo);

/// <param name="Pin">
/// Requerido por el proveedor PKCS#11 (tarjeta real); ignorado por el
/// proveedor de software. Nunca se loguea ni se persiste — se usa una sola
/// vez para esta operación y se descarta.
/// </param>
public sealed record FirmarRequest(string ReferenciaLlave, string HashDocumentoHex, AlgoritmoFirma Algoritmo, string? Pin = null);

public sealed record OperacionFirmarLoteRequest(string ReferenciaLlave, string HashDocumentoHex, AlgoritmoFirma Algoritmo);

/// <param name="Pin">Ver <see cref="FirmarRequest.Pin"/> — se usa UNA sola vez para todo el lote.</param>
public sealed record FirmarLoteRequest(IReadOnlyList<OperacionFirmarLoteRequest> Operaciones, string? Pin = null);

/// <summary>
/// Endpoints de uso EXCLUSIVAMENTE interno (llamados por el Servicio de Firma
/// vía red de servicio, nunca expuestos por el Gateway público — ver
/// docs/07-seguridad/modelo-seguridad.md sección 1).
///
/// SIMPLIFICACIÓN: en este scaffold, el Servicio de Firma solicita generar
/// una llave "al vuelo" la primera vez que un firmante firma, porque el
/// Servicio de Certificados (emisión ligada a una EC acreditada, ver
/// docs/03-legal-normativo) no está implementado. En producción, la llave
/// proviene de un certificado ya emitido y vigente, no se genera aquí. Con
/// el proveedor PKCS#11 esto no genera nada — descubre el certificado de
/// firma ya emitido en el token conectado (ver ProveedorCriptograficoPkcs11).
/// </summary>
[ApiController]
[Authorize]
[Route("api/interno/criptografia")]
public sealed class CriptografiaController(IProveedorCriptografico proveedor) : ControllerBase
{
    [HttpPost("generar-llave")]
    public async Task<IActionResult> GenerarLlave([FromBody] GenerarLlaveRequest request, CancellationToken ct)
    {
        var referencia = await proveedor.GenerarParClavesAsync(request.UsuarioId, request.Algoritmo, ct);
        return Ok(new { referenciaLlave = referencia });
    }

    [HttpPost("firmar")]
    public async Task<IActionResult> Firmar([FromBody] FirmarRequest request, CancellationToken ct)
    {
        var hash = Convert.FromHexString(request.HashDocumentoHex);
        var resultado = await proveedor.FirmarAsync(request.ReferenciaLlave, hash, request.Algoritmo, request.Pin, ct);

        return Ok(new
        {
            firmaBase64 = Convert.ToBase64String(resultado.Firma),
            algoritmo = resultado.Algoritmo.ToString(),
            referenciaLlaveUsada = resultado.ReferenciaLlaveUsada
        });
    }

    /// <summary>
    /// POST /api/interno/criptografia/firmar-lote — modo masivo: firma N
    /// hashes con una sola credencial (ver IProveedorCriptografico.FirmarLoteAsync).
    /// El orden de la respuesta coincide siempre con el de <see cref="FirmarLoteRequest.Operaciones"/>.
    /// </summary>
    [HttpPost("firmar-lote")]
    public async Task<IActionResult> FirmarLote([FromBody] FirmarLoteRequest request, CancellationToken ct)
    {
        var operaciones = request.Operaciones
            .Select(o => new OperacionFirmaLote(o.ReferenciaLlave, Convert.FromHexString(o.HashDocumentoHex), o.Algoritmo))
            .ToList();

        var resultados = await proveedor.FirmarLoteAsync(operaciones, request.Pin, ct);

        return Ok(resultados.Select(r => new
        {
            firmaBase64 = Convert.ToBase64String(r.Firma),
            algoritmo = r.Algoritmo.ToString(),
            referenciaLlaveUsada = r.ReferenciaLlaveUsada
        }));
    }
}
