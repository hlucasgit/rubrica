using Microsoft.EntityFrameworkCore;
using SecureSign.Signature.Application.Clients;
using SecureSign.Signature.Application.Confianza;
using SecureSign.Signature.Application.CrearSolicitudFirma;
using SecureSign.Signature.Application.Estampado;
using SecureSign.Signature.Domain;
using SecureSign.Signature.Infrastructure;
using SecureSign.Signature.Infrastructure.Clients;
using SecureSign.Signature.Infrastructure.Confianza;
using SecureSign.Signature.Infrastructure.Estampado;
using SecureSign.Signature.Infrastructure.Persistence;
using SecureSign.Shared.Auth;
using SecureSign.Trust;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<CrearSolicitudFirmaCommand>());
builder.Services.AddSecureSignJwtValidation(builder.Configuration);
builder.Services.AddSecureSignTokenExchange(builder.Configuration);

builder.Services.AddDbContext<SignatureDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));
builder.Services.AddScoped<ISolicitudFirmaRepository, SolicitudFirmaRepositoryEfCore>();
builder.Services.AddSingleton<IGeneradorUrlFirma, GeneradorUrlFirmaWhiteLabel>();
builder.Services.AddSingleton<IEstampadorVisualDocumento, EstampadorVisualDocumentoPdf>();

// Motor de confianza IOFE (ver informe de preauditoría INDECOPI/IOFE,
// hallazgo P0-01, y RUNBOOK.md 12.9): valida vigencia, cadena, acreditación
// en la TSL de INDECOPI y revocación (OCSP/CRL) del certificado del
// firmante — nunca solo la operación criptográfica. Las raíces y la TSL se
// cargan una vez al iniciar desde ConfianzaIofe/ (ver ese .csproj); los tres
// clientes HTTP hacen llamadas de red reales a RENIEC/INDECOPI.
builder.Services.AddHttpClient<DescargadorCertificadosIntermedios>();
builder.Services.AddHttpClient<VerificadorRevocacionCrl>();
builder.Services.AddHttpClient<VerificadorRevocacionOcsp>();
builder.Services.AddSingleton(_ => AlmacenRaicesConfiables.CargarDesdeDirectorio(
    Path.Combine(AppContext.BaseDirectory, "ConfianzaIofe", "raices")));
builder.Services.AddSingleton(_ => ListaConfianzaIofe.CargarDesdeArchivo(
    Path.Combine(AppContext.BaseDirectory, "ConfianzaIofe", "tsl-pe.xml")));
builder.Services.AddScoped<ValidadorCertificados>();
builder.Services.AddScoped<IValidadorConfianzaFirmante, ValidadorConfianzaFirmanteIofe>();

// Clientes HTTP hacia los servicios de dominio de los que depende la
// orquestación de firma (ver docs/01-arquitectura/arquitectura-general.md
// sección 4). Cada uno intercambia el token del llamador original por uno
// interno de alcance mínimo antes de llamar (RFC 8693, ver TokenExchangeService)
// — ya no reenvía el token del cliente externo tal cual.
const string ActorServicio = "securesign-signature-api";
var serviciosInternos = builder.Configuration.GetSection("ServiciosInternos");
builder.Services.AddSecureSignInternalHttpClient<IDocumentosServiceClient, DocumentosServiceClient>(
    new Uri(serviciosInternos["DocumentosApiUrl"]!), ActorServicio);
builder.Services.AddSecureSignInternalHttpClient<ICriptografiaServiceClient, CriptografiaServiceClient>(
    new Uri(serviciosInternos["CriptografiaApiUrl"]!), ActorServicio);
builder.Services.AddSecureSignInternalHttpClient<IEvidenciasServiceClient, EvidenciasServiceClient>(
    new Uri(serviciosInternos["EvidenciasApiUrl"]!), ActorServicio);
builder.Services.AddSecureSignInternalHttpClient<IIdentidadServiceClient, IdentidadServiceClient>(
    new Uri(serviciosInternos["IdentidadApiUrl"]!), ActorServicio);

var app = builder.Build();

// Las migraciones ya NO se aplican aquí — ver src/Migrator (SecureSign.Migrator)
// y docker-compose.yml (servicio "securesign-signature-migrate").

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
