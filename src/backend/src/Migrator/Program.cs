// =============================================================================
// SecureSign.Migrator — aplica las migraciones de EF Core de un servicio
// como paso explícito y aislado, en vez de que cada Api las aplique sola al
// arrancar (Database.Migrate() en Program.cs, como se hacía antes).
//
// Por qué: si dos réplicas del mismo servicio arrancan a la vez, ambas
// intentarían migrar la base de datos simultáneamente (EF Core lo tolera
// razonablemente bien con locking, pero no es el comportamiento deseado en
// producción); y una migración fallida no debería manifestarse como un
// contenedor de aplicación crasheando en bucle, sino como un paso de
// pipeline claramente marcado en rojo, antes de intentar desplegar código
// nuevo contra un esquema con el que no es compatible.
//
// Uso:
//   dotnet SecureSign.Migrator.dll <documents|signature|evidence|identity|audit>
//   (requiere la variable de entorno ConnectionStrings__Postgres)
//
// Ver docker-compose.yml (servicios "*-migrate") y
// .github/workflows/ci.yml para cómo se invoca en la práctica.
// =============================================================================

using Microsoft.EntityFrameworkCore;
using SecureSign.Audit.Infrastructure.Persistence;
using SecureSign.Documents.Infrastructure.Persistence;
using SecureSign.Evidence.Infrastructure.Persistence;
using SecureSign.Identity.Infrastructure.Persistence;
using SecureSign.Signature.Infrastructure.Persistence;

if (args.Length != 1)
{
    Console.Error.WriteLine("Uso: dotnet SecureSign.Migrator.dll <documents|signature|evidence|identity|audit>");
    return 1;
}

var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
    ?? Environment.GetEnvironmentVariable("ConnectionStrings:Postgres");

if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("Falta la variable de entorno ConnectionStrings__Postgres.");
    return 1;
}

try
{
    switch (args[0].ToLowerInvariant())
    {
        case "documents":
            await MigrarAsync(new DocumentsDbContext(Opciones<DocumentsDbContext>(connectionString)));
            break;
        case "signature":
            await MigrarAsync(new SignatureDbContext(Opciones<SignatureDbContext>(connectionString)));
            break;
        case "evidence":
            await MigrarAsync(new EvidenceDbContext(Opciones<EvidenceDbContext>(connectionString)));
            break;
        case "identity":
            await MigrarAsync(new IdentityDbContext(Opciones<IdentityDbContext>(connectionString)));
            break;
        case "audit":
            await MigrarAsync(new AuditDbContext(Opciones<AuditDbContext>(connectionString)));
            break;
        default:
            Console.Error.WriteLine($"Servicio desconocido: '{args[0]}'. Usar documents|signature|evidence|identity|audit.");
            return 1;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"ERROR aplicando migraciones de '{args[0]}': {ex.Message}");
    return 1;
}

return 0;

static DbContextOptions<TContext> Opciones<TContext>(string connectionString) where TContext : DbContext
    => new DbContextOptionsBuilder<TContext>().UseNpgsql(connectionString).Options;

static async Task MigrarAsync(DbContext contexto)
{
    await using (contexto)
    {
        var pendientes = (await contexto.Database.GetPendingMigrationsAsync()).ToList();
        if (pendientes.Count == 0)
        {
            Console.WriteLine($"{contexto.GetType().Name}: sin migraciones pendientes.");
            return;
        }

        Console.WriteLine($"{contexto.GetType().Name}: aplicando {pendientes.Count} migración(es): {string.Join(", ", pendientes)}");
        await contexto.Database.MigrateAsync();
        Console.WriteLine($"{contexto.GetType().Name}: migraciones aplicadas correctamente.");
    }
}
