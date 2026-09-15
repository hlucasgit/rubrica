using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;

namespace SecureSign.Shared.Auth;

/// <summary>
/// Hash de secretos de clientes integradores (<c>client_secret</c> de
/// <c>ClientesDemo</c>) con Argon2id — ver
/// <c>docs/07-seguridad/modelo-seguridad.md</c> sección 6 ("ClientSecret
/// almacenado solo como hash") y RUNBOOK.md 12.23. Antes de esto, el secreto
/// se guardaba en texto plano en <c>appsettings.json</c> — cualquiera con
/// acceso a ese archivo (o a la variable de entorno que lo sobrescribe)
/// obtenía directamente una credencial utilizable, sin ningún paso
/// intermedio. Con el hash, ese acceso ya no basta: hace falta romper
/// Argon2id, no solo leer un archivo.
///
/// Parámetros (m=19 MiB, t=2, p=1) siguen la recomendación mínima de OWASP
/// para Argon2id en un proceso de servidor sin hardware dedicado — no son
/// arbitrarios, pero tampoco los más altos posibles: este hash se calcula
/// en cada <c>POST /api/auth/token</c>, así que un costo excesivo degradaría
/// la latencia de autenticación de todos los integradores, no solo de un
/// atacante.
///
/// Formato de salida, estilo PHC simplificado (no la librería de referencia,
/// una codificación propia mínima con los mismos campos):
/// <c>$argon2id$v=19$m=&lt;KB&gt;,t=&lt;iter&gt;,p=&lt;paralelismo&gt;$&lt;salt base64&gt;$&lt;hash base64&gt;</c>.
/// Guardar los parámetros junto al hash (en vez de fijarlos solo en código)
/// es lo que permite subir el costo en el futuro sin invalidar los hashes
/// ya almacenados — <see cref="Verificar"/> siempre usa los parámetros
/// leídos del propio hash, nunca los constantes actuales.
/// </summary>
public static class Argon2idSecretHasher
{
    private const int MemoriaKb = 19 * 1024;
    private const int Iteraciones = 2;
    private const int Paralelismo = 1;
    private const int TamanoSalt = 16;
    private const int TamanoHash = 32;

    public static string Hashear(string secreto)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(TamanoSalt);
        byte[] hash = Derivar(secreto, salt, MemoriaKb, Iteraciones, Paralelismo, TamanoHash);
        return $"$argon2id$v=19$m={MemoriaKb},t={Iteraciones},p={Paralelismo}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    /// <summary>
    /// true solo si <paramref name="secreto"/> produce, con los parámetros y
    /// la sal guardados en <paramref name="hashAlmacenado"/>, exactamente el
    /// mismo hash — comparado en tiempo constante. Un <paramref name="hashAlmacenado"/>
    /// con formato inválido nunca lanza: se trata como "no coincide", igual
    /// que un secreto incorrecto (evita que un valor de configuración
    /// corrupto se comporte distinto a un secreto simplemente equivocado).
    /// </summary>
    public static bool Verificar(string secreto, string hashAlmacenado)
    {
        if (!TryParsear(hashAlmacenado, out int memoriaKb, out int iteraciones, out int paralelismo, out byte[] salt, out byte[] hashEsperado))
            return false;

        byte[] hashCalculado = Derivar(secreto, salt, memoriaKb, iteraciones, paralelismo, hashEsperado.Length);
        return CryptographicOperations.FixedTimeEquals(hashCalculado, hashEsperado);
    }

    private static byte[] Derivar(string secreto, byte[] salt, int memoriaKb, int iteraciones, int paralelismo, int tamanoHash)
    {
        var parametros = new Argon2Parameters.Builder(Argon2Parameters.Argon2id)
            .WithVersion(Argon2Parameters.Version13)
            .WithIterations(iteraciones)
            .WithMemoryAsKB(memoriaKb)
            .WithParallelism(paralelismo)
            .WithSalt(salt)
            .Build();

        var generador = new Argon2BytesGenerator();
        generador.Init(parametros);
        byte[] hash = new byte[tamanoHash];
        generador.GenerateBytes(Encoding.UTF8.GetBytes(secreto), hash);
        return hash;
    }

    private static bool TryParsear(string hashAlmacenado, out int memoriaKb, out int iteraciones, out int paralelismo, out byte[] salt, out byte[] hash)
    {
        memoriaKb = iteraciones = paralelismo = 0;
        salt = hash = [];

        // "$argon2id$v=19$m=..,t=..,p=..$<salt-b64>$<hash-b64>" → 5 campos no vacíos tras el split.
        string[] partes = hashAlmacenado.Split('$', StringSplitOptions.RemoveEmptyEntries);
        if (partes.Length != 5 || partes[0] != "argon2id" || partes[1] != $"v={Argon2Parameters.Version13}")
            return false;

        string[] parametrosCrudos = partes[2].Split(',');
        if (parametrosCrudos.Length != 3) return false;

        if (!TryLeerParametro(parametrosCrudos[0], 'm', out memoriaKb)) return false;
        if (!TryLeerParametro(parametrosCrudos[1], 't', out iteraciones)) return false;
        if (!TryLeerParametro(parametrosCrudos[2], 'p', out paralelismo)) return false;

        try
        {
            salt = Convert.FromBase64String(partes[3]);
            hash = Convert.FromBase64String(partes[4]);
        }
        catch (FormatException) { return false; }

        return salt.Length > 0 && hash.Length > 0;
    }

    private static bool TryLeerParametro(string crudo, char prefijoEsperado, out int valor)
    {
        valor = 0;
        // "m=19456" → prefijo 'm' en [0], '=' en [1], el número empieza en [2].
        if (crudo.Length < 3 || crudo[0] != prefijoEsperado || crudo[1] != '=') return false;
        return int.TryParse(crudo.AsSpan(2), out valor) && valor > 0;
    }
}
