using Microsoft.AspNetCore.Cors.Infrastructure;

namespace SecureSign.Gateway;

/// <summary>
/// Endurecimiento HTTP del Gateway, el único borde público de la plataforma
/// (RUNBOOK.md 12.31): cabeceras de seguridad en toda respuesta, y CORS
/// explícito por configuración en vez de abierto siempre.
/// </summary>
public static class SeguridadHttp
{
    public const string PoliticaCors = "visor-firma";
    public const string SeccionOrigenes = "Cors:OrigenesPermitidos";

    /// <summary>
    /// Cabeceras en TODA respuesta del Gateway, propia o proxied:
    /// <list type="bullet">
    /// <item><c>Cache-Control: no-store</c> + <c>Pragma: no-cache</c> si la respuesta no trae su propio
    /// <c>Cache-Control</c> — RFC 6749 §5.1 lo EXIGE en la respuesta del endpoint de token, y también aplica a
    /// documentos y evidencias, que no deben quedar en cachés compartidas.</item>
    /// <item><c>X-Content-Type-Options: nosniff</c> — un PDF descargado nunca se reinterpreta como HTML/script.</item>
    /// <item><c>X-Frame-Options: DENY</c> y <c>Content-Security-Policy: default-src 'none'; frame-ancestors 'none'</c> —
    /// el Gateway sirve JSON y binarios, jamás una página; nadie debe poder enmarcarlo. (El CSP se omite en
    /// <c>/swagger</c>, que es una página con scripts, y esa ruta solo existe en Development.)</item>
    /// <item><c>Referrer-Policy: no-referrer</c>.</item>
    /// </list>
    /// </summary>
    public static IApplicationBuilder UseCabecerasDeSeguridad(this IApplicationBuilder app) =>
        app.Use((contexto, siguiente) =>
        {
            contexto.Response.OnStarting(() =>
            {
                var cabeceras = contexto.Response.Headers;
                cabeceras["X-Content-Type-Options"] = "nosniff";
                cabeceras["X-Frame-Options"] = "DENY";
                cabeceras["Referrer-Policy"] = "no-referrer";
                if (!contexto.Request.Path.StartsWithSegments("/swagger"))
                    cabeceras["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
                if (!cabeceras.ContainsKey("Cache-Control"))
                {
                    cabeceras["Cache-Control"] = "no-store";
                    cabeceras["Pragma"] = "no-cache";
                }
                return Task.CompletedTask;
            });
            return siguiente(contexto);
        });

    /// <summary>
    /// CORS del visor de firma. <b>Development</b>: cualquier origen (el visor estático se abre desde
    /// <c>file://</c> o un servidor local en otro puerto). <b>Fuera de Development</b>: solo los orígenes de
    /// <c>Cors:OrigenesPermitidos</c>; sin ninguno configurado NO se emite ninguna cabecera CORS (el navegador
    /// bloquea las llamadas entre orígenes) — cerrado por defecto, en vez del <c>AllowAnyOrigin</c> permanente
    /// que había antes, que además habría permitido a cualquier página web leer respuestas del Gateway con un
    /// token robado.
    /// </summary>
    public static IServiceCollection AddCorsGateway(this IServiceCollection services, IConfiguration configuracion, IHostEnvironment entorno)
    {
        var origenes = configuracion.GetSection(SeccionOrigenes).Get<string[]>() ?? [];

        return services.AddCors(opciones => opciones.AddPolicy(PoliticaCors, PoliticaPara(entorno.IsDevelopment(), origenes)));
    }

    private static Action<CorsPolicyBuilder> PoliticaPara(bool desarrollo, string[] origenes) => politica =>
    {
        if (desarrollo) politica.AllowAnyOrigin();
        else if (origenes.Length > 0) politica.WithOrigins(origenes);
        else return; // sin orígenes configurados: ninguna cabecera CORS

        politica.AllowAnyMethod()
            .AllowAnyHeader()
            .WithExposedHeaders("X-Hash-Documento", "X-Evidencia-Url", "Content-Disposition");
    };
}
