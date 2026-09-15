using MediatR;
using SecureSign.Audit.Domain;
using SecureSign.Domain.Primitives;

namespace SecureSign.Audit.Application.ConsultarEventos;

public sealed record EventoAuditoriaDto(Guid Id, string TipoEvento, string Detalle, string? OrigenIp, DateTimeOffset OcurridoEn);

/// <param name="TenantId">Obligatorio: un tenant solo puede consultar SUS propios eventos, nunca los de otro (ni el amplio "todos" — eso sería un endpoint interno/administrativo aparte, no implementado).</param>
public sealed record ConsultarEventosAuditoriaQuery(
    Guid TenantId,
    TipoEventoAuditoria? TipoEvento,
    int Limite) : IRequest<Result<IReadOnlyList<EventoAuditoriaDto>>>;

public sealed class ConsultarEventosAuditoriaHandler(IEventoAuditoriaRepository repositorio)
    : IRequestHandler<ConsultarEventosAuditoriaQuery, Result<IReadOnlyList<EventoAuditoriaDto>>>
{
    public async Task<Result<IReadOnlyList<EventoAuditoriaDto>>> Handle(ConsultarEventosAuditoriaQuery request, CancellationToken ct)
    {
        int limite = Math.Clamp(request.Limite <= 0 ? 100 : request.Limite, 1, 500);
        var eventos = await repositorio.ConsultarAsync(request.TenantId, request.TipoEvento, limite, ct);

        IReadOnlyList<EventoAuditoriaDto> dtos = eventos
            .Select(e => new EventoAuditoriaDto(e.Id, e.TipoEvento.ToString(), e.Detalle, e.OrigenIp, e.OcurridoEn))
            .ToList();

        return Result.Exitoso(dtos);
    }
}
