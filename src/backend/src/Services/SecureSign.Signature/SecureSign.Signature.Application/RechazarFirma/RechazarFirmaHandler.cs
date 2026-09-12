using MediatR;
using SecureSign.Domain.Primitives;
using SecureSign.Signature.Application.Clients;
using SecureSign.Signature.Domain;

namespace SecureSign.Signature.Application.RechazarFirma;

public sealed class RechazarFirmaHandler(
    ISolicitudFirmaRepository repositorio,
    IEvidenciasServiceClient evidencias)
    : IRequestHandler<RechazarFirmaCommand, Result>
{
    public async Task<Result> Handle(RechazarFirmaCommand request, CancellationToken ct)
    {
        var solicitud = await repositorio.ObtenerPorIdAsync(request.TenantId, request.SolicitudFirmaId, ct);
        if (solicitud is null) return Result.Fallido("Solicitud de firma no encontrada.");

        var resultado = solicitud.RechazarFirma(request.FlujoFirmaId, request.Motivo);
        if (!resultado.EsExitoso) return resultado;

        await repositorio.ActualizarAsync(solicitud, ct);
        await evidencias.RegistrarAsync(solicitud.DocumentoId, "Rechazo", request.DatosContextuales, ct: ct);

        return Result.Exitoso();
    }
}
