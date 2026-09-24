using Microsoft.Extensions.Configuration;

namespace SecureSign.Shared.Auth;

/// <summary>
/// Origen de configuración para secretos entregados como ARCHIVOS — el
/// formato nativo de Docker/Swarm secrets (<c>/run/secrets</c>) y de los
/// Secrets de Kubernetes montados como volumen. Un archivo por secreto: el
/// nombre es la clave de configuración, con <c>__</c> en lugar de <c>:</c>
/// (p. ej. <c>Jwt__SecretoClienteInterno</c> o
/// <c>Jwt__ServiciosEmisores__0__Secretos__0</c>), y el contenido es el valor
/// sin el salto de línea final.
///
/// Así el secreto nunca aparece en el <c>appsettings.json</c>, en una
/// variable de entorno visible con <c>docker inspect</c>/<c>/proc/&lt;pid&gt;/environ</c>
/// ni en el compose. Se agrega DESPUÉS de appsettings y de las variables de
/// entorno, así que un secreto en archivo tiene la precedencia más alta
/// (RUNBOOK.md 12.30). Un directorio que no existe se ignora — el mismo
/// código corre en desarrollo sin ningún almacén de secretos.
///
/// Se lee una sola vez al arrancar (rotar un secreto = reiniciar el
/// servicio). No usa <c>AddKeyPerFile</c> porque este NO recorta el salto de
/// línea final: un <c>echo secreto &gt; archivo</c> dejaba <c>"secreto\n"</c>
/// como valor, distinto del secreto que envía el otro extremo.
/// </summary>
public static class SecretosDeArchivo
{
    /// <summary>Variable de entorno que redefine el directorio (por defecto <see cref="DirectorioPorDefecto"/>).</summary>
    public const string VariableDirectorio = "SECURESIGN_SECRETS_DIR";

    public const string DirectorioPorDefecto = "/run/secrets";

    public static IConfigurationBuilder AddSecureSignSecretosDeArchivo(this IConfigurationBuilder configuracion, string? directorio = null)
    {
        directorio ??= Environment.GetEnvironmentVariable(VariableDirectorio) ?? DirectorioPorDefecto;
        return configuracion.AddInMemoryCollection(Leer(directorio));
    }

    private static Dictionary<string, string?> Leer(string directorio)
    {
        var valores = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(directorio)) return valores;

        foreach (var archivo in Directory.EnumerateFiles(directorio))
        {
            var nombre = Path.GetFileName(archivo);
            if (nombre.StartsWith('.')) continue; // metadatos (p. ej. ..data de los volúmenes de Kubernetes)

            valores[nombre.Replace("__", ConfigurationPath.KeyDelimiter)] =
                File.ReadAllText(archivo).TrimEnd('\r', '\n');
        }
        return valores;
    }
}
