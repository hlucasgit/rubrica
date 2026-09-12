using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureSign.Identity.Application.ObtenerIndiceConfianza;
using SecureSign.Identity.Application.RegistrarSenal;
using SecureSign.Shared.Auth;

namespace SecureSign.Identity.Api.Controllers;

public sealed record RegistrarSenalRequest(TipoSenalIdentidad TipoSenal);

/// <summary>
/// Endpoints de uso interno (llamados por el Servicio de Firma antes de
/// autorizar una firma, y por los servicios que en producción emitirían
/// señales reales: Certificados, OTP/biometría, motor antifraude). Ver
/// docs/02-innovacion-patente, innovación #5.
/// </summary>
[ApiController]
[Authorize]
[Route("api/interno/identidad")]
public sealed class IdentidadController(ISender mediator) : ControllerBase
{
    [HttpGet("{usuarioId:guid}/indice-confianza")]
    public async Task<IActionResult> ObtenerIndiceConfianza(Guid usuarioId, CancellationToken ct)
    {
        var tenant = User.ObtenerTenantContext();
        var resultado = await mediator.Send(new ObtenerIndiceConfianzaQuery(tenant.TenantId, usuarioId), ct);
        return Ok(resultado.Valor);
    }

    [HttpPost("{usuarioId:guid}/senales")]
    public async Task<IActionResult> RegistrarSenal(Guid usuarioId, [FromBody] RegistrarSenalRequest body, CancellationToken ct)
    {
        var tenant = User.ObtenerTenantContext();
        var resultado = await mediator.Send(new RegistrarSenalCommand(tenant.TenantId, usuarioId, body.TipoSenal), ct);
        return Ok(new { indiceConfianza = resultado.Valor });
    }
}
