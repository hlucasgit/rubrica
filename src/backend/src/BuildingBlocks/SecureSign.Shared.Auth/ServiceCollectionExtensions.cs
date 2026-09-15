using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace SecureSign.Shared.Auth;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registra validación de JWT Bearer contra la llave PÚBLICA del Gateway
    /// — obtenida de su JWKS (ver <see cref="JwksLlaveResolver"/> y
    /// OidcController en el Gateway) a través de un
    /// <c>IssuerSigningKeyResolver</c> propio, NO del descubrimiento OIDC
    /// automático de <c>JwtBearerOptions.Authority</c> (se abandonó: fallaba
    /// con "no security keys were provided" sin diagnóstico aprovechable —
    /// ver RUNBOOK.md 12.21). Desde ahí, ningún servicio vuelve a tener
    /// material de firma en su configuración — estructuralmente ya no puede
    /// forjar un token de otro servicio, no es una promesa, es una
    /// ausencia de la llave.
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

        if (string.IsNullOrWhiteSpace(opciones.Authority))
            throw new InvalidOperationException("Jwt:Authority debe apuntar a la URL base del Gateway (p. ej. http://gateway:8080).");

        services.AddHttpClient<JwksLlaveResolver>(client => client.BaseAddress = new Uri(opciones.Authority));

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
                    },
                    OnAuthenticationFailed = ctx =>
                    {
                        var logger = ctx.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                            .CreateLogger("SecureSign.Auth");
                        logger.LogWarning(ctx.Exception, "Token rechazado al validar contra {Authority}", opciones.Authority);
                        return Task.CompletedTask;
                    }
                };
            });

        // IssuerSigningKeyResolver necesita un JwksLlaveResolver resuelto por
        // DI — se conecta aparte (no dentro de AddJwtBearer, que no da acceso
        // al contenedor) vía el overload de Configure<TDep> de
        // Microsoft.Extensions.Options, pensado exactamente para esto.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<JwksLlaveResolver>((options, resolver) =>
            {
                options.TokenValidationParameters.IssuerSigningKeyResolver = (_, _, kid, _) =>
                    resolver.ObtenerLlavesAsync().GetAwaiter().GetResult().Where(k => k.KeyId == kid);
            });

        services.AddAuthorization();
        return services;
    }

    /// <summary>
    /// Registra el emisor de tokens (OAuth2 client_credentials y, vía
    /// AuthController, el endpoint interno de firma) y su llavero RSA — usado
    /// exclusivamente por el Gateway, el único componente que llega a tocar
    /// una llave privada (ver RsaKeyStore, RUNBOOK.md 12.21).
    /// </summary>
    public static IServiceCollection AddSecureSignJwtIssuer(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SeccionConfiguracion));
        services.AddSingleton<RsaKeyStore>();
        services.AddSingleton<JwtTokenService>();
        return services;
    }

    /// <summary>
    /// Registra el intercambiador de tokens (RFC 8693) usado por cualquier
    /// servicio que necesite llamar a otro servicio interno — ver
    /// TokenExchangeService y AddSecureSignInternalHttpClient. Desde
    /// RUNBOOK.md 12.21, esto es una llamada HTTP real al Gateway (ver
    /// EmisorTokenInterno), no una firma local — el HttpClient registrado
    /// aquí lleva el secreto compartido (Jwt:SecretoClienteInterno) que
    /// autentica esa llamada.
    /// </summary>
    public static IServiceCollection AddSecureSignTokenExchange(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SeccionConfiguracion));
        var opciones = configuration.GetSection(JwtOptions.SeccionConfiguracion).Get<JwtOptions>()
            ?? throw new InvalidOperationException("Falta la sección de configuración 'Jwt'.");

        if (string.IsNullOrWhiteSpace(opciones.Authority) || string.IsNullOrWhiteSpace(opciones.SecretoClienteInterno))
            throw new InvalidOperationException("Jwt:Authority y Jwt:SecretoClienteInterno son obligatorios para pedir tokens internos al Gateway.");

        services.AddHttpClient<EmisorTokenInterno>(client =>
        {
            client.BaseAddress = new Uri(opciones.Authority);
            client.DefaultRequestHeaders.Add("X-Internal-Client-Secret", opciones.SecretoClienteInterno);
        });
        services.AddSingleton<TokenExchangeService>();
        return services;
    }
}
