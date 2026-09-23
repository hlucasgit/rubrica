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

        foreach (var secreto in opciones.SecretosInternosAceptados())
        {
            if (secreto.StartsWith(PrefijoDesarrollo, StringComparison.Ordinal))
                errores.Add($"Jwt: un secreto interno usa el valor de desarrollo ('{PrefijoDesarrollo}...'); defínelo por variable de entorno o almacén de secretos fuera de Development.");
            else if (secreto.Length < LongitudMinima)
                errores.Add($"Jwt: un secreto interno tiene menos de {LongitudMinima} caracteres.");
        }
        return errores;
    }
}
