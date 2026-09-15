using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureSign.Audit.Application.ConsultarEventos;
using SecureSign.Audit.Application.RegistrarEvento;
using SecureSign.Audit.Domain;
using SecureSign.Shared.Auth;

namespace SecureSign.Audit.Api.Controllers;

public sealed record RegistrarEventoAuditoriaRequest(TipoEventoAuditoria TipoEvento, string Detalle, Guid? TenantId);

/// <summary>
/// Auditoría TÉCNICA — distinta de /api/evidencias (evidencia de negocio de
/// un documento/firma concreto, con cadena de hashes) y de /api/validador
/// (el resultado de validar UN documento en particular). Aquí se registran
/// eventos de seguridad/técnicos sin cadena de hashes: certificados
/// rechazados, tickets de firma rechazados, validaciones PAdES realizadas —
/// ver informe de preauditoría INDECOPI/IOFE, sección 8, y RUNBOOK.md 12.16.
/// </summary>
[ApiController]
[Authorize]
[Route("api/auditoria")]
public sealed class AuditoriaController(ISender mediator) : ControllerBase
{
    /// <summary>
    /// POST /api/auditoria — uso interno: invocado por otros servicios (Firma,
    /// Signature.Api) como reacción a un evento técnico/de seguridad relevante,
    /// igual patrón que POST /api/evidencias.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Registrar([FromBody] RegistrarEventoAuditoriaRequest request, CancellationToken ct)
    {
        var comando = new RegistrarEventoAuditoriaCommand(
            request.TipoEvento, request.Detalle, request.TenantId,
            HttpContext.Connection.RemoteIpAddress?.ToString());

        var resultado = await mediator.Send(comando, ct);
        return resultado.EsExitoso ? Ok(new { idEvento = resultado.Valor }) : BadRequest(new { error = resultado.Error });
    }

    /// <summary>GET /api/auditoria — los eventos del tenant que llama, más recientes primero.</summary>
    [HttpGet]
    public async Task<IActionResult> Consultar([FromQuery] TipoEventoAuditoria? tipoEvento, [FromQuery] int limite, CancellationToken ct)
    {
        var tenant = User.ObtenerTenantContext();
        var resultado = await mediator.Send(new ConsultarEventosAuditoriaQuery(tenant.TenantId, tipoEvento, limite), ct);

        return resultado.EsExitoso
            ? Ok(resultado.Valor)
            : BadRequest(new { error = resultado.Error });
    }
}
