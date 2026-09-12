namespace SecureSign.Crypto.Domain;

public sealed record SelloTiempo(byte[] Token, DateTimeOffset FechaHora, string AutoridadEmisora);

/// <summary>
/// Abstracción sobre una Autoridad de Sellado de Tiempo conforme a RFC 3161.
/// La implementación real requiere contratar/integrar una TSA acreditada
/// (no incluida en este scaffold, ver docs/03-legal-normativo).
/// </summary>
public interface ISellosTiempoProvider
{
    Task<SelloTiempo> SellarAsync(byte[] hash, CancellationToken ct = default);
}
