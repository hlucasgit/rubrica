using SecureSign.Domain.Primitives;

namespace SecureSign.Identity.Domain;

/// <summary>
/// Perfil de confianza persistente de un usuario, por tenant. Es el aggregate
/// que respalda el Índice de Confianza Digital dinámico (innovación #5, ver
/// docs/02-innovacion-patente/analisis-innovaciones.md): cada señal que
/// llega (validación exitosa, emisión de certificado, marca institucional,
/// rechazo por sospecha de fraude) se registra aquí, y el índice se
/// recalcula a partir del estado acumulado — nunca se cachea como un valor
/// estático independiente del historial.
///
/// SIMPLIFICACIÓN: en vez de guardar cada señal de validación individual
/// (que requeriría una tabla hija y no cambia el resultado de
/// IndiceConfianzaDigital.Calcular, que solo mira si HUBO alguna exitosa),
/// se guarda un indicador agregado `TieneValidacionExitosa`. El detalle de
/// cada señal (para auditoría) debería vivir en el Servicio de Evidencia,
/// no duplicarse aquí.
/// </summary>
public sealed class UsuarioIdentidad : Entity
{
    public Guid TenantId { get; private set; }
    public Guid UsuarioId { get; private set; }
    public bool TieneValidacionExitosa { get; private set; }
    public bool TieneCertificadoVigente { get; private set; }
    public bool EsCuentaInstitucional { get; private set; }
    public int RechazosPorSospechaFraude { get; private set; }
    public DateTimeOffset CreadoEn { get; private set; }
    public DateTimeOffset ActualizadoEn { get; private set; }

    private UsuarioIdentidad() { }

    public static UsuarioIdentidad Registrar(Guid tenantId, Guid usuarioId)
    {
        var ahora = DateTimeOffset.UtcNow;
        return new UsuarioIdentidad
        {
            TenantId = tenantId,
            UsuarioId = usuarioId,
            CreadoEn = ahora,
            ActualizadoEn = ahora
        };
    }

    public void RegistrarValidacionExitosa()
    {
        TieneValidacionExitosa = true;
        ActualizadoEn = DateTimeOffset.UtcNow;
    }

    public void MarcarCertificadoVigente()
    {
        TieneCertificadoVigente = true;
        ActualizadoEn = DateTimeOffset.UtcNow;
    }

    public void RevocarCertificado()
    {
        TieneCertificadoVigente = false;
        ActualizadoEn = DateTimeOffset.UtcNow;
    }

    public void MarcarCuentaInstitucional()
    {
        EsCuentaInstitucional = true;
        ActualizadoEn = DateTimeOffset.UtcNow;
    }

    public void RegistrarRechazoPorSospechaFraude()
    {
        RechazosPorSospechaFraude++;
        ActualizadoEn = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Recalcula el índice a partir del estado acumulado — ver
    /// IndiceConfianzaDigital para la fórmula completa.
    /// </summary>
    public int CalcularIndiceConfianza()
    {
        var historial = TieneValidacionExitosa
            ? new[] { new SenalValidacion(MetodoValidacionIdentidad.Manual, true, ActualizadoEn) }
            : Array.Empty<SenalValidacion>();

        return IndiceConfianzaDigital.Calcular(historial, TieneCertificadoVigente, EsCuentaInstitucional, RechazosPorSospechaFraude);
    }
}
