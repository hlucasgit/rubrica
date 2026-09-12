using MediatR;
using SecureSign.Documents.Domain;
using SecureSign.Domain.Primitives;

namespace SecureSign.Documents.Application.MarcarDocumentoFirmado;

public sealed class MarcarDocumentoFirmadoHandler(IDocumentoRepository repositorio)
    : IRequestHandler<MarcarDocumentoFirmadoCommand, Result>
{
    public async Task<Result> Handle(MarcarDocumentoFirmadoCommand request, CancellationToken ct)
    {
        var documento = await repositorio.ObtenerPorIdAsync(request.TenantId, request.DocumentoId, ct);
        if (documento is null) return Result.Fallido("Documento no encontrado.");

        var resultado = documento.MarcarComoFirmado();
        if (!resultado.EsExitoso) return resultado;

        await repositorio.ActualizarAsync(documento, ct);
        return Result.Exitoso();
    }
}
