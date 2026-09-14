using MediatR;
using SecureSign.Domain.Primitives;

namespace SecureSign.Documents.Application.GuardarContenidoFirmadoPades;

/// <summary>
/// Invocado internamente por el Servicio de Firma cuando el Firmador Local
/// (ver RUNBOOK.md 12.8) produce un PDF con firma PAdES real incrustada —
/// reemplaza lo que devuelve GET /api/documentos/{id}/firmado.
/// </summary>
public sealed record GuardarContenidoFirmadoPadesCommand(Guid TenantId, Guid DocumentoId, byte[] Contenido) : IRequest<Result>;
