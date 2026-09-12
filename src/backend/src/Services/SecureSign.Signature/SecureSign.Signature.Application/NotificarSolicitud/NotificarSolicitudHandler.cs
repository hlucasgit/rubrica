using MediatR;
using SecureSign.Domain.Primitives;
using SecureSign.Signature.Domain;

namespace SecureSign.Signature.Application.NotificarSolicitud;

public sealed class NotificarSolicitudHandler(ISolicitudFirmaRepository repositorio)
    : IRequestHandler<NotificarSolicitudCommand, Result>
{
    public async Task<Result> Handle(NotificarSolicitudCommand request, CancellationToken ct)
    {
        var solicitud = await repositorio.ObtenerPorIdAsync(request.TenantId, request.SolicitudFirmaId, ct);
        if (solicitud is null) return Result.Fallido("Solicitud de firma no encontrada.");

        foreach (var flujo in solicitud.ObtenerFirmantesElegiblesParaNotificar())
        {
            var resultado = solicitud.NotificarFirmante(flujo.Id);
            if (!resultado.EsExitoso) return resultado;
        }

        await repositorio.ActualizarAsync(solicitud, ct);
        return Result.Exitoso();
    }
}
