using MediatR;
using SecureSign.Domain.Primitives;
using SecureSign.Evidence.Domain;

namespace SecureSign.Evidence.Application.RegistrarEvidencia;

public sealed record RegistrarEvidenciaCommand(
    Guid TenantId,
    Guid DocumentoId,
    TipoEvidencia TipoEvidencia,
    DatosContextuales DatosContextuales,
    Guid? FirmaId) : IRequest<Result<Guid>>;
