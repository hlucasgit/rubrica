using SecureSign.Domain.Primitives;
using SecureSign.Signature.Domain.Events;

namespace SecureSign.Signature.Domain;

/// <summary>
/// Aggregate root que orquesta la máquina de estados de un proceso de firma.
/// Implementa la degradación controlada y auditable descrita en la innovación #6
/// (docs/02-innovacion-patente/analisis-innovaciones.md): cualquier transición
/// fuera del camino feliz queda registrada explícitamente, nunca es un
/// side-effect silencioso.
/// </summary>
public sealed class SolicitudFirma : Entity
{
    private readonly List<FlujoFirma> _flujos = new();

    public Guid TenantId { get; private set; }
    public Guid DocumentoId { get; private set; }
    public TipoFirma TipoFirma { get; private set; }
    public EstadoSolicitudFirma Estado { get; private set; } = EstadoSolicitudFirma.Pendiente;
    public string CodigoVerificacionPublico { get; private set; } = default!;
    public bool RequiereOrdenSecuencial { get; private set; }
    public DateTimeOffset? FechaLimite { get; private set; }
    public Guid? ClienteIntegradorId { get; private set; }
    public Guid CreadoPor { get; private set; }
    public DateTimeOffset CreadoEn { get; private set; }

    public IReadOnlyCollection<FlujoFirma> Flujos => _flujos.AsReadOnly();

    private SolicitudFirma() { }

    public static Result<SolicitudFirma> Crear(
        Guid tenantId,
        Guid documentoId,
        TipoFirma tipoFirma,
        bool requiereOrdenSecuencial,
        IReadOnlyList<(Guid usuarioId, int orden)> firmantes,
        Guid creadoPor,
        DateTimeOffset? fechaLimite = null,
        Guid? clienteIntegradorId = null)
    {
        if (firmantes.Count == 0)
            return Result.Fallido<SolicitudFirma>("Una solicitud de firma requiere al menos un firmante.");

        if (requiereOrdenSecuencial && firmantes.Select(f => f.orden).Distinct().Count() != firmantes.Count)
            return Result.Fallido<SolicitudFirma>("Los órdenes de firma deben ser únicos cuando se exige secuencia.");

        var solicitud = new SolicitudFirma
        {
            TenantId = tenantId,
            DocumentoId = documentoId,
            TipoFirma = tipoFirma,
            Estado = EstadoSolicitudFirma.Pendiente,
            CodigoVerificacionPublico = GenerarCodigoVerificacion(),
            RequiereOrdenSecuencial = requiereOrdenSecuencial,
            FechaLimite = fechaLimite,
            ClienteIntegradorId = clienteIntegradorId,
            CreadoPor = creadoPor,
            CreadoEn = DateTimeOffset.UtcNow
        };

        foreach (var (usuarioId, orden) in firmantes)
            solicitud._flujos.Add(FlujoFirma.Crear(solicitud.Id, usuarioId, orden));

        solicitud.RaiseDomainEvent(new SolicitudFirmaCreadaEvent(tenantId, solicitud.Id, documentoId, tipoFirma.ToString(), DateTimeOffset.UtcNow));
        return Result.Exitoso(solicitud);
    }

    /// <summary>
    /// Determina qué firmantes pueden ser notificados ahora mismo, respetando
    /// el orden secuencial cuando aplica.
    /// </summary>
    public IReadOnlyList<FlujoFirma> ObtenerFirmantesElegiblesParaNotificar()
    {
        if (!RequiereOrdenSecuencial)
            return _flujos.Where(f => f.Estado == EstadoFlujoFirma.Pendiente).ToList();

        var siguienteOrden = _flujos
            .Where(f => f.Estado != EstadoFlujoFirma.Firmado)
            .Select(f => f.OrdenFirma)
            .DefaultIfEmpty(int.MaxValue)
            .Min();

        return _flujos.Where(f => f.OrdenFirma == siguienteOrden && f.Estado == EstadoFlujoFirma.Pendiente).ToList();
    }

    public Result NotificarFirmante(Guid flujoFirmaId)
    {
        var flujo = _flujos.FirstOrDefault(f => f.Id == flujoFirmaId);
        if (flujo is null) return Result.Fallido("Flujo de firma no encontrado.");

        var resultado = flujo.Notificar();
        if (!resultado.EsExitoso) return resultado;

        if (Estado == EstadoSolicitudFirma.Pendiente)
            Estado = EstadoSolicitudFirma.Pendiente; // se mantiene hasta primera visualización

        RaiseDomainEvent(new FirmanteNotificadoEvent(TenantId, Id, flujo.Id, DateTimeOffset.UtcNow));
        return Result.Exitoso();
    }

