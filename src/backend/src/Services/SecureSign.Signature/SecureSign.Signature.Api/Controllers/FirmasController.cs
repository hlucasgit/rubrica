using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureSign.Signature.Application.Clients;
using SecureSign.Signature.Application.ConsultarEstado;
using SecureSign.Signature.Application.CrearSolicitudFirma;
using SecureSign.Signature.Application.EstablecerPosicionFirma;
using SecureSign.Signature.Application.FirmarDocumento;
using SecureSign.Signature.Application.FirmarLote;
using SecureSign.Signature.Application.ListarPendientes;
using SecureSign.Signature.Application.NotificarSolicitud;
using SecureSign.Signature.Application.ObtenerDocumentoVisual;
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

/// <param name="Pin">
/// PIN de la tarjeta/token del firmante. Solo se necesita cuando el
/// Servicio Criptográfico configurado usa el proveedor PKCS#11 (tarjeta
/// real, p. ej. DNIe) — con el proveedor de software se ignora. Nunca se
/// guarda ni se registra: viaja solo dentro de esta petición puntual.
/// </param>
public sealed record FirmarRequest(string? Pin = null);

/// <summary>Coordenadas normalizadas (0..1, origen arriba-izquierda) elegidas en el visor — ver PosicionFirma.</summary>
public sealed record PosicionFirmaRequest(int NumeroPagina, double X, double Y, double Ancho, double Alto);

public sealed record OperacionFirmarLoteRequest(Guid SolicitudFirmaId, Guid FlujoFirmaId);

/// <param name="Pin">Ver FirmarRequest.Pin — se usa UNA sola vez para todas las <paramref name="Operaciones"/> (deben ser del mismo firmante).</param>
public sealed record FirmarLoteRequest(IReadOnlyList<OperacionFirmarLoteRequest> Operaciones, string? Pin = null);

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
    /// POST /api/firmas/{id}/flujos/{flujoId}/posicion — el firmante elige,
    /// arrastrando sobre la página en el visor (estilo Firma Perú/ONPE),
    /// dónde debe verse su firma. Debe llamarse después de visualizar y
    /// antes de firmar; ver PosicionFirma.
    /// </summary>
    [HttpPost("{id:guid}/flujos/{flujoId:guid}/posicion")]
    public async Task<IActionResult> EstablecerPosicion(Guid id, Guid flujoId, [FromBody] PosicionFirmaRequest body, CancellationToken ct)
    {
        var tenant = User.ObtenerTenantContext();
        var comando = new EstablecerPosicionFirmaCommand(tenant.TenantId, id, flujoId, body.NumeroPagina, body.X, body.Y, body.Ancho, body.Alto);
        var resultado = await mediator.Send(comando, ct);

        return resultado.EsExitoso
            ? NoContent()
            : BadRequest(new { error = "POSICION_INVALIDA", mensaje = resultado.Error });
    }

    /// <summary>
    /// GET /api/firmas/pendientes/{firmanteId} — lo que ve el firmante antes
    /// de decidir qué firmar en lote (ver ListarPendientesHandler).
    /// </summary>
    [HttpGet("pendientes/{firmanteId:guid}")]
    public async Task<IActionResult> ListarPendientes(Guid firmanteId, CancellationToken ct)
    {
        var tenant = User.ObtenerTenantContext();
        var resultado = await mediator.Send(new ListarPendientesQuery(tenant.TenantId, firmanteId), ct);

        return resultado.EsExitoso
            ? Ok(resultado.Valor)
            : BadRequest(new { error = "CONSULTA_INVALIDA", mensaje = resultado.Error });
    }

    /// <summary>
    /// POST /api/firmas/lotes/firmar — modo masivo: un mismo firmante firma
    /// varios flujos pendientes con una sola credencial (ver FirmarLoteHandler).
    /// </summary>
    [HttpPost("lotes/firmar")]
    public async Task<IActionResult> FirmarLote([FromBody] FirmarLoteRequest body, CancellationToken ct)
    {
        var tenant = User.ObtenerTenantContext();
        var comando = new FirmarLoteCommand(
            tenant.TenantId,
            body.Operaciones.Select(o => new OperacionFirmaLoteDto(o.SolicitudFirmaId, o.FlujoFirmaId)).ToList(),
            CapturarContexto(),
            body.Pin);

        var resultado = await mediator.Send(comando, ct);

        return resultado.EsExitoso
            ? Ok(resultado.Valor)
            : BadRequest(new { error = "LOTE_INVALIDO", mensaje = resultado.Error });
    }

    /// <summary>
    /// POST /api/firmas/{id}/flujos/{flujoId}/firmar — orquesta identidad,
    /// criptografía, evidencia y documentos (ver FirmarDocumentoHandler).
    /// </summary>
    [HttpPost("{id:guid}/flujos/{flujoId:guid}/firmar")]
    public async Task<IActionResult> Firmar(Guid id, Guid flujoId, [FromBody] FirmarRequest? body, CancellationToken ct)
    {
        var tenant = User.ObtenerTenantContext();
        var resultado = await mediator.Send(new FirmarDocumentoCommand(tenant.TenantId, id, flujoId, CapturarContexto(), body?.Pin), ct);

        return resultado.EsExitoso
            ? Ok(resultado.Valor)
            : BadRequest(new { error = "TRANSICION_INVALIDA", mensaje = resultado.Error });
    }

    /// <summary>
    /// GET /api/firmas/{id}/documento-visual — descarga el documento con el
    /// sello visual de cada firma ya aplicada, en la posición que cada
    /// firmante eligió (ver ObtenerDocumentoVisualHandler; solo PDF).
    /// </summary>
    [HttpGet("{id:guid}/documento-visual")]
    public async Task<IActionResult> DescargarDocumentoVisual(Guid id, CancellationToken ct)
    {
        var tenant = User.ObtenerTenantContext();
        var resultado = await mediator.Send(new ObtenerDocumentoVisualQuery(tenant.TenantId, id), ct);

        return resultado.EsExitoso
            ? File(resultado.Valor.Contenido, resultado.Valor.TipoContenido, resultado.Valor.NombreArchivo)
            : NotFound(new { error = "NO_ENCONTRADO", mensaje = resultado.Error });
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
