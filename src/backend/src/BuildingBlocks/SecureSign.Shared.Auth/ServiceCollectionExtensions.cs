using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace SecureSign.Shared.Auth;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registra validación de JWT Bearer contra la llave simétrica compartida
    /// (ver JwtOptions). Usado por todos los servicios de dominio (Documents,
    /// Signature, Evidence, Crypto, Identity) para exigir un token válido.
    ///
    /// Acepta DOS audiencias: `Audience` (tokens emitidos por el Gateway a
    /// clientes externos) e `InternalAudience` (tokens de intercambio
    /// servicio-a-servicio, ver TokenExchangeService) — cualquier servicio
    /// puede recibir tráfico de cualquiera de los dos orígenes.
    /// </summary>
    public static IServiceCollection AddSecureSignJwtValidation(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SeccionConfiguracion));
        var opciones = configuration.GetSection(JwtOptions.SeccionConfiguracion).Get<JwtOptions>()
            ?? throw new InvalidOperationException("Falta la sección de configuración 'Jwt'.");

        if (string.IsNullOrWhiteSpace(opciones.SigningKey) || opciones.SigningKey.Length < 32)
            throw new InvalidOperationException("Jwt:SigningKey debe tener al menos 32 caracteres (256 bits) para HS256.");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = opciones.Issuer,
                    ValidateAudience = true,
                    ValidAudiences = new[] { opciones.Audience, opciones.InternalAudience },
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(opciones.SigningKey)),
                    ClockSkew = TimeSpan.FromSeconds(30)
                };

                // Trazabilidad de auditoría (ver docs/07-seguridad/modelo-seguridad.md
                // sección 5): permite distinguir en los logs si una petición vino
                // con el token de un cliente externo o con un token de
                // intercambio interno, y de qué servicio actor en ese caso.
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = ctx =>
                    {
                        var logger = ctx.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                            .CreateLogger("SecureSign.Auth");
                        var aud = ctx.Principal?.FindFirst("aud")?.Value ?? string.Join(',', ctx.Principal?.Claims.Where(c => c.Type == "aud").Select(c => c.Value) ?? []);
                        var scope = ctx.Principal?.FindFirst(ClaimsSecureSign.Scope)?.Value;
                        var actor = ctx.Principal?.FindFirst("act")?.Value;
                        logger.LogInformation(
                            "Token validado — audiencia={Audiencia} scope={Scope} actorInterno={Actor}",
                            aud, scope, actor ?? "(ninguno, token de cliente externo)");
                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorization();
        return services;
    }

    /// <summary>Registra el emisor de tokens externos (OAuth2 client_credentials). Usado exclusivamente por el Gateway.</summary>
    public static IServiceCollection AddSecureSignJwtIssuer(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SeccionConfiguracion));
        services.AddSingleton<JwtTokenService>();
        return services;
    }

    /// <summary>
    /// Registra el intercambiador de tokens (RFC 8693) usado por cualquier
    /// servicio que necesite llamar a otro servicio interno — ver
    /// TokenExchangeService y AddSecureSignInternalHttpClient.
    /// </summary>
    public static IServiceCollection AddSecureSignTokenExchange(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SeccionConfiguracion));
        services.AddSingleton<TokenExchangeService>();
        return services;
    }
}
