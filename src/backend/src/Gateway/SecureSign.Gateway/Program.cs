using SecureSign.Gateway;
using SecureSign.Shared.Auth;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSecureSignJwtIssuer(builder.Configuration);
builder.Services.AddSecureSignJwtValidation(builder.Configuration);
builder.Services.Configure<ClientesDemoOptions>(builder.Configuration.GetSection(ClientesDemoOptions.SeccionConfiguracion));

// Cada ruta proxied declara explícitamente su política en appsettings.json
// (ReverseProxy:Routes:*:AuthorizationPolicy). Esto reemplaza — no se suma a
// — cualquier política por defecto, así que NO se debe encadenar además un
// .RequireAuthorization() global sobre MapReverseProxy(): si se hace, un
// endpoint con la política pública quedaría igual bloqueado, porque ambas
// políticas se exigirían simultáneamente en vez de que la de la ruta
// reemplace a la global.
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("requerir-token", p => p.RequireAuthenticatedUser());
    // Portal de verificación pública (verificar.securesign.pe) — ver
    // docs/06-white-label y manual de integración 5.5.
    options.AddPolicy("publico-sin-auth", p => p.RequireAssertion(_ => true));
});

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

// La autorización de cada ruta la decide su propia AuthorizationPolicy en
// appsettings.json — ver comentario más arriba sobre por qué NO se encadena
// .RequireAuthorization() aquí.
app.MapReverseProxy();

app.Run();
