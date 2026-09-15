namespace SecureSign.Signature.Application.Confianza;

/// <summary>
/// Cadena de certificados + CRL/OCSP crudos ya obtenidos durante la
/// validación de confianza — lo que <c>PdfDssWriter</c> necesita para
/// PAdES-LT (RUNBOOK.md 12.24). Tipo propio de Application (no el de
/// SecureSign.Trust) a propósito: esta capa no referencia Trust — el
/// adaptador de Infraestructura (<c>ValidadorConfianzaFirmanteIofe</c>) es
/// quien mapea de uno a otro, misma separación que ya existe para el resto
/// de <see cref="ResultadoConfianzaFirmante"/>.
/// </summary>
public sealed record MaterialValidacionLargoPlazo(
    IReadOnlyList<byte[]> CadenaCertificadosDer,
    byte[]? CrlDer,
    byte[]? OcspRespuestaDer);

/// <param name="Confiable">
/// true solo si el certificado está vigente, su cadena cierra en una raíz
/// de confianza, esa cadena está acreditada y bajo supervisión en la TSL de
/// IOFE, su KeyUsage es de firma, y su revocación (OCSP y/o CRL) resultó
/// explícitamente "no revocado" — nunca por no haber podido determinarla.
/// </param>
/// <param name="Evidencia">Detalle auditable de cada comprobación — nunca solo un booleano (ver RUNBOOK.md 12.9).</param>
/// <param name="MaterialLargoPlazo">Ver <see cref="MaterialValidacionLargoPlazo"/>.</param>
public sealed record ResultadoConfianzaFirmante(bool Confiable, IReadOnlyList<string> Evidencia, MaterialValidacionLargoPlazo MaterialLargoPlazo);

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
