using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace SecureSign.Shared.Auth;

/// <summary>
/// Regla de arranque para los secretos internos de <see cref="JwtOptions"/>:
/// fuera de Development, ninguno puede ser el valor de desarrollo que viaja en
/// el repositorio (<c>dev-only-...</c>) ni tener menos de
/// <see cref="LongitudMinima"/> caracteres. Cierra el hueco de que un
/// despliegue productivo arranque "bien" con el secreto de ejemplo del
/// appsettings.json — el servicio se niega a arrancar en vez de hacerlo
/// inseguro en silencio. Ver RUNBOOK.md 12.27.
/// </summary>
public sealed class ValidadorOpcionesJwt(IHostEnvironment entorno) : IValidateOptions<JwtOptions>
{
    public const string PrefijoDesarrollo = "dev-only-";
    public const int LongitudMinima = 32;

    public ValidateOptionsResult Validate(string? name, JwtOptions opciones) =>
        Validar(opciones, entorno.IsDevelopment()) is { Count: > 0 } errores
            ? ValidateOptionsResult.Fail(errores)
            : ValidateOptionsResult.Success;

    public static IReadOnlyList<string> Validar(JwtOptions opciones, bool esDesarrollo)
    {
        var errores = new List<string>();
        if (esDesarrollo) return errores;

        if (opciones.ServiciosEmisores.Count > 0 && opciones.PuertoInterno is null)
            errores.Add("Jwt:PuertoInterno es obligatorio fuera de Development: sin él, POST /api/auth/interno/emitir queda expuesto en el mismo puerto público del Gateway.");

        var secretos = new List<(string Origen, string Valor)> { ("Jwt:SecretoClienteInterno", opciones.SecretoClienteInterno) };
        foreach (var servicio in opciones.ServiciosEmisores)
            secretos.AddRange(servicio.Secretos.Select(s => ($"Jwt:ServiciosEmisores['{servicio.Nombre}']", s)));

        foreach (var (origen, secreto) in secretos.Where(s => !string.IsNullOrEmpty(s.Valor)))
        {
            if (secreto.StartsWith(PrefijoDesarrollo, StringComparison.Ordinal))
                errores.Add($"{origen}: usa el valor de desarrollo ('{PrefijoDesarrollo}...'); defínelo por archivo de secretos, variable de entorno o almacén de secretos fuera de Development.");
            else if (secreto.Length < LongitudMinima)
                errores.Add($"{origen}: tiene menos de {LongitudMinima} caracteres.");
        }
        return errores;
    }
}
