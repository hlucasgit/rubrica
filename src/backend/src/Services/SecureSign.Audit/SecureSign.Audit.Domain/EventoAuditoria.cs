using SecureSign.Domain.Primitives;

namespace SecureSign.Audit.Domain;

/// <summary>
/// Tipos de evento de AUDITORÍA TÉCNICA — distintos de la evidencia de
/// negocio (SecureSign.Evidence: Carga/Visualizacion/Firma, un eslabón por
/// operación de un documento concreto). Ver informe de preauditoría
/// INDECOPI/IOFE, sección 8 ("Auditoría"): "debemos separar: evidencia de
/// negocio, auditoría técnica y registro de validación criptográfica — son
/// conceptos relacionados, pero no equivalentes". Aquí van eventos
/// TÉCNICOS/DE SEGURIDAD que no pertenecen a la cadena de evidencia de
/// ningún documento en particular (una validación PAdES de un documento
/// ajeno; un certificado rechazado por el motor de confianza al intentar
/// firmar; un ticket de firma rechazado).
/// </summary>
public enum TipoEventoAuditoria
{
    ValidacionPadesIndependiente,
    CertificadoRechazadoPorConfianza,
    TicketFirmaLocalRechazado,
}

/// <summary>
/// Un evento de auditoría técnica — a diferencia de EventoEvidencia, NO es
/// un eslabón de una cadena de hashes (esa es la innovación específica de
/// Evidence para la trazabilidad legal de un documento firmado); es
/// simplemente un registro append-only, consultable por tenant/tipo/fecha,
/// del tipo que usa cualquier sistema para responder "¿qué pasó, cuándo, y
/// con qué resultado?" sobre eventos técnicos/de seguridad.
/// </summary>
public sealed class EventoAuditoria : Entity
{
    /// <summary>
    /// null cuando el evento no tiene un tenant identificable — p. ej. una
    /// validación PAdES anónima de un documento ajeno (ver
    /// SecureSign.Validator, RUNBOOK.md 12.12: el endpoint es público a propósito).
    /// </summary>
    public Guid? TenantId { get; private set; }
    public TipoEventoAuditoria TipoEvento { get; private set; }
    public string Detalle { get; private set; } = default!;
    public string? OrigenIp { get; private set; }
    public DateTimeOffset OcurridoEn { get; private set; }

    private EventoAuditoria() { }

    public static EventoAuditoria Crear(TipoEventoAuditoria tipoEvento, string detalle, Guid? tenantId, string? origenIp) => new()
    {
        TenantId = tenantId,
        TipoEvento = tipoEvento,
        Detalle = detalle,
        OrigenIp = origenIp,
        OcurridoEn = DateTimeOffset.UtcNow,
    };
}
