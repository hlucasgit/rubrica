using MediatR;
using SecureSign.Domain.Primitives;
using SecureSign.Evidence.Domain;

namespace SecureSign.Evidence.Application.RegistrarEvidencia;

/// <summary>
/// Consumidor único autorizado a escribir en la cadena de evidencia (ver
/// docs/01-arquitectura/arquitectura-general.md principio #4). En producción
/// este handler se invoca como reacción a eventos de dominio publicados por
/// Documentos/Firma/Identidad en el bus, no por llamada directa de un cliente externo.
/// </summary>
public sealed class RegistrarEvidenciaHandler(IEventoEvidenciaRepository repositorio)
    : IRequestHandler<RegistrarEvidenciaCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(RegistrarEvidenciaCommand request, CancellationToken ct)
    {
        var ultimo = await repositorio.ObtenerUltimoAsync(request.TenantId, ct);

        var evento = EventoEvidencia.Crear(
            request.TenantId,
            request.DocumentoId,
            request.TipoEvidencia,
            request.DatosContextuales,
            ultimo?.HashEvento,
            request.FirmaId);

        await repositorio.AgregarAsync(evento, ct);
        return Result.Exitoso(evento.Id);
    }
}
