using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace SecureSign.Gateway;

/// <summary>
/// Límites por minuto de los endpoints de credenciales del Gateway
/// (sección <c>LimitacionDeTasa</c> de appsettings.json).
/// </summary>
public sealed class OpcionesLimitacionDeTasa
{
    public const string SeccionConfiguracion = "LimitacionDeTasa";

    /// <summary>
    /// <c>POST /api/auth/token</c>: cada intento verifica un hash Argon2id
    /// (deliberadamente caro, ~19 MiB de memoria por verificación), así que
    /// sin tope es a la vez un vector de adivinación de <c>client_secret</c>
    /// y de agotamiento de memoria/CPU. Un integrador legítimo pide un token
    /// por hora: 10 por minuto por origen sobra.
    /// </summary>
    public int TokenPorMinuto { get; set; } = 10;

    /// <summary>
    /// <c>POST /api/auth/interno/emitir</c>: lo llaman los servicios en cada
    /// intercambio, o sea con tráfico real y sostenido — el límite solo
    /// frena un abuso descontrolado, no el uso normal.
    /// </summary>
    public int InternoPorMinuto { get; set; } = 3000;
}

/// <summary>
/// Limitación de tasa (ver RUNBOOK.md 12.29): ventana fija por dirección
/// remota, respuesta <c>429</c> con <c>Retry-After</c>. La partición es por
/// IP del par TCP: detrás de un balanceador/proxy inverso propio hay que
/// habilitar <c>ForwardedHeaders</c> para que sea la del cliente real.
/// </summary>
public static class LimitacionDeTasa
{
    public const string PoliticaToken = "token";
    public const string PoliticaInterno = "interno-emitir";

    public static IServiceCollection AddLimitacionDeTasaGateway(this IServiceCollection services, IConfiguration configuration)
    {
        var opciones = configuration.GetSection(OpcionesLimitacionDeTasa.SeccionConfiguracion).Get<OpcionesLimitacionDeTasa>()
            ?? new OpcionesLimitacionDeTasa();

        services.AddRateLimiter(limitador =>
        {
            limitador.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limitador.AddPolicy(PoliticaToken, contexto => Ventana(contexto, opciones.TokenPorMinuto));
            limitador.AddPolicy(PoliticaInterno, contexto => Ventana(contexto, opciones.InternoPorMinuto));
            limitador.OnRejected = (contexto, _) =>
            {
                if (contexto.Lease.TryGetMetadata(MetadataName.RetryAfter, out var espera))
                    contexto.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(espera.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                return ValueTask.CompletedTask;
            };
        });
        return services;
    }

    private static RateLimitPartition<string> Ventana(HttpContext contexto, int permisosPorMinuto) =>
        RateLimitPartition.GetFixedWindowLimiter(
            contexto.Connection.RemoteIpAddress?.ToString() ?? "desconocido",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permisosPorMinuto,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            });
}
