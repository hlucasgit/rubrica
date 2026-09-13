using Microsoft.EntityFrameworkCore;
using SecureSign.Signature.Domain;

namespace SecureSign.Signature.Infrastructure.Persistence;

public sealed class SignatureDbContext(DbContextOptions<SignatureDbContext> options) : DbContext(options)
{
    public DbSet<SolicitudFirma> SolicitudesFirma => Set<SolicitudFirma>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SolicitudFirma>(b =>
        {
            b.ToTable("SolicitudesFirma");
            b.HasKey(s => s.Id);
            b.Ignore(s => s.DomainEvents);

            b.Property(s => s.TenantId).IsRequired();
            b.Property(s => s.DocumentoId).IsRequired();
            b.Property(s => s.TipoFirma).HasConversion<string>().HasMaxLength(20).IsRequired();
            b.Property(s => s.Estado).HasConversion<string>().HasMaxLength(30).IsRequired();
            b.Property(s => s.CodigoVerificacionPublico).HasMaxLength(20).IsRequired();
            b.Property(s => s.CreadoPor).IsRequired();
            b.Property(s => s.CreadoEn).IsRequired();

            b.HasIndex(s => s.CodigoVerificacionPublico).IsUnique();
            b.HasIndex(s => new { s.TenantId, s.Estado });

            // Flujos es una IReadOnlyCollection<FlujoFirma> respaldada por el
            // campo privado _flujos del aggregate — se expone así a propósito
            // para que nadie fuera del aggregate pueda mutar la colección sin
            // pasar por los métodos de SolicitudFirma (ver Entity/aggregate root).
            b.HasMany(s => s.Flujos)
                .WithOne()
                .HasForeignKey(f => f.SolicitudFirmaId)
                .OnDelete(DeleteBehavior.Cascade);

            b.Navigation(s => s.Flujos)
                .HasField("_flujos")
                .UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<FlujoFirma>(b =>
        {
            b.ToTable("FlujosFirma");
            b.HasKey(f => f.Id);
            b.Ignore(f => f.DomainEvents);

            b.Property(f => f.SolicitudFirmaId).IsRequired();
            b.Property(f => f.FirmanteUsuarioId).IsRequired();
            b.Property(f => f.OrdenFirma).IsRequired();
            b.Property(f => f.Estado).HasConversion<string>().HasMaxLength(30).IsRequired();
            b.Property(f => f.TokenAccesoUnico).HasMaxLength(100).IsRequired();
            b.Property(f => f.MotivoRechazo).HasMaxLength(500);

            // PosicionFirma es un value object opcional (null hasta que el
            // firmante la fija en el visor) — se mapea como owned type para
            // que sus 5 componentes vivan como columnas nullable propias de
            // FlujosFirma, sin una tabla aparte.
            b.OwnsOne(f => f.Posicion, p =>
            {
                p.Property(x => x.NumeroPagina).HasColumnName("PosicionNumeroPagina");
                p.Property(x => x.X).HasColumnName("PosicionX");
                p.Property(x => x.Y).HasColumnName("PosicionY");
                p.Property(x => x.Ancho).HasColumnName("PosicionAncho");
                p.Property(x => x.Alto).HasColumnName("PosicionAlto");
            });

            b.HasIndex(f => f.TokenAccesoUnico).IsUnique();
            b.HasIndex(f => new { f.FirmanteUsuarioId, f.Estado });
        });
    }
}
