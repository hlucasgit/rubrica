using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SecureSign.Evidence.Domain;

namespace SecureSign.Evidence.Infrastructure.Persistence;

public sealed class EvidenceDbContext(DbContextOptions<EvidenceDbContext> options) : DbContext(options)
{
    public DbSet<EventoEvidencia> Eventos => Set<EventoEvidencia>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EventoEvidencia>(b =>
        {
            b.ToTable("Evidencias");
            b.HasKey(e => e.Id);
            b.Ignore(e => e.DomainEvents);

            b.Property(e => e.TenantId).IsRequired();
            b.Property(e => e.DocumentoId).IsRequired();
            b.Property(e => e.TipoEvidencia).HasConversion<string>().HasMaxLength(50).IsRequired();
            b.Property(e => e.HashEventoAnterior).HasMaxLength(64);
            b.Property(e => e.HashEvento).HasMaxLength(64).IsRequired();
            b.Property(e => e.RegistradoEn).IsRequired();

            // DatosContextuales se persiste íntegro como JSON: es un value
            // object de solo lectura (IP, user agent, dispositivo,
            // geolocalización opcional) sin necesidad de columnas propias
            // ni de consultarse por sus campos internos.
            b.Property(e => e.DatosContextuales)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<DatosContextuales>(v, (JsonSerializerOptions?)null)!)
                .HasColumnType("jsonb")
                .IsRequired();

            // Orden cronológico por tenant: es el eje sobre el que se
            // reconstruye y verifica la cadena de hashes (ver
            // VerificadorCadenaEvidencia y docs/02-innovacion-patente).
            b.HasIndex(e => new { e.TenantId, e.RegistradoEn });
            b.HasIndex(e => e.DocumentoId);
        });
    }
}
