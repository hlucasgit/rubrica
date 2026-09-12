using Microsoft.EntityFrameworkCore;
using SecureSign.Identity.Domain;

namespace SecureSign.Identity.Infrastructure.Persistence;

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public DbSet<UsuarioIdentidad> UsuariosIdentidad => Set<UsuarioIdentidad>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UsuarioIdentidad>(b =>
        {
            b.ToTable("UsuariosIdentidad");
            b.HasKey(u => u.Id);
            b.Ignore(u => u.DomainEvents);

            b.Property(u => u.TenantId).IsRequired();
            b.Property(u => u.UsuarioId).IsRequired();
            b.Property(u => u.CreadoEn).IsRequired();
            b.Property(u => u.ActualizadoEn).IsRequired();

            b.HasIndex(u => new { u.TenantId, u.UsuarioId }).IsUnique();
        });
    }
}
