using MediatR;
using SecureSign.Documents.Application.Clients;
using SecureSign.Documents.Domain;
using SecureSign.Domain.Primitives;

namespace SecureSign.Documents.Application.RegistrarDocumento;

public sealed class RegistrarDocumentoHandler(
    IDocumentoRepository repositorio,
    IAlmacenamientoDocumental almacenamiento,
    IEvidenciasServiceClient evidencias)
    : IRequestHandler<RegistrarDocumentoCommand, Result<RegistrarDocumentoResponse>>
{
    public async Task<Result<RegistrarDocumentoResponse>> Handle(RegistrarDocumentoCommand request, CancellationToken ct)
    {
        var existente = await repositorio.ObtenerPorHashAsync(
            request.TenantId,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(request.Contenido)).ToLowerInvariant(),
            ct);

        if (existente is not null && existente.CodigoExterno == request.CodigoExterno)
            return Result.Exitoso(new RegistrarDocumentoResponse(existente.Id, existente.Hash.ValorHex, existente.Estado.ToString()));

        var urlAlmacenamiento = await almacenamiento.GuardarAsync(request.TenantId, request.NombreArchivo, request.Contenido, ct);

        var resultado = Documento.Registrar(
            request.TenantId,
            request.NombreArchivo,
            request.TipoContenido,
            request.Contenido,
            urlAlmacenamiento,
            request.CreadoPor,
            request.CodigoExterno);

        if (!resultado.EsExitoso)
            return Result.Fallido<RegistrarDocumentoResponse>(resultado.Error!);

        var documento = resultado.Valor;
        await repositorio.AgregarAsync(documento, ct);

        // Primer eslabón de la cadena de evidencia del documento (ver
        // docs/02-innovacion-patente, innovación #1).
        await evidencias.RegistrarAsync(documento.Id, "Carga", request.DatosContextuales, ct);

        return Result.Exitoso(new RegistrarDocumentoResponse(documento.Id, documento.Hash.ValorHex, documento.Estado.ToString()));
    }
}

/// <summary>
/// Abstracción sobre el almacenamiento de objetos (S3-compatible / Azure Blob).
/// La implementación real vive en la capa de Infraestructura.
/// </summary>
public interface IAlmacenamientoDocumental
{
    Task<string> GuardarAsync(Guid tenantId, string nombreArchivo, byte[] contenido, CancellationToken ct = default);

    Task<byte[]> LeerAsync(string urlAlmacenamiento, CancellationToken ct = default);
}
