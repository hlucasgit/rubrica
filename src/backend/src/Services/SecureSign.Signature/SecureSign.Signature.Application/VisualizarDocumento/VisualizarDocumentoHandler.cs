using MediatR;
using SecureSign.Domain.Primitives;
using SecureSign.Signature.Application.Clients;
using SecureSign.Signature.Domain;

namespace SecureSign.Signature.Application.VisualizarDocumento;

/// <summary>
/// Registra que el firmante visualizó el documento. Este es el punto donde
/// arrancaría, en producción, la captura de métricas de continuidad
/// conductual (innovación #3, ver docs/02-innovacion-patente) — no
/// implementada en este scaffold porque requiere un SDK cliente instrumentado.
/// </summary>
public sealed class VisualizarDocumentoHandler(
    ISolicitudFirmaRepository repositorio,
    IEvidenciasServiceClient evidencias)
    : IRequestHandler<VisualizarDocumentoCommand, Result>
{
    public async Task<Result> Handle(VisualizarDocumentoCommand request, CancellationToken ct)
    {
        var solicitud = await repositorio.ObtenerPorIdAsync(request.TenantId, request.SolicitudFirmaId, ct);
        if (solicitud is null) return Result.Fallido("Solicitud de firma no encontrada.");

        var resultado = solicitud.RegistrarVisualizacion(request.FlujoFirmaId);
        if (!resultado.EsExitoso) return resultado;

        await repositorio.ActualizarAsync(solicitud, ct);
        await evidencias.RegistrarAsync(solicitud.DocumentoId, "Visualizacion", request.DatosContextuales, ct: ct);

        return Result.Exitoso();
    }
}
