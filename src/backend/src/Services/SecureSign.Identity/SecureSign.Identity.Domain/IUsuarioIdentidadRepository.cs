namespace SecureSign.Identity.Domain;

public interface IUsuarioIdentidadRepository
{
    Task<UsuarioIdentidad?> ObtenerAsync(Guid tenantId, Guid usuarioId, CancellationToken ct = default);
    Task AgregarAsync(UsuarioIdentidad usuario, CancellationToken ct = default);
    Task ActualizarAsync(UsuarioIdentidad usuario, CancellationToken ct = default);
}
