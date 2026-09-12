namespace SecureSign.Documents.Application.Clients;

public sealed record DatosContextualesRemoto(string? IpOrigen, string? UserAgent, string? Dispositivo);

/// <summary>
/// Contrato hacia el Servicio de Evidencia. Cada servicio de dominio define
/// su propio contrato mínimo hacia Evidencia (en vez de compartir una
/// librería de cliente) para mantener independencia de despliegue entre
/// microservicios — ver docs/01-arquitectura/arquitectura-general.md.
/// </summary>
public interface IEvidenciasServiceClient
{
    Task RegistrarAsync(Guid documentoId, string tipoEvidencia, DatosContextualesRemoto datos, CancellationToken ct = default);
}
