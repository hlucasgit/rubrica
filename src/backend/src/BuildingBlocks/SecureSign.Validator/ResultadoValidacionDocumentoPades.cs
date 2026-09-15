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
/// Siempre false en esta versión, INCLUSO cuando <paramref name="InstanteSelloTiempo"/>
/// tiene valor: se implementó un cliente RFC 3161 real (ver
/// SecureSign.Tsa.ClienteTsaRfc3161, RUNBOOK.md 12.14) que sí produce y
/// verifica sellos de tiempo genuinos — pero esa verificación solo confirma
/// que el token no fue alterado DESPUÉS de emitido, no que la propia TSA
/// emisora sea, en sí misma, una autoridad acreditada/confiable (no hay
/// todavía un almacén de raíces de confianza para TSAs, análogo al de
/// SecureSign.Trust para certificados de firmante). Por eso
/// <paramref name="InstanteFirmaDeclarado"/> (el /M autodeclarado) sigue
/// siendo el instante que se usa para validar vigencia/revocación del
/// certificado — <paramref name="InstanteSelloTiempo"/> es información
/// adicional expuesta para auditoría, no todavía la base de la validación.
/// </param>
/// <param name="InstanteSelloTiempo">
/// El <c>genTime</c> de un TimeStampToken RFC 3161 real embebido en la
/// firma (PAdES-T), SOLO si ese token está presente y su firma CMS interna
/// verifica contra su propio certificado — null si la firma es PAdES-B
/// (sin sello) o si el token embebido está corrupto/alterado.
/// </param>
/// <param name="SelloTiempoAutoridad">El firmante (Subject DN) del certificado de la TSA que emitió el sello, si lo hay.</param>
public sealed record ResultadoValidacionFirmaPades(
    string? NombreFirma,
    bool FirmaCriptograficaValida,
    X509Certificate2? Certificado,
    DateTimeOffset? InstanteFirmaDeclarado,
    bool InstanteFirmaConfiable,
    DateTimeOffset? InstanteSelloTiempo,
    string? SelloTiempoAutoridad,
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
        InstanteSelloTiempo: null,
        SelloTiempoAutoridad: null,
        ValidacionCertificado: null,
        Evidencia: [error],
        Error: error);
}

/// <summary>
/// Expediente de UN sello de tiempo de ARCHIVO (PAdES-LTA, RUNBOOK.md
/// 12.24) — deliberadamente separado de <see cref="ResultadoValidacionFirmaPades"/>:
/// no es la firma de una persona, es una prueba independiente de que el
/// documento (con sus firmas y su DSS) ya existía en <see cref="GenTime"/>.
/// Nunca participa en <see cref="ResultadoValidacionDocumentoPades.DocumentoValido"/>
/// — es información adicional para auditoría, igual que el sello de tiempo
/// de PAdES-T (ver <see cref="ResultadoValidacionFirmaPades.InstanteSelloTiempo"/>).
/// </summary>
public sealed record ResultadoValidacionSelloArchivo(
    bool Valido,
    DateTimeOffset? GenTime,
    string? AutoridadTsa,
    string? Error);

/// <summary>
/// Expediente de validación de TODAS las firmas /Sig de un PDF — ver
/// SecureSign.Validator.ValidadorDocumentoPades.
/// </summary>
public sealed record ResultadoValidacionDocumentoPades(
    int TotalFirmas,
    IReadOnlyList<ResultadoValidacionFirmaPades> Firmas,
    IReadOnlyList<ResultadoValidacionSelloArchivo> SellosDeArchivo)
{
    /// <summary>
    /// El documento es válido solo si tiene al menos una firma y TODAS sus
    /// firmas pasan completas (criptografía + confianza IOFE) — un documento
    /// sin ningún /Sig no está "válido por defecto", está simplemente sin firmar.
    /// Los sellos de archivo (PAdES-LTA) NO afectan este veredicto — ver
    /// comentario de <see cref="ResultadoValidacionSelloArchivo"/>.
    /// </summary>
    public bool DocumentoValido => TotalFirmas > 0 && Firmas.All(f => f.EstadoFinal);
}
