using MediatR;
using SecureSign.Domain.Primitives;
using SecureSign.Identity.Domain;

namespace SecureSign.Identity.Application.RegistrarSenal;

public sealed class RegistrarSenalHandler(IUsuarioIdentidadRepository repositorio)
    : IRequestHandler<RegistrarSenalCommand, Result<int>>
{
    public async Task<Result<int>> Handle(RegistrarSenalCommand request, CancellationToken ct)
    {
        var usuario = await repositorio.ObtenerAsync(request.TenantId, request.UsuarioId, ct);
        var esNuevo = usuario is null;
        usuario ??= UsuarioIdentidad.Registrar(request.TenantId, request.UsuarioId);

        switch (request.TipoSenal)
        {
            case TipoSenalIdentidad.ValidacionExitosa:
                usuario.RegistrarValidacionExitosa();
                break;
            case TipoSenalIdentidad.CertificadoEmitido:
                usuario.MarcarCertificadoVigente();
                break;
            case TipoSenalIdentidad.CertificadoRevocado:
                usuario.RevocarCertificado();
                break;
            case TipoSenalIdentidad.CuentaInstitucional:
                usuario.MarcarCuentaInstitucional();
                break;
            case TipoSenalIdentidad.RechazoPorSospechaFraude:
                usuario.RegistrarRechazoPorSospechaFraude();
                break;
        }

        if (esNuevo)
            await repositorio.AgregarAsync(usuario, ct);
        else
            await repositorio.ActualizarAsync(usuario, ct);

        return Result.Exitoso(usuario.CalcularIndiceConfianza());
    }
}
