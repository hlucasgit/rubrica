using SecureSign.Signature.Domain;

namespace SecureSign.Identity.Domain;

public enum MetodoValidacionIdentidad
{
    OtpEmail,
    OtpSms,
    BiometriaFacial,
    CertificadoDigital,
    Manual
}

public sealed record SenalValidacion(MetodoValidacionIdentidad Metodo, bool Exitoso, DateTimeOffset OcurridoEn);

/// <summary>
/// Innovación #5 (ver docs/02-innovacion-patente/analisis-innovaciones.md):
/// índice 0-100 recalculado dinámicamente que determina qué clase de firma
/// (Simple/Avanzada/Digital) puede ejecutar un usuario para una operación
/// específica. No es un valor cacheado estático: se evalúa en el momento de
/// cada solicitud de firma, permitiendo negar una operación de alto riesgo
/// aunque el usuario tenga un índice históricamente alto si las señales
/// recientes lo justifican (p. ej., certificado revocado).
/// </summary>
public static class IndiceConfianzaDigital
{
    public const int UsuarioNuevo = 30;
    public const int UsuarioValidado = 70;
    public const int UsuarioConCertificado = 95;
    public const int UsuarioInstitucional = 99;

    public static int Calcular(
        IReadOnlyList<SenalValidacion> historialValidaciones,
        bool tieneCertificadoVigente,
        bool esCuentaInstitucional,
        int rechazosPorSospechaFraude)
    {
        if (esCuentaInstitucional) return UsuarioInstitucional;
        if (tieneCertificadoVigente) return UsuarioConCertificado;

        var tieneValidacionExitosa = historialValidaciones.Any(s => s.Exitoso);
        var score = tieneValidacionExitosa ? UsuarioValidado : UsuarioNuevo;

        // Penalización por historial de fraude: reduce el índice incluso si
        // hubo validaciones exitosas previas — el riesgo no es estático.
        score -= Math.Min(rechazosPorSospechaFraude * 15, score);

        return Math.Clamp(score, 0, 100);
    }

    /// <summary>
    /// Política de mapeo índice -> tipo de firma habilitado. Configurable por
    /// tenant en producción (ver docs/06-white-label); estos son los umbrales
    /// por defecto de la plataforma.
    /// </summary>
    public static bool PuedeEjecutar(int indiceConfianza, TipoFirma tipoFirmaSolicitado) => tipoFirmaSolicitado switch
    {
        TipoFirma.Simple => indiceConfianza >= 0,
        TipoFirma.Avanzada => indiceConfianza >= UsuarioValidado,
        TipoFirma.Digital => indiceConfianza >= UsuarioConCertificado,
        _ => false
    };
}
