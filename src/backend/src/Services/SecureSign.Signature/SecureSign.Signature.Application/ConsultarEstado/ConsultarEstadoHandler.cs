using MediatR;
using SecureSign.Domain.Primitives;
using SecureSign.Signature.Domain;

namespace SecureSign.Signature.Application.ConsultarEstado;

public sealed class ConsultarEstadoHandler(ISolicitudFirmaRepository repositorio)
    : IRequestHandler<ConsultarEstadoQuery, Result<SolicitudEstadoDto>>,
      IRequestHandler<ConsultarPorCodigoQuery, Result<SolicitudEstadoDto>>
{
    public async Task<Result<SolicitudEstadoDto>> Handle(ConsultarEstadoQuery request, CancellationToken ct)
    {
        var solicitud = await repositorio.ObtenerPorIdAsync(request.TenantId, request.SolicitudFirmaId, ct);
        return solicitud is null
            ? Result.Fallido<SolicitudEstadoDto>("Solicitud de firma no encontrada.")
            : Result.Exitoso(Proyectar(solicitud));
    }

    public async Task<Result<SolicitudEstadoDto>> Handle(ConsultarPorCodigoQuery request, CancellationToken ct)
    {
        var solicitud = await repositorio.ObtenerPorCodigoVerificacionAsync(request.Codigo, ct);
        return solicitud is null
            ? Result.Fallido<SolicitudEstadoDto>("Código de verificación no encontrado.")
            : Result.Exitoso(Proyectar(solicitud));
    }

    private static SolicitudEstadoDto Proyectar(SolicitudFirma solicitud) => new(
        solicitud.Id,
        solicitud.DocumentoId,
        solicitud.Estado.ToString(),
        solicitud.CodigoVerificacionPublico,
        solicitud.Flujos.Select(f => new FlujoEstadoDto(f.Id, f.FirmanteUsuarioId, f.OrdenFirma, f.Estado.ToString())).ToList());
}
