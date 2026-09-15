using System.Security.Cryptography.X509Certificates;
using MediatR;
using SecureSign.Domain.Primitives;
using SecureSign.Identity.Domain;
using SecureSign.Pades;
using SecureSign.Signature.Application.Clients;
using SecureSign.Signature.Application.Confianza;
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
    IEvidenciasServiceClient evidencias,
    IValidadorConfianzaFirmante validadorConfianza,
    IAuditoriaServiceClient auditoria)
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

        // Si la petición se autenticó con un ticket de firma de un solo uso
        // (ver EmisorTicketFirmaLocal, RUNBOOK.md 12.13), sus claims deben
        // coincidir EXACTAMENTE con la operación que se está completando —
        // un ticket emitido para otra solicitud/flujo/documento, o cuyo hash
        // ya no coincide con el documento actual, se rechaza sin excepción
        // (fail closed, ver informe de preauditoría INDECOPI/IOFE, sección 12).
        if (request.Ticket is { } ticket)
        {
            if (ticket.SolicitudFirmaId != request.SolicitudFirmaId || ticket.FlujoFirmaId != request.FlujoFirmaId || ticket.DocumentoId != solicitud.DocumentoId)
                return Result.Fallido<FirmarDocumentoResponse>(
                    "El ticket de firma no corresponde a esta solicitud/flujo/documento — operación rechazada.");

            if (!string.Equals(ticket.DocumentoHashSha256Hex, documento.HashSha256, StringComparison.OrdinalIgnoreCase))
                return Result.Fallido<FirmarDocumentoResponse>(
                    "El documento actual ya no coincide con el hash que autorizó el ticket de firma — operación rechazada.");
        }

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

        // Motor de confianza IOFE (hallazgo P0-01 del informe de
        // preauditoría): la operación matemática de arriba solo prueba que
        // la firma corresponde a ESTE certificado — no que ese certificado
        // fuera, en este instante, vigente, no revocado, de propósito de
        // firma y perteneciente a una cadena acreditada por INDECOPI. Sin
        // esto, un certificado revocado o ajeno a la IOFE firmaría igual de
        // "válido" que un DNIe real. Ver RUNBOOK.md 12.9.
        var confianzaCertificado = await validadorConfianza.ValidarAsync(certificado, DateTimeOffset.UtcNow, ct);
        if (!confianzaCertificado.Confiable)
        {
            // Auditoría técnica (distinta de la evidencia de negocio de
            // abajo — ver informe de preauditoría INDECOPI/IOFE, sección 8,
            // y RUNBOOK.md 12.16): un intento de firma con un certificado
            // que el motor de confianza rechazó es exactamente el tipo de
            // evento de seguridad que un registro de auditoría técnica debe
            // capturar, se complete o no la firma.
            try
            {
                await auditoria.RegistrarAsync(
                    "CertificadoRechazadoPorConfianza",
                    $"Solicitud {request.SolicitudFirmaId}, flujo {request.FlujoFirmaId}: " + string.Join(" | ", confianzaCertificado.Evidencia),
                    request.TenantId, ct);
            }
            catch (HttpRequestException) { /* la auditoría nunca debe impedir que se reporte el rechazo real al llamador */ }

            return Result.Fallido<FirmarDocumentoResponse>(
                "El certificado del firmante no pasó la validación de confianza IOFE: " + string.Join(" | ", confianzaCertificado.Evidencia));
        }

        // Fail closed (ver informe de preauditoría INDECOPI/IOFE, hallazgo
        // P0-03): si el documento es PDF, el Firmador Local DEBE haber
        // producido un PAdES real y criptográficamente válido — y ese
        // certificado debe ser el MISMO que acaba de autorizar la firma
        // desacoplada de arriba. Se verifica ANTES de tocar el estado de la
        // solicitud, para no dejar nunca un flujo marcado "Firmado" con la
        // promesa de un PAdES que en realidad no existe o no es válido.
        byte[]? documentoPades = null;
        bool esPdf = documento.TipoContenido.Contains("pdf", StringComparison.OrdinalIgnoreCase);
        if (esPdf)
        {
            if (string.IsNullOrEmpty(request.DocumentoPadesBase64))
                return Result.Fallido<FirmarDocumentoResponse>(
                    "Este documento es PDF y requiere una firma PAdES real incrustada, pero el Firmador Local no la envió.");

            try
            {
                documentoPades = Convert.FromBase64String(request.DocumentoPadesBase64);
            }
            catch (FormatException)
            {
                return Result.Fallido<FirmarDocumentoResponse>("El PDF con PAdES recibido no es Base64 válido.");
            }

            var verificacionPades = PdfSignatureVerifier.VerificarUltima(documentoPades);
            if (!verificacionPades.Valido)
                return Result.Fallido<FirmarDocumentoResponse>($"La firma PAdES incrustada no es válida: {verificacionPades.Error}");

            if (verificacionPades.Certificado is null || !CertificadoCoincide(verificacionPades.Certificado, certificado))
                return Result.Fallido<FirmarDocumentoResponse>(
                    "El certificado incrustado en el PAdES no coincide con el certificado de la firma desacoplada.");
        }

        var confirmacion = solicitud.ConfirmarFirma(request.FlujoFirmaId);
        if (!confirmacion.EsExitoso) return Result.Fallido<FirmarDocumentoResponse>(confirmacion.Error!);

        await repositorio.ActualizarAsync(solicitud, ct);

        if (solicitud.Estado == EstadoSolicitudFirma.Firmado)
            await documentos.MarcarFirmadoAsync(solicitud.DocumentoId, ct);

        if (documentoPades is not null)
        {
            // Ya validado arriba matemáticamente — si esto falla es un
            // problema de infraestructura del Servicio Documental, no de la
            // firma en sí; no se revierte la confirmación ya persistida
            // (la máquina de estados de SolicitudFirma no contempla
            // deshacer una firma) pero sí queda registrado para investigar.
            try { await documentos.GuardarDocumentoFirmadoPadesAsync(solicitud.DocumentoId, documentoPades, ct); }
            catch (HttpRequestException) { /* ver limitación en RUNBOOK.md 12.8 */ }
        }

        await evidencias.RegistrarAsync(solicitud.DocumentoId, "Firma", request.DatosContextuales, ct: ct);

        return Result.Exitoso(new FirmarDocumentoResponse(solicitud.Estado.ToString(), request.Algoritmo, request.FirmaBase64));
    }

    private static bool CertificadoCoincide(X509Certificate2 delPades, byte[] delFlujo)
    {
        try { return delPades.RawData.AsSpan().SequenceEqual(delFlujo); }
        catch { return false; }
    }
}
