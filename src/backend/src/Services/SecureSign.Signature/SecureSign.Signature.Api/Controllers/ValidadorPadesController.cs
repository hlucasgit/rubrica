using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureSign.Signature.Application.ValidarPades;

namespace SecureSign.Signature.Api.Controllers;

/// <summary>
/// Validador PAdES independiente — ver informe de preauditoría INDECOPI/IOFE,
/// hallazgo P0-05 ("no debe depender del mismo camino de código que genera
/// la firma") y RUNBOOK.md 12.12. Sin autenticación, igual que
/// ValidacionPublicaController: cualquier destinatario de un documento
/// firmado (no necesariamente un tenant integrado a Rúbrica) debe poder
/// verificar su validez, y el PDF puede venir de cualquier software que
/// produzca PAdES/CMS estándar, no solo de SecureSign.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/validador")]
public sealed class ValidadorPadesController(ISender mediator) : ControllerBase
{
    private const long TamanoMaximoBytes = 50 * 1024 * 1024;

    /// <summary>POST /api/validador/pdf (multipart/form-data, campo "documento") — devuelve el expediente completo de validación de cada firma /Sig del PDF.</summary>
    [HttpPost("pdf")]
    [RequestSizeLimit(TamanoMaximoBytes)]
    public async Task<IActionResult> ValidarPdf(IFormFile documento, CancellationToken ct)
    {
        if (documento.Length == 0)
            return BadRequest(new { error = "DOCUMENTO_VACIO", mensaje = "No se recibió ningún archivo en el campo 'documento'." });

        byte[] pdf;
        using (var buffer = new MemoryStream())
        {
            await documento.CopyToAsync(buffer, ct);
            pdf = buffer.ToArray();
        }

        var resultado = await mediator.Send(new ValidarPadesQuery(pdf), ct);

        return resultado.EsExitoso
            ? Ok(resultado.Valor)
            : BadRequest(new { error = "DOCUMENTO_INVALIDO", mensaje = resultado.Error });
    }
}
