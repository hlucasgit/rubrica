using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureSign.Crypto.Domain;

namespace SecureSign.Crypto.Api.Controllers;

public sealed record GenerarLlaveRequest(Guid UsuarioId, AlgoritmoFirma Algoritmo);
public sealed record FirmarRequest(string ReferenciaLlave, string HashDocumentoHex, AlgoritmoFirma Algoritmo);

/// <summary>
/// Endpoints de uso EXCLUSIVAMENTE interno (llamados por el Servicio de Firma
/// vía red de servicio, nunca expuestos por el Gateway público — ver
/// docs/07-seguridad/modelo-seguridad.md sección 1).
///
/// SIMPLIFICACIÓN: en este scaffold, el Servicio de Firma solicita generar
/// una llave "al vuelo" la primera vez que un firmante firma, porque el
/// Servicio de Certificados (emisión ligada a una EC acreditada, ver
/// docs/03-legal-normativo) no está implementado. En producción, la llave
/// proviene de un certificado ya emitido y vigente, no se genera aquí.
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
        var resultado = await proveedor.FirmarAsync(request.ReferenciaLlave, hash, request.Algoritmo, ct);

        return Ok(new
        {
            firmaBase64 = Convert.ToBase64String(resultado.Firma),
            algoritmo = resultado.Algoritmo.ToString(),
            referenciaLlaveUsada = resultado.ReferenciaLlaveUsada
        });
    }
}
