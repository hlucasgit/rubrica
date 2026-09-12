namespace SecureSign.Signature.Application.Clients;

public sealed record IndiceConfianzaRemoto(int Indice);

public interface IIdentidadServiceClient
{
    Task<IndiceConfianzaRemoto> ObtenerIndiceConfianzaAsync(Guid usuarioId, CancellationToken ct = default);
}
