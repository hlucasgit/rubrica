using MediatR;
using SecureSign.Domain.Primitives;
using SecureSign.Signature.Domain;

namespace SecureSign.Signature.Application.ListarPendientes;

/// <summary>
/// Respalda el modo masivo (ver docs/RUNBOOK.md): antes de firmar en lote,
/// el firmante necesita ver qué tiene pendiente. Aplana cada SolicitudFirma
/// a solo el/los flujo(s) de ESTE firmante — un firmante nunca ve los
/// flujos de otros firmantes de la misma solicitud.
/// </summary>
public sealed class ListarPendientesHandler(ISolicitudFirmaRepository repositorio)
    : IRequestHandler<ListarPendientesQuery, Result<IReadOnlyList<PendienteDto>>>
{
    public async Task<Result<IReadOnlyList<PendienteDto>>> Handle(ListarPendientesQuery request, CancellationToken ct)
    {
        var solicitudes = await repositorio.ListarPendientesPorFirmanteAsync(request.TenantId, request.FirmanteUsuarioId, ct);

        var pendientes = solicitudes
            .SelectMany(s => s.Flujos
                .Where(f => f.FirmanteUsuarioId == request.FirmanteUsuarioId
                    && f.Estado != EstadoFlujoFirma.Firmado && f.Estado != EstadoFlujoFirma.Rechazado)
                .Select(f => new PendienteDto(
                    s.Id, f.Id, s.DocumentoId, s.TipoFirma.ToString(), f.Estado.ToString(), s.Estado.ToString(), f.NotificadoEn)))
            .ToList();

        return Result.Exitoso<IReadOnlyList<PendienteDto>>(pendientes);
    }
}
