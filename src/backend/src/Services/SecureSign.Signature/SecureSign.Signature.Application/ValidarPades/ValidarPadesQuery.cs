using MediatR;
using SecureSign.Domain.Primitives;
using SecureSign.Signature.Application.Clients;

namespace SecureSign.Signature.Application.ValidarPades;

/// <summary>
/// Expediente de validación de UNA firma /Sig — ver
/// SecureSign.Validator.ResultadoValidacionFirmaPades (la implementación
/// real vive en Infraestructura, sobre SecureSign.Validator; ver informe de
/// preauditoría INDECOPI/IOFE, hallazgo P0-05). Deliberadamente plano
/// (strings/bools/fechas, sin X509Certificate2 ni tipos de SecureSign.Trust)
/// para que la Aplicación no dependa de esas librerías.
/// </summary>
public sealed record FirmaValidadaDto(
    string? NombreFirmante,
    bool FirmaCriptograficaValida,
    string? CertificadoSujeto,
    string? CertificadoEmisor,
    DateTimeOffset? CertificadoVigenteDesde,
    DateTimeOffset? CertificadoVigenteHasta,
    DateTimeOffset? InstanteFirmaDeclarado,
    bool InstanteFirmaConfiable,
    DateTimeOffset? InstanteSelloTiempo,
    string? SelloTiempoAutoridad,
    bool CertificadoVigente,
    bool CadenaValida,
    bool RaizConfiableIofe,
    bool PropositoValido,
    string EstadoRevocacionOcsp,
    string EstadoRevocacionCrl,
    string EstadoRevocacionCombinado,
    bool EstadoFinal,
    IReadOnlyList<string> Evidencia,
    string? Error);

public sealed record ResultadoValidarPadesResponse(
    int TotalFirmas,
    bool DocumentoValido,
    IReadOnlyList<FirmaValidadaDto> Firmas);

/// <summary>
/// Puerto hacia el validador independiente de PAdES — la implementación
/// real (Infraestructura) compone SecureSign.Pades.PdfSignatureVerifier con
/// SecureSign.Trust.ValidadorCertificados, sin depender del código que
/// generó la firma (SecureSign.Pades.PdfSignaturePlaceholder). Cualquier PDF
/// con un PAdES/CMS estándar se valida igual, sin importar qué software lo produjo.
/// </summary>
public interface IValidadorDocumentoPadesIndependiente
{
    Task<ResultadoValidarPadesResponse> ValidarAsync(byte[] pdf, CancellationToken ct = default);
}

/// <param name="Pdf">El PDF completo tal cual se recibió — cualquier documento PAdES, generado por SecureSign o por un tercero.</param>
public sealed record ValidarPadesQuery(byte[] Pdf) : IRequest<Result<ResultadoValidarPadesResponse>>;

public sealed class ValidarPadesHandler(IValidadorDocumentoPadesIndependiente validador, IAuditoriaServiceClient auditoria)
    : IRequestHandler<ValidarPadesQuery, Result<ResultadoValidarPadesResponse>>
{
    public async Task<Result<ResultadoValidarPadesResponse>> Handle(ValidarPadesQuery request, CancellationToken ct)
    {
        if (request.Pdf.Length == 0)
            return Result.Fallido<ResultadoValidarPadesResponse>("El documento recibido está vacío.");

        var resultado = await validador.ValidarAsync(request.Pdf, ct);

        // Registro de validación criptográfica (informe de preauditoría
        // INDECOPI/IOFE, sección 8: el tercer concepto, distinto de
        // evidencia de negocio y auditoría técnica genérica) — este
        // endpoint es público a propósito (RUNBOOK.md 12.12: cualquier
        // destinatario de un documento debe poder validarlo), así que no
        // hay tenant que ligar; se registra igual, sin tenant.
        try
        {
            await auditoria.RegistrarAsync(
                "ValidacionPadesIndependiente",
                $"{resultado.TotalFirmas} firma(s), documentoValido={resultado.DocumentoValido}",
                tenantId: null, ct);
        }
        catch (HttpRequestException) { /* la auditoría nunca debe impedir que se devuelva el resultado real de la validación */ }

        return Result.Exitoso(resultado);
    }
}
