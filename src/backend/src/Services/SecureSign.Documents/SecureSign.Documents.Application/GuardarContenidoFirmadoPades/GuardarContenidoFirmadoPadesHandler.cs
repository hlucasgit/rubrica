using MediatR;
using SecureSign.Documents.Domain;
using SecureSign.Domain.Primitives;

namespace SecureSign.Documents.Application.GuardarContenidoFirmadoPades;

public sealed class GuardarContenidoFirmadoPadesHandler(IDocumentoRepository repositorio)
    : IRequestHandler<GuardarContenidoFirmadoPadesCommand, Result>
{
    public async Task<Result> Handle(GuardarContenidoFirmadoPadesCommand request, CancellationToken ct)
    {
        if (request.Contenido.Length == 0)
            return Result.Fallido("El contenido recibido está vacío.");

        var documento = await repositorio.ObtenerPorIdAsync(request.TenantId, request.DocumentoId, ct);
        if (documento is null) return Result.Fallido("Documento no encontrado.");

        documento.GuardarContenidoFirmadoPades(request.Contenido);
        await repositorio.ActualizarAsync(documento, ct);
        return Result.Exitoso();
    }
}
