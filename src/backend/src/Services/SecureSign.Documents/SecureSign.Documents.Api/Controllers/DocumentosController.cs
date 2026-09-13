using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureSign.Documents.Application.MarcarDocumentoFirmado;
using SecureSign.Documents.Application.ObtenerDocumento;
using SecureSign.Documents.Application.RegistrarDocumento;
using SecureSign.Documents.Domain;
using SecureSign.Shared.Auth;

namespace SecureSign.Documents.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/documentos")]
public sealed class DocumentosController(ISender mediator, IAlmacenamientoDocumental almacenamiento) : ControllerBase
{
    /// <summary>POST /api/documentos — ver docs/05-integracion/manual-integracion-api.md 5.1</summary>
    [HttpPost]
    [RequestSizeLimit(50 * 1024 * 1024)]
    public async Task<IActionResult> Registrar(
        [FromForm] IFormFile archivo,
        [FromForm] string? codigoExterno,
        [FromForm] Guid? usuarioSolicitanteId,
        CancellationToken ct)
    {
        var tenant = User.ObtenerTenantContext();

        using var stream = new MemoryStream();
        await archivo.CopyToAsync(stream, ct);

        var datosContextuales = new SecureSign.Documents.Application.Clients.DatosContextualesRemoto(
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString(),
            null);

        var comando = new RegistrarDocumentoCommand(
            tenant.TenantId,
            archivo.FileName,
            archivo.ContentType,
            stream.ToArray(),
            usuarioSolicitanteId ?? tenant.UsuarioId ?? Guid.Empty,
            codigoExterno,
            datosContextuales);

        var resultado = await mediator.Send(comando, ct);

        if (!resultado.EsExitoso)
            return BadRequest(new { error = "SOLICITUD_INVALIDA", mensaje = resultado.Error });

        return CreatedAtAction(nameof(ObtenerDetalle), new { id = resultado.Valor.IdDocumento }, new
        {
            idDocumento = resultado.Valor.IdDocumento,
            hashDocumento = resultado.Valor.HashDocumento,
            estado = resultado.Valor.Estado
        });
    }

    /// <summary>
    /// GET /api/documentos/{id} — uso interno (Servicio de Firma consulta hash
    /// y ubicación antes de firmar) y externo (integrador consulta metadata).
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> ObtenerDetalle(Guid id, CancellationToken ct)
    {
        var tenant = User.ObtenerTenantContext();
        var resultado = await mediator.Send(new ObtenerDocumentoQuery(tenant.TenantId, id), ct);

        if (!resultado.EsExitoso)
            return NotFound(new { error = "NO_ENCONTRADO", mensaje = resultado.Error });

        return Ok(resultado.Valor);
    }

    /// <summary>
    /// POST /api/documentos/{id}/marcar-firmado — invocado internamente por el
    /// Servicio de Firma cuando todos los firmantes completan la solicitud.
    /// </summary>
    [HttpPost("{id:guid}/marcar-firmado")]
    public async Task<IActionResult> MarcarFirmado(Guid id, CancellationToken ct)
    {
        var tenant = User.ObtenerTenantContext();
        var resultado = await mediator.Send(new MarcarDocumentoFirmadoCommand(tenant.TenantId, id), ct);

        return resultado.EsExitoso
            ? NoContent()
            : BadRequest(new { error = "TRANSICION_INVALIDA", mensaje = resultado.Error });
    }

    /// <summary>
    /// GET /api/documentos/{id}/contenido — bytes originales del documento,
    /// en CUALQUIER estado (a diferencia de /firmado, que exige Firmado).
    /// Uso interno (Servicio de Firma la usa para generar el sello visual,
    /// ver IEstampadorVisualDocumento) y del visor del firmante (necesita
    /// renderizar el PDF para que el usuario elija dónde colocar su firma
    /// ANTES de que exista ninguna firma).
    /// </summary>
    [HttpGet("{id:guid}/contenido")]
    public async Task<IActionResult> ObtenerContenido(Guid id, CancellationToken ct)
    {
        var tenant = User.ObtenerTenantContext();
        var resultado = await mediator.Send(new ObtenerDocumentoQuery(tenant.TenantId, id), ct);

        if (!resultado.EsExitoso)
            return NotFound(new { error = "NO_ENCONTRADO", mensaje = resultado.Error });

        var contenido = await almacenamiento.LeerAsync(resultado.Valor.UrlAlmacenamiento, ct);
        return File(contenido, resultado.Valor.TipoContenido, resultado.Valor.NombreArchivo);
    }

    /// <summary>GET /api/documentos/{id}/firmado — ver manual de integración 5.4.</summary>
    [HttpGet("{id:guid}/firmado")]
    public async Task<IActionResult> DescargarFirmado(Guid id, CancellationToken ct)
    {
        var tenant = User.ObtenerTenantContext();
        var resultado = await mediator.Send(new ObtenerDocumentoQuery(tenant.TenantId, id), ct);

        if (!resultado.EsExitoso)
            return NotFound(new { error = "NO_ENCONTRADO", mensaje = resultado.Error });

        if (resultado.Valor.Estado != nameof(EstadoDocumento.Firmado))
            return Conflict(new { error = "DOCUMENTO_NO_FIRMADO", mensaje = $"El documento está en estado {resultado.Valor.Estado}, no Firmado." });

        var contenido = await almacenamiento.LeerAsync(resultado.Valor.UrlAlmacenamiento, ct);

        Response.Headers["X-Hash-Documento"] = resultado.Valor.HashSha256;
        Response.Headers["X-Evidencia-Url"] = $"/api/evidencias/documento/{tenant.TenantId}/{id}";

        return File(contenido, resultado.Valor.TipoContenido, resultado.Valor.NombreArchivo);
    }
}
