using MediatR;
using SecureSign.Domain.Primitives;
using SecureSign.Signature.Domain;

namespace SecureSign.Signature.Application.EstablecerPosicionFirma;

/// <summary>
/// Registra dónde eligió el firmante ver su firma en el documento (arrastrar
/// el recuadro sobre la página en el visor, como Firma Perú/ONPE) — se llama
/// después de Visualizar y antes de Firmar.
/// </summary>
public sealed class EstablecerPosicionFirmaHandler(ISolicitudFirmaRepository repositorio)
    : IRequestHandler<EstablecerPosicionFirmaCommand, Result>
{
    public async Task<Result> Handle(EstablecerPosicionFirmaCommand request, CancellationToken ct)
    {
        var solicitud = await repositorio.ObtenerPorIdAsync(request.TenantId, request.SolicitudFirmaId, ct);
        if (solicitud is null) return Result.Fallido("Solicitud de firma no encontrada.");

        var posicion = PosicionFirma.Crear(request.NumeroPagina, request.X, request.Y, request.Ancho, request.Alto);
        if (!posicion.EsExitoso) return Result.Fallido(posicion.Error!);

        var resultado = solicitud.EstablecerPosicionFirma(request.FlujoFirmaId, posicion.Valor);
        if (!resultado.EsExitoso) return resultado;

        await repositorio.ActualizarAsync(solicitud, ct);
        return Result.Exitoso();
    }
}
