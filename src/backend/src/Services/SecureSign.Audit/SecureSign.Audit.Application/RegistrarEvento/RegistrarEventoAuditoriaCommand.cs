using MediatR;
using SecureSign.Audit.Domain;
using SecureSign.Domain.Primitives;

namespace SecureSign.Audit.Application.RegistrarEvento;

public sealed record RegistrarEventoAuditoriaCommand(
    TipoEventoAuditoria TipoEvento,
    string Detalle,
    Guid? TenantId,
    string? OrigenIp) : IRequest<Result<Guid>>;

public sealed class RegistrarEventoAuditoriaHandler(IEventoAuditoriaRepository repositorio)
    : IRequestHandler<RegistrarEventoAuditoriaCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(RegistrarEventoAuditoriaCommand request, CancellationToken ct)
    {
        var evento = EventoAuditoria.Crear(request.TipoEvento, request.Detalle, request.TenantId, request.OrigenIp);
        await repositorio.AgregarAsync(evento, ct);
        return Result.Exitoso(evento.Id);
    }
}
