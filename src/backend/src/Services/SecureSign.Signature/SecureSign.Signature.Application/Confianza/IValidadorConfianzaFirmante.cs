namespace SecureSign.Signature.Application.Confianza;

/// <param name="Confiable">
/// true solo si el certificado está vigente, su cadena cierra en una raíz
/// de confianza, esa cadena está acreditada y bajo supervisión en la TSL de
/// IOFE, su KeyUsage es de firma, y su revocación (OCSP y/o CRL) resultó
/// explícitamente "no revocado" — nunca por no haber podido determinarla.
/// </param>
/// <param name="Evidencia">Detalle auditable de cada comprobación — nunca solo un booleano (ver RUNBOOK.md 12.9).</param>
public sealed record ResultadoConfianzaFirmante(bool Confiable, IReadOnlyList<string> Evidencia);

/// <summary>
/// Motor de confianza IOFE — responde "¿este certificado era de fiar en
/// este instante?" (vigencia, cadena, acreditación IOFE, propósito,
/// revocación), NUNCA "¿la firma es matemáticamente correcta?" (eso lo
/// hacen VerificadorFirmaExterna/PdfSignatureVerifier). La implementación
/// real vive en Infraestructura, sobre SecureSign.Trust — ver RUNBOOK.md
/// sección 12.9 y el informe de preauditoría INDECOPI/IOFE, hallazgo P0-01.
/// </summary>
public interface IValidadorConfianzaFirmante
{
    Task<ResultadoConfianzaFirmante> ValidarAsync(byte[] certificadoDer, DateTimeOffset instante, CancellationToken ct = default);
}
