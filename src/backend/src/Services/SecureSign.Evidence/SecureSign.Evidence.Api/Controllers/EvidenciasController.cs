using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureSign.Evidence.Application.RegistrarEvidencia;
using SecureSign.Evidence.Domain;
using SecureSign.Shared.Auth;

namespace SecureSign.Evidence.Api.Controllers;

public sealed record DatosContextualesDto(string? IpOrigen, string? UserAgent, string? Dispositivo);
public sealed record RegistrarEvidenciaRequest(Guid DocumentoId, TipoEvidencia TipoEvidencia, DatosContextualesDto Datos, Guid? FirmaId);

[ApiController]
[Authorize]
[Route("api/evidencias")]
public sealed class EvidenciasController(ISender mediator, IEventoEvidenciaRepository repositorio) : ControllerBase
{
    /// <summary>
    /// POST /api/evidencias — uso interno: invocado por Documentos/Firma/Identidad
    /// como reacción a cada evento de dominio relevante (ver
    /// docs/01-arquitectura/arquitectura-general.md principio #4).
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Registrar([FromBody] RegistrarEvidenciaRequest request, CancellationToken ct)
    {
        var tenant = User.ObtenerTenantContext();

        var comando = new RegistrarEvidenciaCommand(
            tenant.TenantId,
            request.DocumentoId,
            request.TipoEvidencia,
            new DatosContextuales(request.Datos.IpOrigen, request.Datos.UserAgent, request.Datos.Dispositivo, null),
            request.FirmaId);

        var resultado = await mediator.Send(comando, ct);
        return resultado.EsExitoso ? Ok(new { idEvento = resultado.Valor }) : BadRequest(new { error = resultado.Error });
    }

    /// <summary>
    /// Verifica la integridad completa de la cadena de evidencia de un tenant.
    /// Respalda GET /api/validacion/{codigo} (a través del Servicio de Firma).
    /// </summary>
    [HttpGet("cadena/{tenantId:guid}/verificar")]
    [AllowAnonymous]
    public async Task<IActionResult> VerificarCadena(Guid tenantId, CancellationToken ct)
    {
        var cadena = await repositorio.ObtenerCadenaCompletaAsync(tenantId, ct);
        var resultado = VerificadorCadenaEvidencia.Verificar(cadena);

        return Ok(new
        {
            cadenaValida = resultado.CadenaValida,
            totalEventos = resultado.TotalEventos,
            primerEventoRotoId = resultado.PrimerEventoRotoId
        });
    }

    [HttpGet("documento/{tenantId:guid}/{documentoId:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> ObtenerPorDocumento(Guid tenantId, Guid documentoId, CancellationToken ct)
    {
        var eventos = await repositorio.ObtenerPorDocumentoAsync(tenantId, documentoId, ct);
        return Ok(eventos.Select(e => new
        {
            e.Id,
            TipoEvidencia = e.TipoEvidencia.ToString(),
            e.HashEvento,
            e.HashEventoAnterior,
            e.RegistradoEn
        }));
    }
}
