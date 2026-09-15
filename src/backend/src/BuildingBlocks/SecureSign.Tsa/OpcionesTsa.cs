namespace SecureSign.Tsa;

/// <summary>
/// URL de la TSA (Autoridad de Sellado de Tiempo) RFC 3161 a usar. Por
/// defecto, la TSA pública y gratuita de DigiCert (no requiere autenticación
/// ni contrato — la misma que ya usa RUNBOOK.md 12.14 para PAdES-T). Para
/// IOFE real, sustituir por la URL de una TSA acreditada peruana cuando
/// exista — ver informe de preauditoría INDECOPI/IOFE, hallazgo P1
/// ("TSA / RFC 3161"). Compartida entre todo servicio que necesite pedir un
/// sello de tiempo (Signature.Api para PAdES-LTA, RUNBOOK.md 12.24) — no
/// duplicar esta clase por servicio.
/// </summary>
public sealed class OpcionesTsa
{
    public const string SeccionConfiguracion = "Tsa";
    public string UrlTsa { get; set; } = "http://timestamp.digicert.com";
}
