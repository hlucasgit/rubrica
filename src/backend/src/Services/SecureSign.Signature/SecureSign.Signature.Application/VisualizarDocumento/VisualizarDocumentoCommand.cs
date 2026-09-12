using MediatR;
using SecureSign.Domain.Primitives;
using SecureSign.Signature.Application.Clients;

namespace SecureSign.Signature.Application.VisualizarDocumento;

public sealed record VisualizarDocumentoCommand(
    Guid TenantId,
    Guid SolicitudFirmaId,
    Guid FlujoFirmaId,
    DatosContextualesRemoto DatosContextuales) : IRequest<Result>;
