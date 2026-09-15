namespace SecureSign.Trust;

/// <summary>
/// Estado de una consulta de revocación — nunca colapsar "no se pudo saber"
/// en "válido". Ver informe de preauditoría INDECOPI/IOFE, hallazgo P0-01:
/// "los estados Unknown y Unavailable deben diferenciarse de Good; no poder
/// determinar una revocación jamás debe transformarse automáticamente en
/// 'certificado válido'".
/// </summary>
public enum EstadoRevocacion
{
    /// <summary>La fuente respondió explícitamente que el certificado no está revocado.</summary>
    Good,

    /// <summary>La fuente respondió explícitamente que el certificado SÍ está revocado.</summary>
    Revoked,

    /// <summary>La fuente respondió, pero no tiene información sobre este certificado en particular.</summary>
    Unknown,

    /// <summary>No se pudo consultar la fuente (red, timeout, formato inesperado, servicio caído, etc.).</summary>
    Unavailable
}

/// <summary>
/// Resultado combinado de OCSP y CRL para un mismo certificado. Revoked por
/// cualquiera de los dos gana siempre; Good requiere que AL MENOS uno haya
/// confirmado explícitamente que no está revocado — si ninguno pudo
/// confirmarlo, el resultado combinado es Unavailable (fail closed).
/// </summary>
public sealed record ResultadoRevocacion(EstadoRevocacion Ocsp, EstadoRevocacion Crl, IReadOnlyList<string> Evidencia)
{
    public EstadoRevocacion Combinado =>
        Ocsp == EstadoRevocacion.Revoked || Crl == EstadoRevocacion.Revoked ? EstadoRevocacion.Revoked :
        Ocsp == EstadoRevocacion.Good || Crl == EstadoRevocacion.Good ? EstadoRevocacion.Good :
        EstadoRevocacion.Unavailable;
}

/// <summary>
/// Material crudo (DER) recolectado durante la validación de confianza —
/// exactamente lo que un validador PAdES-LT necesita para revalidar la firma
/// SIN volver a consultar red (ETSI EN 319 142-1, RUNBOOK.md 12.24). Nunca
/// se genera aparte ni se vuelve a descargar: es subproducto directo de la
/// misma validación de vigencia/cadena/revocación que ya se hacía — si esa
/// validación no llegó a obtener CRL/OCSP (p. ej. Unavailable), el campo
/// correspondiente queda null, nunca inventado.
/// </summary>
public sealed record MaterialValidacionLargoPlazo(
    IReadOnlyList<byte[]> CadenaCertificadosDer,
    byte[]? CrlDer,
    byte[]? OcspRespuestaDer)
{
    public static readonly MaterialValidacionLargoPlazo Vacio = new(Array.Empty<byte[]>(), null, null);
}

/// <summary>
/// Resultado completo de validar UN certificado — deliberadamente nunca solo
/// verdadero/falso (ver hallazgo P0-05 del informe de preauditoría): cada
/// campo es auditable por separado, y <see cref="EstadoFinal"/> exige que
/// TODOS sean verdaderos, incluida la revocación combinada en Good (nunca
/// Unknown/Unavailable).
/// </summary>
public sealed record ResultadoValidacionCertificado(
    bool CertificadoVigente,
    bool CadenaValida,
    bool RaizConfiableIofe,
    bool PropositoValido,
    ResultadoRevocacion Revocacion,
    DateTimeOffset InstanteValidacion,
    IReadOnlyList<string> Evidencia,
    string? Error,
    MaterialValidacionLargoPlazo MaterialLargoPlazo)
{
    public bool EstadoFinal =>
        Error is null
        && CertificadoVigente
        && CadenaValida
        && RaizConfiableIofe
        && PropositoValido
        && Revocacion.Combinado == EstadoRevocacion.Good;

    public static ResultadoValidacionCertificado Fallido(string error, DateTimeOffset instante) => new(
        CertificadoVigente: false,
        CadenaValida: false,
        RaizConfiableIofe: false,
        PropositoValido: false,
        Revocacion: new ResultadoRevocacion(EstadoRevocacion.Unavailable, EstadoRevocacion.Unavailable, Array.Empty<string>()),
        InstanteValidacion: instante,
        Evidencia: Array.Empty<string>(),
        Error: error,
        MaterialLargoPlazo: MaterialValidacionLargoPlazo.Vacio);
}
