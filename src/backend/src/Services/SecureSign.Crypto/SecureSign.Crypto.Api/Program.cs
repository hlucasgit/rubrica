using SecureSign.Crypto.Domain;
using SecureSign.Crypto.Infrastructure;
using SecureSign.Crypto.Infrastructure.Pkcs11;
using SecureSign.Shared.Auth;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSecureSignJwtValidation(builder.Configuration);

// Selección de proveedor criptográfico por configuración:
// - "Software" (default): llaves ECDSA en memoria — SOLO desarrollo.
// - "Pkcs11": token real (probado con DNIe peruano vía middleware IDEMIA).
//   Requiere correr este servicio NATIVO en Windows (no en Docker/Linux) con
//   el lector y la tarjeta conectados a ESTA máquina — ver
//   docs/07-seguridad/modelo-seguridad.md y src/backend/RUNBOOK.md.
var proveedorCripto = builder.Configuration["CriptoProveedor"] ?? "Software";
if (proveedorCripto.Equals("Pkcs11", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.Configure<Pkcs11Options>(builder.Configuration.GetSection(Pkcs11Options.SeccionConfiguracion));
    builder.Services.AddSingleton<IProveedorCriptografico, ProveedorCriptograficoPkcs11>();
}
else
{
    // ADVERTENCIA: proveedor de software solo para desarrollo. Ver clase para detalle.
    builder.Services.AddSingleton<IProveedorCriptografico, ProveedorCriptograficoSoftware>();
}

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
