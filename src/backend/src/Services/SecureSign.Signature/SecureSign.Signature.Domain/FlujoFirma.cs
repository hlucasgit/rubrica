using SecureSign.Domain.Primitives;

namespace SecureSign.Signature.Domain;

/// <summary>
/// Representa a un firmante dentro de una SolicitudFirma. El orden importa
/// cuando SolicitudFirma.RequiereOrdenSecuencial es verdadero.
/// </summary>
public sealed class FlujoFirma : Entity
{
    public Guid SolicitudFirmaId { get; private set; }
    public Guid FirmanteUsuarioId { get; private set; }
    public int OrdenFirma { get; private set; }
    public EstadoFlujoFirma Estado { get; private set; } = EstadoFlujoFirma.Pendiente;
    public string TokenAccesoUnico { get; private set; } = default!;
    public DateTimeOffset? NotificadoEn { get; private set; }
    public DateTimeOffset? VisualizadoEn { get; private set; }
    public DateTimeOffset? RechazadoEn { get; private set; }
    public string? MotivoRechazo { get; private set; }

    private FlujoFirma() { }

    internal static FlujoFirma Crear(Guid solicitudFirmaId, Guid firmanteUsuarioId, int ordenFirma) => new()
    {
        SolicitudFirmaId = solicitudFirmaId,
        FirmanteUsuarioId = firmanteUsuarioId,
        OrdenFirma = ordenFirma,
        TokenAccesoUnico = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)).ToLowerInvariant()
    };

    public Result Notificar()
    {
        if (Estado != EstadoFlujoFirma.Pendiente)
            return Result.Fallido($"No se puede notificar un flujo en estado {Estado}.");
        Estado = EstadoFlujoFirma.Notificado;
        NotificadoEn = DateTimeOffset.UtcNow;
        return Result.Exitoso();
    }

    public Result RegistrarVisualizacion()
    {
        if (Estado is EstadoFlujoFirma.Firmado or EstadoFlujoFirma.Rechazado)
            return Result.Fallido($"No se puede visualizar un flujo en estado {Estado}.");
        Estado = EstadoFlujoFirma.Visualizado;
        VisualizadoEn ??= DateTimeOffset.UtcNow;
        return Result.Exitoso();
    }

    public Result MarcarFirmado()
    {
        if (Estado != EstadoFlujoFirma.Visualizado)
            return Result.Fallido("El firmante debe visualizar el documento antes de firmar.");
        Estado = EstadoFlujoFirma.Firmado;
        return Result.Exitoso();
    }

    public Result Rechazar(string motivo)
    {
        if (Estado == EstadoFlujoFirma.Firmado)
            return Result.Fallido("No se puede rechazar un flujo ya firmado.");
        Estado = EstadoFlujoFirma.Rechazado;
        RechazadoEn = DateTimeOffset.UtcNow;
        MotivoRechazo = motivo;
        return Result.Exitoso();
    }
}
