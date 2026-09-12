using MediatR;
using SecureSign.Domain.Primitives;
using SecureSign.Identity.Domain;

namespace SecureSign.Identity.Application.ObtenerIndiceConfianza;

public sealed class ObtenerIndiceConfianzaHandler(IUsuarioIdentidadRepository repositorio)
    : IRequestHandler<ObtenerIndiceConfianzaQuery, Result<IndiceConfianzaResponse>>
{
    public async Task<Result<IndiceConfianzaResponse>> Handle(ObtenerIndiceConfianzaQuery request, CancellationToken ct)
    {
        var usuario = await repositorio.ObtenerAsync(request.TenantId, request.UsuarioId, ct);

        if (usuario is null)
        {
            usuario = UsuarioIdentidad.Registrar(request.TenantId, request.UsuarioId);
            await repositorio.AgregarAsync(usuario, ct);
        }

        return Result.Exitoso(new IndiceConfianzaResponse(
            usuario.UsuarioId,
            usuario.CalcularIndiceConfianza(),
            usuario.TieneCertificadoVigente,
            usuario.EsCuentaInstitucional,
            usuario.TieneValidacionExitosa));
    }
}
