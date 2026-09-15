using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace SecureSign.Shared.Auth;

/// <summary>Una llave RSA de firma, con el identificador (<c>kid</c>) que la distingue en el JWKS y en el header de cada JWT que firma.</summary>
public sealed record LlaveFirma(string Kid, RSA Rsa, DateTimeOffset CreadaEn)
{
    public RsaSecurityKey ComoSecurityKey() => new(Rsa) { KeyId = Kid };
}

/// <summary>
/// Llaves RSA-2048 del Gateway (único emisor de JWT — ver RUNBOOK.md 12.21),
/// persistidas como archivos PEM en un directorio configurable
/// (<see cref="JwtOptions.DirectorioLlaves"/>) — deliberadamente FUERA del
/// repositorio y de cualquier <c>appsettings.json</c>, para cerrar el
/// hallazgo del informe de preauditoría sobre "secretos externalizados".
///
/// Si el directorio está vacío, genera una llave nueva automáticamente al
/// arrancar — no hace falta ningún gesto manual para levantar el stack la
/// primera vez. El nombre de archivo (<c>AAAAMMDDHHmmss_kid.pem</c>) ordena
/// alfabéticamente en orden cronológico, así que "la llave activa" es
/// siempre, sin ambigüedad, la de nombre más alto — sin depender de
/// metadatos de filesystem (creation time no es confiable de forma uniforme
/// entre Windows/Linux/contenedores).
///
/// Publica TODAS las llaves vigentes (no solo la activa) para que el JWKS
/// (ver OidcController en el Gateway) permita validar tokens firmados con
/// una llave anterior mientras esté vigente — esto es el mecanismo que hace
/// posible la rotación: rotar es agregar un archivo de llave nuevo (pasa a
/// ser la activa de inmediato) y, pasado un período de gracia documentado
/// en la política de versiones, borrar el archivo de la llave vieja. No hay
/// un disparador automático de rotación por tiempo — es un procedimiento
/// operativo, no una tarea en segundo plano.
/// </summary>
public sealed class RsaKeyStore
{
    private const int BitsLlave = 2048;
    private readonly List<LlaveFirma> _llaves;

    public RsaKeyStore(IOptions<JwtOptions> opciones)
    {
        string directorio = opciones.Value.DirectorioLlaves;
        Directory.CreateDirectory(directorio);

        var archivos = Directory.GetFiles(directorio, "*.pem").OrderBy(f => f, StringComparer.Ordinal).ToArray();
        if (archivos.Length == 0)
            archivos = [GenerarYPersistir(directorio)];

        _llaves = archivos.Select(CargarDesdeArchivo).ToList();
    }

    /// <summary>La llave con la que se firma todo JWT nuevo — siempre la más reciente.</summary>
    public LlaveFirma LlaveDeFirmaActiva => _llaves[^1];

    /// <summary>Todas las llaves que todavía deben aceptarse al validar (para publicar en el JWKS) — incluye llaves en período de gracia de rotación.</summary>
    public IReadOnlyList<LlaveFirma> LlavesVigentes => _llaves;

    private static LlaveFirma CargarDesdeArchivo(string ruta)
    {
        string nombre = Path.GetFileNameWithoutExtension(ruta);
        var partes = nombre.Split('_', 2);
        if (partes.Length != 2)
            throw new InvalidOperationException($"Nombre de archivo de llave con formato inesperado: '{ruta}' (se espera 'AAAAMMDDHHmmss_kid.pem').");

        var creadaEn = DateTimeOffset.ParseExact(partes[0], "yyyyMMddHHmmss", null, System.Globalization.DateTimeStyles.AssumeUniversal);
        var rsa = RSA.Create();
        rsa.ImportFromPem(File.ReadAllText(ruta));
        return new LlaveFirma(partes[1], rsa, creadaEn);
    }

    private static string GenerarYPersistir(string directorio)
    {
        using var rsa = RSA.Create(BitsLlave);
        string kid = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
        string nombreArchivo = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}_{kid}.pem";
        string ruta = Path.Combine(directorio, nombreArchivo);
        File.WriteAllText(ruta, rsa.ExportPkcs8PrivateKeyPem());
        return ruta;
    }
}
