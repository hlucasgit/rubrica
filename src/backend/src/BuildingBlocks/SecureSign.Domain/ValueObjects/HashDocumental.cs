using System.Security.Cryptography;

namespace SecureSign.Domain.ValueObjects;

/// <summary>
/// Hash SHA-256 de un documento. Se usa tanto al registrar el documento como
/// para revalidar integridad justo antes de habilitar la firma (ver
/// docs/02-innovacion-patente/analisis-innovaciones.md, innovación #2).
/// </summary>
public sealed record HashDocumental
{
    public string Algoritmo { get; } = "SHA-256";
    public string ValorHex { get; }

    private HashDocumental(string valorHex) => ValorHex = valorHex;

    public static HashDocumental CalcularDesde(Stream contenido)
    {
        if (contenido.CanSeek) contenido.Position = 0;
        var hashBytes = SHA256.HashData(contenido);
        return new HashDocumental(Convert.ToHexString(hashBytes).ToLowerInvariant());
    }

    public static HashDocumental CalcularDesde(byte[] contenido)
        => new(Convert.ToHexString(SHA256.HashData(contenido)).ToLowerInvariant());

    public static HashDocumental DesdeValorConocido(string valorHex) => new(valorHex.ToLowerInvariant());

    public bool CoincideCon(HashDocumental otro) =>
        CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(ValorHex),
            Convert.FromHexString(otro.ValorHex));

    public override string ToString() => ValorHex;
}
