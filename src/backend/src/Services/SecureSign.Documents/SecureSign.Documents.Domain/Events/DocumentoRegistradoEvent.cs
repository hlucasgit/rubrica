using SecureSign.Domain.Primitives;

namespace SecureSign.Documents.Domain.Events;

public sealed record DocumentoRegistradoEvent(
    Guid TenantId,
    Guid DocumentoId,
    string HashSHA256,
    DateTimeOffset OcurridoEn) : IDomainEvent;
