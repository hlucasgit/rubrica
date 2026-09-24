using Microsoft.EntityFrameworkCore;
using SecureSign.Documents.Application.Clients;
using SecureSign.Documents.Application.RegistrarDocumento;
using SecureSign.Documents.Domain;
using SecureSign.Documents.Infrastructure.Clients;
using SecureSign.Documents.Infrastructure.Persistence;
using SecureSign.Documents.Infrastructure.Storage;
using SecureSign.Shared.Auth;

var builder = WebApplication.CreateBuilder(args);

// Secretos entregados como archivos (Docker/Kubernetes secrets) — precedencia máxima, ver RUNBOOK.md 12.30.
builder.Configuration.AddSecureSignSecretosDeArchivo();

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<RegistrarDocumentoCommand>());
builder.Services.AddSecureSignJwtValidation(builder.Configuration);
builder.Services.AddSecureSignTokenExchange(builder.Configuration);

builder.Services.AddDbContext<DocumentsDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));
builder.Services.AddScoped<IDocumentoRepository, DocumentoRepositoryEfCore>();

// NOTA: almacenamiento en disco local del proceso — reemplazar por
// almacenamiento S3-compatible con cifrado (ver docs/07-seguridad) antes de
// cualquier despliegue real.
builder.Services.AddSingleton<IAlmacenamientoDocumental>(
    new AlmacenamientoDocumentalLocal(Path.Combine(Path.GetTempPath(), "securesign-documentos")));

// Intercambia el token del llamador original por uno interno antes de
// llamar a Evidencia (RFC 8693, ver TokenExchangeService).
builder.Services.AddSecureSignInternalHttpClient<IEvidenciasServiceClient, EvidenciasServiceClient>(
    new Uri(builder.Configuration["ServiciosInternos:EvidenciasApiUrl"]!), "securesign-documents-api");

var app = builder.Build();

// Las migraciones ya NO se aplican aquí — ver src/Migrator (SecureSign.Migrator)
// y docker-compose.yml (servicio "securesign-documents-migrate"). Un servicio
// que arranca sin que su esquema esté al día fallará rápido y explícitamente
// al primer intento de acceso a datos, en vez de migrar silenciosamente.

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
