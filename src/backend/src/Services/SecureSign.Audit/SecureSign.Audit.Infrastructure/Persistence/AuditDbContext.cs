using Microsoft.EntityFrameworkCore;
using SecureSign.Audit.Domain;

namespace SecureSign.Audit.Infrastructure.Persistence;

public sealed class AuditDbContext(DbContextOptions<AuditDbContext> options) : DbContext(options)
{
    public DbSet<EventoAuditoria> Eventos => Set<EventoAuditoria>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EventoAuditoria>(b =>
        {
            b.ToTable("EventosAuditoria");
            b.HasKey(e => e.Id);
            b.Ignore(e => e.DomainEvents);

            b.Property(e => e.TipoEvento).HasConversion<string>().HasMaxLength(50).IsRequired();
            b.Property(e => e.Detalle).IsRequired();
            b.Property(e => e.OrigenIp).HasMaxLength(64);
            b.Property(e => e.OcurridoEn).IsRequired();

            b.HasIndex(e => new { e.TenantId, e.OcurridoEn });
            b.HasIndex(e => e.TipoEvento);
        });
    }
}
