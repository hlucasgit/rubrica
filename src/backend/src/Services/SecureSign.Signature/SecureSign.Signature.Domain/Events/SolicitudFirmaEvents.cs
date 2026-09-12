using SecureSign.Domain.Primitives;

namespace SecureSign.Signature.Domain.Events;

public sealed record SolicitudFirmaCreadaEvent(Guid TenantId, Guid SolicitudFirmaId, Guid DocumentoId, string TipoFirma, DateTimeOffset OcurridoEn) : IDomainEvent;

public sealed record FirmanteNotificadoEvent(Guid TenantId, Guid SolicitudFirmaId, Guid FlujoFirmaId, DateTimeOffset OcurridoEn) : IDomainEvent;

public sealed record DocumentoFirmadoEvent(Guid TenantId, Guid SolicitudFirmaId, DateTimeOffset OcurridoEn) : IDomainEvent;

public sealed record FirmaRechazadaEvent(Guid TenantId, Guid SolicitudFirmaId, Guid FlujoFirmaId, string Motivo, DateTimeOffset OcurridoEn) : IDomainEvent;
