namespace SecureSign.Domain.Primitives;

/// <summary>
/// Marca un evento de dominio. Los eventos publicados en el bus (Kafka/Service Bus)
/// alimentan al Servicio de Auditoría y al Servicio de Evidencia (ver docs/01-arquitectura).
/// </summary>
public interface IDomainEvent
{
    Guid TenantId { get; }
    DateTimeOffset OcurridoEn { get; }
}
