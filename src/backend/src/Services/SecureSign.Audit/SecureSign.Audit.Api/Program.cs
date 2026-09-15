using Microsoft.EntityFrameworkCore;
using SecureSign.Audit.Application.RegistrarEvento;
using SecureSign.Audit.Domain;
using SecureSign.Audit.Infrastructure.Persistence;
using SecureSign.Shared.Auth;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<RegistrarEventoAuditoriaCommand>());
builder.Services.AddSecureSignJwtValidation(builder.Configuration);

builder.Services.AddDbContext<AuditDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));
builder.Services.AddScoped<IEventoAuditoriaRepository, EventoAuditoriaRepositoryEfCore>();

var app = builder.Build();

// Las migraciones ya NO se aplican aquí — ver src/Migrator (SecureSign.Migrator)
// y docker-compose.yml (servicio "securesign-audit-migrate").

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
