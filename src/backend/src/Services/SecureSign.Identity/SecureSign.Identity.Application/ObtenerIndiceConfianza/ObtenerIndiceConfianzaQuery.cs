using MediatR;
using SecureSign.Domain.Primitives;

namespace SecureSign.Identity.Application.ObtenerIndiceConfianza;

public sealed record IndiceConfianzaResponse(
    Guid UsuarioId,
    int Indice,
    bool TieneCertificadoVigente,
    bool EsCuentaInstitucional,
    bool TieneValidacionExitosa);

/// <summary>
/// Obtiene (creando el perfil con valores por defecto de "usuario nuevo" si
/// es la primera vez que se consulta) el índice de confianza vigente.
/// </summary>
public sealed record ObtenerIndiceConfianzaQuery(Guid TenantId, Guid UsuarioId) : IRequest<Result<IndiceConfianzaResponse>>;
