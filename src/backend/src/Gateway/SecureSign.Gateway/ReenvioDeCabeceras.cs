using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace SecureSign.Gateway;

/// <summary>
/// Sección <c>ReenvioDeCabeceras</c> de appsettings.json — RUNBOOK.md 12.36.
/// Por defecto DESACTIVADO: el Gateway usa la IP del par TCP, que es correcta cuando los clientes le llegan
/// directo. Detrás de un balanceador o proxy inverso todos los clientes tendrían la IP del proxy (y por tanto
/// compartirían el mismo cupo de limitación de tasa, ver RUNBOOK.md 12.29): entonces se activa y se declaran
/// EXPLÍCITAMENTE los proxies de confianza.
/// </summary>
public sealed class OpcionesReenvioDeCabeceras
{
    public const string SeccionConfiguracion = "ReenvioDeCabeceras";

    public bool Habilitado { get; set; }

    /// <summary>IP de proxies individuales cuyas cabeceras <c>X-Forwarded-*</c> se aceptan.</summary>
    public string[] ProxiesConfiables { get; set; } = [];

    /// <summary>Redes en notación CIDR (p. ej. <c>10.0.0.0/8</c>) cuyas cabeceras <c>X-Forwarded-*</c> se aceptan.</summary>
    public string[] RedesConfiables { get; set; } = [];

    /// <summary>
    /// Cuántos saltos de proxy se procesan. 1 (por defecto) toma la dirección que agregó el proxy
    /// MÁS CERCANO (la última del encabezado), que es la única que ese proxy vio con sus propios ojos: las
    /// anteriores las escribió el cliente y son falsificables.
    /// </summary>
    public int LimiteDeSaltos { get; set; } = 1;
}

public static class ReenvioDeCabeceras
{
    /// <summary>
    /// Falla el arranque si se activa sin declarar en quién confiar: con las listas vacías ASP.NET Core solo
    /// confía en loopback, o sea que "activado" no haría nada y el operador creería estar protegido; y la
    /// alternativa peligrosa (confiar en cualquiera) dejaría que un cliente falsifique su IP con un
    /// <c>X-Forwarded-For</c> y evada la limitación de tasa.
    /// </summary>
    public static IServiceCollection AddReenvioDeCabecerasGateway(this IServiceCollection services, IConfiguration configuracion)
    {
        var opciones = configuracion.GetSection(OpcionesReenvioDeCabeceras.SeccionConfiguracion).Get<OpcionesReenvioDeCabeceras>()
            ?? new OpcionesReenvioDeCabeceras();
        if (!opciones.Habilitado) return services;

        if (opciones.ProxiesConfiables.Length == 0 && opciones.RedesConfiables.Length == 0)
            throw new InvalidOperationException(
                "ReenvioDeCabeceras:Habilitado requiere declarar ProxiesConfiables y/o RedesConfiables — sin ellos no se sabría en quién confiar.");
        if (opciones.LimiteDeSaltos < 1)
            throw new InvalidOperationException("ReenvioDeCabeceras:LimiteDeSaltos debe ser al menos 1.");

        var proxies = opciones.ProxiesConfiables.Select(p => IPAddress.TryParse(p, out var ip)
            ? ip
            : throw new InvalidOperationException($"ReenvioDeCabeceras:ProxiesConfiables contiene una IP inválida: '{p}'.")).ToList();
        var redes = opciones.RedesConfiables.Select(ParsearRed).ToList();

        services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            o.ForwardLimit = opciones.LimiteDeSaltos;

            // Se descarta el valor por defecto (loopback) para que la lista sea EXACTAMENTE la configurada.
            o.KnownProxies.Clear();
            o.KnownNetworks.Clear();
            foreach (var p in proxies) o.KnownProxies.Add(p);
            foreach (var r in redes) o.KnownNetworks.Add(r);
        });
        return services;
    }

    /// <summary>Debe ir PRIMERO en el pipeline: la limitación de tasa, HSTS y la redirección HTTPS leen IP y esquema.</summary>
    public static IApplicationBuilder UseReenvioDeCabecerasGateway(this IApplicationBuilder app)
    {
        var habilitado = app.ApplicationServices.GetRequiredService<IConfiguration>()
            .GetSection(OpcionesReenvioDeCabeceras.SeccionConfiguracion).GetValue<bool>(nameof(OpcionesReenvioDeCabeceras.Habilitado));
        return habilitado ? app.UseForwardedHeaders() : app;
    }

    private static Microsoft.AspNetCore.HttpOverrides.IPNetwork ParsearRed(string cidr)
    {
        var partes = cidr.Split('/');
        if (partes.Length != 2 || !IPAddress.TryParse(partes[0], out var prefijo) || !int.TryParse(partes[1], out var longitud)
            || longitud < 0 || longitud > (prefijo.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128))
            throw new InvalidOperationException($"ReenvioDeCabeceras:RedesConfiables contiene una red CIDR inválida: '{cidr}' (formato esperado: 10.0.0.0/8).");
        return new Microsoft.AspNetCore.HttpOverrides.IPNetwork(prefijo, longitud);
    }
}
