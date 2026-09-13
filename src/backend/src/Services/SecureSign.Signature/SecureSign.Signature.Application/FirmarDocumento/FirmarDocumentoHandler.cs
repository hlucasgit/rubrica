using MediatR;
using SecureSign.Domain.Primitives;
using SecureSign.Identity.Domain;
using SecureSign.Signature.Application.Clients;
using SecureSign.Signature.Domain;

namespace SecureSign.Signature.Application.FirmarDocumento;

/// <summary>
/// Orquesta la transición completa a "Firmado": consulta el Índice de
/// Confianza Digital del firmante y lo exige contra el tipo de firma
/// solicitado (innovación #5), obtiene el hash vigente del documento,
/// solicita la operación criptográfica al Servicio Criptográfico, aplica la
/// transición de dominio, marca el documento como firmado y registra el
/// evento en la cadena de evidencia. Es el punto de integración real entre
/// los cinco servicios (Firma, Identidad, Documentos, Criptografía,
/// Evidencia) descrito en docs/01-arquitectura/arquitectura-general.md
/// sección 4.
///
/// SIMPLIFICACIÓN: el índice de confianza del firmante refleja únicamente
/// las señales registradas explícitamente contra el Servicio de Identidad
/// (ver SecureSign.Identity.Api) — no hay todavía validación real de OTP,
/// biometría ni un certificado emitido por una EC acreditada que lo eleve
/// automáticamente. Un usuario nunca antes visto arranca en 30
/// (UsuarioNuevo), suficiente solo para TipoFirma.Simple.
/// </summary>
public sealed class FirmarDocumentoHandler(
    ISolicitudFirmaRepository repositorio,
    IIdentidadServiceClient identidad,
    IDocumentosServiceClient documentos,
    ICriptografiaServiceClient criptografia,
    IEvidenciasServiceClient evidencias)
    : IRequestHandler<FirmarDocumentoCommand, Result<FirmarDocumentoResponse>>
{
    public async Task<Result<FirmarDocumentoResponse>> Handle(FirmarDocumentoCommand request, CancellationToken ct)
    {
        var solicitud = await repositorio.ObtenerPorIdAsync(request.TenantId, request.SolicitudFirmaId, ct);
        if (solicitud is null) return Result.Fallido<FirmarDocumentoResponse>("Solicitud de firma no encontrada.");

        var flujo = solicitud.Flujos.FirstOrDefault(f => f.Id == request.FlujoFirmaId);
        if (flujo is null) return Result.Fallido<FirmarDocumentoResponse>("Flujo de firma no encontrado.");

        // Gate de confianza dinámica: se evalúa ANTES de mutar el estado de la
        // solicitud, para que un intento denegado no dañe la máquina de
        // estados ni requiera un mecanismo de rollback.
        var confianza = await identidad.ObtenerIndiceConfianzaAsync(flujo.FirmanteUsuarioId, ct);
        if (!IndiceConfianzaDigital.PuedeEjecutar(confianza.Indice, solicitud.TipoFirma))
        {
            return Result.Fallido<FirmarDocumentoResponse>(
                $"El firmante no alcanza el nivel de confianza requerido para firma {solicitud.TipoFirma} " +
                $"(índice actual: {confianza.Indice}). Ver docs/02-innovacion-patente, innovación #5.");
        }

        var inicioValidacion = solicitud.IniciarValidacionIdentidad(request.FlujoFirmaId);
        if (!inicioValidacion.EsExitoso) return Result.Fallido<FirmarDocumentoResponse>(inicioValidacion.Error!);

        var documento = await documentos.ObtenerAsync(solicitud.DocumentoId, ct);
        if (documento is null) return Result.Fallido<FirmarDocumentoResponse>("El documento asociado ya no existe.");

        var referenciaLlave = await criptografia.ObtenerOGenerarLlaveAsync(flujo.FirmanteUsuarioId, ct);
        var firma = await criptografia.FirmarAsync(referenciaLlave, documento.HashSha256, request.PinFirmante, ct);

        var confirmacion = solicitud.ConfirmarFirma(request.FlujoFirmaId);
        if (!confirmacion.EsExitoso) return Result.Fallido<FirmarDocumentoResponse>(confirmacion.Error!);

        await repositorio.ActualizarAsync(solicitud, ct);

        if (solicitud.Estado == EstadoSolicitudFirma.Firmado)
            await documentos.MarcarFirmadoAsync(solicitud.DocumentoId, ct);

        await evidencias.RegistrarAsync(solicitud.DocumentoId, "Firma", request.DatosContextuales, ct: ct);

        return Result.Exitoso(new FirmarDocumentoResponse(solicitud.Estado.ToString(), firma.Algoritmo, firma.FirmaBase64));
    }
}