    /// <summary>
    /// El firmante elige, en el visor, dónde debe verse su firma (página +
    /// coordenadas normalizadas). Debe llamarse antes de ConfirmarFirma —
    /// ver PosicionFirma y IEstampadorVisualDocumento.
    /// </summary>
    public Result EstablecerPosicionFirma(Guid flujoFirmaId, PosicionFirma posicion)
    {
        var flujo = _flujos.FirstOrDefault(f => f.Id == flujoFirmaId);
        if (flujo is null) return Result.Fallido("Flujo de firma no encontrado.");
        return flujo.EstablecerPosicion(posicion);
    }

    public Result RegistrarVisualizacion(Guid flujoFirmaId)
    {
        var flujo = _flujos.FirstOrDefault(f => f.Id == flujoFirmaId);
        if (flujo is null) return Result.Fallido("Flujo de firma no encontrado.");

        var resultado = flujo.RegistrarVisualizacion();
        if (!resultado.EsExitoso) return resultado;

        if (Estado == EstadoSolicitudFirma.Pendiente)
            Estado = EstadoSolicitudFirma.Visualizado;

        return Result.Exitoso();
    }

    /// <summary>
    /// Transición previa obligatoria a la firma: valida que el firmante puede
    /// firmar ahora (orden secuencial respetado) y mueve la solicitud a
    /// ValidandoIdentidad. El Servicio de Identidad resuelve el resultado de
    /// esa validación de forma externa antes de invocar ConfirmarFirma.
    /// </summary>
    public Result IniciarValidacionIdentidad(Guid flujoFirmaId)
    {
        var flujo = _flujos.FirstOrDefault(f => f.Id == flujoFirmaId);
        if (flujo is null) return Result.Fallido("Flujo de firma no encontrado.");

        if (RequiereOrdenSecuencial && !ObtenerFirmantesElegiblesParaNotificar().Any(f => f.Id == flujoFirmaId))
            return Result.Fallido("Este firmante no puede firmar todavía: existen firmantes previos pendientes.");

        Estado = EstadoSolicitudFirma.ValidandoIdentidad;
        return Result.Exitoso();
    }

    public Result ConfirmarFirma(Guid flujoFirmaId)
    {
        var flujo = _flujos.FirstOrDefault(f => f.Id == flujoFirmaId);
        if (flujo is null) return Result.Fallido("Flujo de firma no encontrado.");

        var resultado = flujo.MarcarFirmado();
        if (!resultado.EsExitoso) return resultado;

        Estado = _flujos.All(f => f.Estado == EstadoFlujoFirma.Firmado)
            ? EstadoSolicitudFirma.Firmado
            : EstadoSolicitudFirma.Pendiente;

        if (Estado == EstadoSolicitudFirma.Firmado)
            RaiseDomainEvent(new DocumentoFirmadoEvent(TenantId, Id, DateTimeOffset.UtcNow));

        return Result.Exitoso();
    }

    public Result RechazarFirma(Guid flujoFirmaId, string motivo)
    {
        var flujo = _flujos.FirstOrDefault(f => f.Id == flujoFirmaId);
        if (flujo is null) return Result.Fallido("Flujo de firma no encontrado.");

        var resultado = flujo.Rechazar(motivo);
        if (!resultado.EsExitoso) return resultado;

        Estado = EstadoSolicitudFirma.Rechazado;
        RaiseDomainEvent(new FirmaRechazadaEvent(TenantId, Id, flujo.Id, motivo, DateTimeOffset.UtcNow));
        return Result.Exitoso();
    }

    public Result Cancelar()
    {
        if (Estado == EstadoSolicitudFirma.Firmado)
            return Result.Fallido("No se puede cancelar una solicitud ya firmada.");
        Estado = EstadoSolicitudFirma.Cancelado;
        return Result.Exitoso();
    }

    public void ExpirarSiVencida()
    {
        if (FechaLimite.HasValue && DateTimeOffset.UtcNow > FechaLimite.Value && Estado is not (EstadoSolicitudFirma.Firmado or EstadoSolicitudFirma.Cancelado))
            Estado = EstadoSolicitudFirma.Expirado;
    }

    private static string GenerarCodigoVerificacion()
    {
        const string alfabeto = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // sin caracteres ambiguos (0/O, 1/I)
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(10);
        return new string(bytes.Select(b => alfabeto[b % alfabeto.Length]).ToArray());
    }
}
