using Microsoft.EntityFrameworkCore;
using SecureSign.Signature.Application.Clients;
using SecureSign.Signature.Application.Confianza;
using SecureSign.Signature.Application.CrearSolicitudFirma;
using SecureSign.Signature.Application.Estampado;
using SecureSign.Signature.Domain;
using SecureSign.Signature.Infrastructure;
using SecureSign.Signature.Infrastructure.Clients;
using SecureSign.Signature.Application.TicketFirmaLocal;
using SecureSign.Signature.Application.ValidarPades;
using SecureSign.Signature.Infrastructure.Confianza;
using SecureSign.Signature.Infrastructure.Estampado;
using SecureSign.Signature.Infrastructure.Persistence;
using SecureSign.Signature.Infrastructure.TicketFirmaLocal;
using SecureSign.Signature.Infrastructure.ValidarPades;
using SecureSign.Shared.Auth;
using SecureSign.Trust;
using SecureSign.Tsa;
using Microsoft.Extensions.Options;

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

// PAdES-LTA (RUNBOOK.md 12.24): FirmarLocalHandler pide un sello de tiempo
// RFC 3161 sobre el documento ya con firma+DSS — mismo cliente puro que usa
// el Firmador Local para PAdES-T (ver RUNBOOK.md 12.14), aquí del lado
// servidor. Se expone OpcionesTsa como instancia POCO simple (no IOptions<T>)
// para que SecureSign.Signature.Application no necesite depender de
// Microsoft.Extensions.Options en su propio .csproj.
builder.Services.Configure<OpcionesTsa>(builder.Configuration.GetSection(OpcionesTsa.SeccionConfiguracion));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<OpcionesTsa>>().Value);
builder.Services.AddHttpClient<ClienteTsaRfc3161>();

// Validador PAdES independiente (ver informe de preauditoría INDECOPI/IOFE,
// hallazgo P0-05, y RUNBOOK.md 12.12): compone PdfSignatureVerifier +
// ValidadorCertificados SIN pasar por PdfSignaturePlaceholder (el
// generador) — cualquier PDF con PAdES/CMS estándar, de SecureSign o de un
// tercero, se valida exactamente igual.
builder.Services.AddScoped<SecureSign.Validator.ValidadorDocumentoPades>();
builder.Services.AddScoped<IValidadorDocumentoPadesIndependiente, ValidadorDocumentoPadesIndependiente>();

// Ticket de firma de un solo uso para el Firmador Local (ver informe de
// preauditoría INDECOPI/IOFE, sección 12, y RUNBOOK.md 12.13): reutiliza
// deliberadamente la misma llave HS256 de JwtOptions — es seguro porque el
// Firmador Local nunca verifica este JWT, solo lo reenvía como credencial
// Bearer hacia este mismo backend, que sí lo valida (firma, expiración) con
// el pipeline de JWT ya existente, y además liga sus claims a la operación
// exacta que se está completando (ver FirmarLocalHandler).
builder.Services.AddSingleton<EmisorTicketFirmaLocal>();
builder.Services.AddScoped<IEmisorTicketFirmaLocal, EmisorTicketFirmaLocalAdaptador>();

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
builder.Services.AddSecureSignInternalHttpClient<IAuditoriaServiceClient, AuditoriaServiceClient>(
    new Uri(serviciosInternos["AuditoriaApiUrl"]!), ActorServicio);

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
