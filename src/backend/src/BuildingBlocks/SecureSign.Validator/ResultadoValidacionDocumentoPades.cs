using System.Security.Cryptography.X509Certificates;
using SecureSign.Trust;

namespace SecureSign.Validator;

/// <summary>
/// Expediente completo de validación de UNA firma /Sig dentro de un PDF —
/// combina la verificación matemática (<see cref="SecureSign.Pades.PdfSignatureVerifier"/>,
/// "¿la firma corresponde a este documento y a este certificado?") con la
/// validación de confianza IOFE (<see cref="SecureSign.Trust.ValidadorCertificados"/>,
/// "¿ese certificado era de fiar en el instante de la firma?"). Ver informe
/// de preauditoría INDECOPI/IOFE, hallazgo P0-05: "el resultado nunca debería
/// limitarse a true/false" — cada campo queda expuesto por separado para que
/// un auditor pueda revisar exactamente cuál comprobación falló, si alguna.
/// </summary>
/// <param name="InstanteFirmaConfiable">
/// Siempre false en esta versión: <paramref name="InstanteFirmaDeclarado"/>
/// es el valor de /M tal como lo declaró el propio firmante, NO un instante
/// probado por una autoridad de sellado de tiempo (TSA) independiente — ver
/// RUNBOOK.md 12.9 e ISellosTiempoProvider (sin implementación real
/// todavía). Se valida la confianza del certificado EN ese instante
/// declarado (mejor aproximación disponible hoy), pero el resultado nunca
/// debe presentarse como "validación de largo plazo" (PAdES-LT/LTA) hasta
/// que exista una TSA real.
/// </param>
public sealed record ResultadoValidacionFirmaPades(
    string? NombreFirma,
    bool FirmaCriptograficaValida,
    X509Certificate2? Certificado,
    DateTimeOffset? InstanteFirmaDeclarado,
    bool InstanteFirmaConfiable,
    ResultadoValidacionCertificado? ValidacionCertificado,
    IReadOnlyList<string> Evidencia,
    string? Error)
{
    /// <summary>
    /// true solo si la firma es matemáticamente válida Y el certificado pasó
    /// completa la validación de confianza IOFE (vigencia, cadena, TSL,
    /// propósito y revocación explícitamente "no revocado") en el instante
    /// declarado de la firma.
    /// </summary>
    public bool EstadoFinal =>
        Error is null
        && FirmaCriptograficaValida
        && ValidacionCertificado is { EstadoFinal: true };

    public static ResultadoValidacionFirmaPades Fallida(string? nombreFirma, string error) => new(
        NombreFirma: nombreFirma,
        FirmaCriptograficaValida: false,
        Certificado: null,
        InstanteFirmaDeclarado: null,
        InstanteFirmaConfiable: false,
        ValidacionCertificado: null,
        Evidencia: [error],
        Error: error);
}

/// <summary>
/// Expediente de validación de TODAS las firmas /Sig de un PDF — ver
/// SecureSign.Validator.ValidadorDocumentoPades.
/// </summary>
public sealed record ResultadoValidacionDocumentoPades(
    int TotalFirmas,
    IReadOnlyList<ResultadoValidacionFirmaPades> Firmas)
{
    /// <summary>
    /// El documento es válido solo si tiene al menos una firma y TODAS sus
    /// firmas pasan completas (criptografía + confianza IOFE) — un documento
    /// sin ningún /Sig no está "válido por defecto", está simplemente sin firmar.
    /// </summary>
    public bool DocumentoValido => TotalFirmas > 0 && Firmas.All(f => f.EstadoFinal);
}
