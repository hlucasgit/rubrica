using Microsoft.EntityFrameworkCore;
using SecureSign.Evidence.Application.RegistrarEvidencia;
using SecureSign.Evidence.Domain;
using SecureSign.Evidence.Infrastructure.Persistence;
using SecureSign.Shared.Auth;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<RegistrarEvidenciaCommand>());
builder.Services.AddSecureSignJwtValidation(builder.Configuration);

builder.Services.AddDbContext<EvidenceDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));
builder.Services.AddScoped<IEventoEvidenciaRepository, EventoEvidenciaRepositoryEfCore>();

var app = builder.Build();

// Las migraciones ya NO se aplican aquí — ver src/Migrator (SecureSign.Migrator)
// y docker-compose.yml (servicio "securesign-evidence-migrate").

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
