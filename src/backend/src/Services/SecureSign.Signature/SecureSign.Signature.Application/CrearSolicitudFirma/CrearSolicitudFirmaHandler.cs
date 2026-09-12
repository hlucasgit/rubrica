using MediatR;
using SecureSign.Domain.Primitives;
using SecureSign.Signature.Domain;

namespace SecureSign.Signature.Application.CrearSolicitudFirma;

public sealed class CrearSolicitudFirmaHandler(
    ISolicitudFirmaRepository repositorio,
    IGeneradorUrlFirma generadorUrl)
    : IRequestHandler<CrearSolicitudFirmaCommand, Result<CrearSolicitudFirmaResponse>>
{
    public async Task<Result<CrearSolicitudFirmaResponse>> Handle(CrearSolicitudFirmaCommand request, CancellationToken ct)
    {
        var firmantes = request.Firmantes.Select(f => (f.UsuarioId, f.Orden)).ToList();

        var resultado = SolicitudFirma.Crear(
            request.TenantId,
            request.DocumentoId,
            request.TipoFirma,
            request.RequiereOrdenSecuencial,
            firmantes,
            request.CreadoPor,
            request.FechaLimite,
            request.ClienteIntegradorId);

        if (!resultado.EsExitoso)
            return Result.Fallido<CrearSolicitudFirmaResponse>(resultado.Error!);

        var solicitud = resultado.Valor;
        await repositorio.AgregarAsync(solicitud, ct);

        var primerFlujo = solicitud.ObtenerFirmantesElegiblesParaNotificar().First();
        var urlFirma = generadorUrl.Generar(solicitud.TenantId, primerFlujo.TokenAccesoUnico);

        return Result.Exitoso(new CrearSolicitudFirmaResponse(solicitud.Id, solicitud.CodigoVerificacionPublico, urlFirma));
    }
}

/// <summary>
/// Genera la URL (o referencia de token) de firma resuelta por el BFF White
/// Label bajo el dominio personalizado del tenant (ver docs/06-white-label).
/// </summary>
public interface IGeneradorUrlFirma
{
    string Generar(Guid tenantId, string tokenAccesoUnico);
}
