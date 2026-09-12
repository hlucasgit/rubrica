namespace SecureSign.Signature.Application.Clients;

public sealed record DatosContextualesRemoto(string? IpOrigen, string? UserAgent, string? Dispositivo);

public interface IEvidenciasServiceClient
{
    Task RegistrarAsync(Guid documentoId, string tipoEvidencia, DatosContextualesRemoto datos, Guid? firmaId = null, CancellationToken ct = default);
}
