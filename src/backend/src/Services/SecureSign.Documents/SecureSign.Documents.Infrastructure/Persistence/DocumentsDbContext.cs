using Microsoft.EntityFrameworkCore;
using SecureSign.Documents.Domain;
using SecureSign.Domain.ValueObjects;

namespace SecureSign.Documents.Infrastructure.Persistence;

public sealed class DocumentsDbContext(DbContextOptions<DocumentsDbContext> options) : DbContext(options)
{
    public DbSet<Documento> Documentos => Set<Documento>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Documento>(b =>
        {
            b.ToTable("Documentos");
            b.HasKey(d => d.Id);
            b.Ignore(d => d.DomainEvents);

            b.Property(d => d.TenantId).IsRequired();
            b.Property(d => d.CodigoExterno).HasMaxLength(100);
            b.Property(d => d.NombreArchivo).HasMaxLength(300).IsRequired();
            b.Property(d => d.TipoContenido).HasMaxLength(100).IsRequired();
            b.Property(d => d.UrlAlmacenamiento).HasMaxLength(500).IsRequired();

            // HashDocumental es un value object inmutable (ver
            // SecureSign.Domain.ValueObjects.HashDocumental) — se persiste
            // como su representación hexadecimal; el algoritmo es constante
            // (SHA-256) por ahora y no requiere columna propia.
            b.Property(d => d.Hash)
                .HasConversion(h => h.ValorHex, v => HashDocumental.DesdeValorConocido(v))
                .HasColumnName("HashSha256")
                .HasMaxLength(64)
                .IsRequired();

            b.Property(d => d.Estado).HasConversion<string>().HasMaxLength(30).IsRequired();
            b.Property(d => d.CreadoPor).IsRequired();
            b.Property(d => d.CreadoEn).IsRequired();

            b.HasIndex(d => new { d.TenantId, d.Estado });
            b.HasIndex(d => d.CodigoExterno);
        });
    }
}
