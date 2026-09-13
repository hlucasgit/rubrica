using MediatR;
using SecureSign.Domain.Primitives;
using SecureSign.Identity.Domain;
using SecureSign.Signature.Application.Clients;
using SecureSign.Signature.Application.FirmarDocumento;
using SecureSign.Signature.Domain;

namespace SecureSign.Signature.Application.FirmarLocal;

/// <summary>
/// Misma orquestación que FirmarDocumentoHandler (gate de confianza →
/// validación de identidad → evidencia), pero SIN pasar por el Servicio
/// Criptográfico: la firma ya fue calculada por el Firmador Local en la
/// máquina del firmante, con SU certificado. Aquí solo se verifica
/// matemáticamente que esa firma corresponde al hash ACTUAL del documento
/// (recalculado desde el Servicio Documental, nunca confiado del llamador)
/// y a la llave pública del certificado recibido — ver VerificadorFirmaExterna.
/// </summary>
public sealed class FirmarLocalHandler(
    ISolicitudFirmaRepository repositorio,
    IIdentidadServiceClient identidad,
    IDocumentosServiceClient documentos,
    IEvidenciasServiceClient evidencias)
    : IRequestHandler<FirmarLocalCommand, Result<FirmarDocumentoResponse>>
{
    public async Task<Result<FirmarDocumentoResponse>> Handle(FirmarLocalCommand request, CancellationToken ct)
    {
        var solicitud = await repositorio.ObtenerPorIdAsync(request.TenantId, request.SolicitudFirmaId, ct);
        if (solicitud is null) return Result.Fallido<FirmarDocumentoResponse>("Solicitud de firma no encontrada.");

        var flujo = solicitud.Flujos.FirstOrDefault(f => f.Id == request.FlujoFirmaId);
        if (flujo is null) return Result.Fallido<FirmarDocumentoResponse>("Flujo de firma no encontrado.");

        var confianza = await identidad.ObtenerIndiceConfianzaAsync(flujo.FirmanteUsuarioId, ct);
        if (!IndiceConfianzaDigital.PuedeEjecutar(confianza.Indice, solicitud.TipoFirma))
        {
            return Result.Fallido<FirmarDocumentoResponse>(
                $"El firmante no alcanza el nivel de confianza requerido para firma {solicitud.TipoFirma} " +
                $"(índice actual: {confianza.Indice}).");
        }

        var inicioValidacion = solicitud.IniciarValidacionIdentidad(request.FlujoFirmaId);
        if (!inicioValidacion.EsExitoso) return Result.Fallido<FirmarDocumentoResponse>(inicioValidacion.Error!);

        var documento = await documentos.ObtenerAsync(solicitud.DocumentoId, ct);
        if (documento is null) return Result.Fallido<FirmarDocumentoResponse>("El documento asociado ya no existe.");

        byte[] hash, firma, certificado;
        try
        {
            hash = Convert.FromHexString(documento.HashSha256);
            firma = Convert.FromBase64String(request.FirmaBase64);
            certificado = Convert.FromBase64String(request.CertificadoBase64);
        }
        catch (FormatException)
        {
            return Result.Fallido<FirmarDocumentoResponse>("La firma o el certificado recibidos no son Base64/hex válidos.");
        }

        bool firmaValida;
        try
        {
            firmaValida = VerificadorFirmaExterna.Verificar(hash, firma, certificado);
        }
        catch (Exception)
        {
            return Result.Fallido<FirmarDocumentoResponse>("El certificado recibido no es válido.");
        }

        if (!firmaValida)
            return Result.Fallido<FirmarDocumentoResponse>("La firma no corresponde al hash actual del documento y/o al certificado indicado.");

        var confirmacion = solicitud.ConfirmarFirma(request.FlujoFirmaId);
        if (!confirmacion.EsExitoso) return Result.Fallido<FirmarDocumentoResponse>(confirmacion.Error!);

        await repositorio.ActualizarAsync(solicitud, ct);

        if (solicitud.Estado == EstadoSolicitudFirma.Firmado)
            await documentos.MarcarFirmadoAsync(solicitud.DocumentoId, ct);

        await evidencias.RegistrarAsync(solicitud.DocumentoId, "Firma", request.DatosContextuales, ct: ct);

        return Result.Exitoso(new FirmarDocumentoResponse(solicitud.Estado.ToString(), request.Algoritmo, request.FirmaBase64));
    }
}
