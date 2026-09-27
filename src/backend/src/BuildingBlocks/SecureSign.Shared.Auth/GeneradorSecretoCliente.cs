using System.Security.Cryptography;

namespace SecureSign.Shared.Auth;

/// <summary>
/// Genera el <c>client_secret</c> de un integrador y el hash Argon2id que se guarda en la configuración del
/// Gateway (<c>ClientesDemo:Clientes[*]:SecretosHash</c>, ver <see cref="Argon2idSecretHasher"/>). El secreto en
/// claro existe UNA sola vez: se le entrega al integrador y no se guarda en ningún lado (RUNBOOK.md 12.39).
/// </summary>
public static class GeneradorSecretoCliente
{
    public const int BytesPorDefecto = 32;

    /// <summary>24 bytes = 192 bits: por debajo de eso ya no es un secreto de alta entropía sino una contraseña.</summary>
    public const int BytesMinimos = 24;

    /// <returns>El secreto en base64url sin relleno (apto para pegarse en un formulario o una cabecera) y su hash Argon2id.</returns>
    public static (string Secreto, string Hash) Generar(int bytes = BytesPorDefecto)
    {
        if (bytes < BytesMinimos)
            throw new ArgumentOutOfRangeException(nameof(bytes), $"Un client_secret necesita al menos {BytesMinimos} bytes de entropía.");

        string secreto = Base64Url(RandomNumberGenerator.GetBytes(bytes));
        return (secreto, Argon2idSecretHasher.Hashear(secreto));
    }

    private static string Base64Url(byte[] datos) =>
        Convert.ToBase64String(datos).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
