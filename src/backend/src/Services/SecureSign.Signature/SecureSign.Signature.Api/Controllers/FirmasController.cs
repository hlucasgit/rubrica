using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureSign.Signature.Application.Clients;
using SecureSign.Signature.Application.ConsultarEstado;
using SecureSign.Signature.Application.CrearSolicitudFirma;
using SecureSign.Signature.Application.FirmarDocumento;
using SecureSign.Signature.Application.NotificarSolicitud;
using SecureSign.Signature.Application.RechazarFirma;
using SecureSign.Signature.Application.VisualizarDocumento;
using SecureSign.Signature.Domain;
using SecureSign.Shared.Auth;

namespace SecureSign.Signature.Api.Controllers;

public sealed record CrearSolicitudFirmaRequest(
    Guid DocumentoId,
    TipoFirma TipoFirma,
    bool RequiereOrdenSecuencial,
    List<FirmanteDto> Firmantes,
    DateTimeOffset? FechaLimite);

public sealed record RechazarFirmaRequest(string Motivo);

[ApiController]
[Authorize]
[Route("api/firmas")]
public sealed class FirmasController(ISender mediator) : ControllerBase
{
    private DatosContextualesRemoto CapturarContexto() => new(
        HttpContext.Connection.RemoteIpAddress?.ToString(),
        Request.Headers.UserAgent.ToString(),
        null);

    /// <summary>POST /api/firmas/solicitudes — ver manual de integración 5.2. Notifica automáticamente a los primeros firmantes elegibles.</summary>
    [HttpPost("solicitudes")]
    public async Task<IActionResult> CrearSolicitud([FromBody] CrearSolicitudFirmaRequest body, CancellationToken ct)
    {
        var tenant = User.ObtenerTenantContext();

        var comando = new CrearSolicitudFirmaCommand(
            tenant.TenantId, body.DocumentoId, body.TipoFirma, body.RequiereOrdenSecuencial,
            body.Firmantes, tenant.UsuarioId ?? Guid.Empty, body.FechaLimite, tenant.ClienteIntegradorId);

        var resultado = await mediator.Send(comando, ct);
        if (!resultado.EsExitoso)
            return BadRequest(new { error = "SOLICITUD_INVALIDA", mensaje = resultado.Error });

        await mediator.Send(new NotificarSolicitudCommand(tenant.TenantId, resultado.Valor.SolicitudFirmaId), ct);

        return CreatedAtAction(nameof(ConsultarEstado), new { id = resultado.Valor.SolicitudFirmaId }, resultado.Valor);
    }

    /// <summary>GET /api/firmas/{id}/estado — ver manual de integración 5.3.</summary>
    [HttpGet("{id:guid}/estado")]
    public async Task<IActionResult> ConsultarEstado(Guid id, CancellationToken ct)
    {
        var tenant = User.ObtenerTenantContext();
        var resultado = await mediator.Send(new ConsultarEstadoQuery(tenant.TenantId, id), ct);

        return resultado.EsExitoso
            ? Ok(resultado.Valor)
            : NotFound(new { error = "NO_ENCONTRADO", mensaje = resultado.Error });
    }

    /// <summary>
    /// POST /api/firmas/{id}/flujos/{flujoId}/visualizar — el firmante abre el
    /// documento. Registra evidencia de tipo Visualizacion.
    /// </summary>
    [HttpPost("{id:guid}/flujos/{flujoId:guid}/visualizar")]
    public async Task<IActionResult> Visualizar(Guid id, Guid flujoId, CancellationToken ct)
    {
        var tenant = User.ObtenerTenantContext();
        var resultado = await mediator.Send(new VisualizarDocumentoCommand(tenant.TenantId, id, flujoId, CapturarContexto()), ct);

        return resultado.EsExitoso
            ? NoContent()
            : BadRequest(new { error = "TRANSICION_INVALIDA", mensaje = resultado.Error });
    }

    /// <summary>
    /// POST /api/firmas/{id}/flujos/{flujoId}/firmar — orquesta identidad,
    /// criptografía, evidencia y documentos (ver FirmarDocumentoHandler).
    /// </summary>
    [HttpPost("{id:guid}/flujos/{flujoId:guid}/firmar")]
    public async Task<IActionResult> Firmar(Guid id, Guid flujoId, CancellationToken ct)
    {
        var tenant = User.ObtenerTenantContext();
        var resultado = await mediator.Send(new FirmarDocumentoCommand(tenant.TenantId, id, flujoId, CapturarContexto()), ct);

        return resultado.EsExitoso
            ? Ok(resultado.Valor)
            : BadRequest(new { error = "TRANSICION_INVALIDA", mensaje = resultado.Error });
    }

    /// <summary>POST /api/firmas/{id}/flujos/{flujoId}/rechazar</summary>
    [HttpPost("{id:guid}/flujos/{flujoId:guid}/rechazar")]
    public async Task<IActionResult> Rechazar(Guid id, Guid flujoId, [FromBody] RechazarFirmaRequest body, CancellationToken ct)
    {
        var tenant = User.ObtenerTenantContext();
        var resultado = await mediator.Send(new RechazarFirmaCommand(tenant.TenantId, id, flujoId, body.Motivo, CapturarContexto()), ct);

        return resultado.EsExitoso
            ? NoContent()
            : BadRequest(new { error = "TRANSICION_INVALIDA", mensaje = resultado.Error });
    }
}

/// <summary>GET /api/validacion/{codigo} — portal público, ver manual de integración 5.5. Sin autenticación.</summary>
[ApiController]
[AllowAnonymous]
[Route("api/validacion")]
public sealed class ValidacionPublicaController(ISender mediator) : ControllerBase
{
    [HttpGet("{codigo}")]
    public async Task<IActionResult> Validar(string codigo, CancellationToken ct)
    {
        var resultado = await mediator.Send(new ConsultarPorCodigoQuery(codigo), ct);
        if (!resultado.EsExitoso)
            return NotFound(new { documentoValido = false, mensaje = resultado.Error });

        return Ok(new
        {
            documentoValido = resultado.Valor.Estado == "Firmado",
            estado = resultado.Valor.Estado,
            firmantes = resultado.Valor.Firmantes.Select(f => new { f.Orden, f.Estado }),
        });
    }
}
