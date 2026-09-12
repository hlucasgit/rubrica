using SecureSign.Crypto.Domain;
using SecureSign.Crypto.Infrastructure;
using SecureSign.Shared.Auth;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSecureSignJwtValidation(builder.Configuration);

// ADVERTENCIA: proveedor de software solo para desarrollo. Reemplazar por
// adaptador HSM/KMS antes de cualquier despliegue real (ver clase para detalle).
builder.Services.AddSingleton<IProveedorCriptografico, ProveedorCriptograficoSoftware>();

var app = builder.Build();

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
