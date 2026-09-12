using System.Security.Cryptography;
using System.Text;

namespace SecureSign.Evidence.Domain;

/// <summary>
/// Agregación periódica de un lote de HashEvento en una raíz de Merkle, para
/// sellado de tiempo conjunto (RFC 3161) en lugar de sellar cada evento
/// individualmente. Ver docs/02-innovacion-patente/analisis-innovaciones.md
/// innovación #1 y #4 (registro permisionado multi-tenant).
/// </summary>
public static class AgregadorMerkle
{
    public static string CalcularRaiz(IReadOnlyList<string> hashesHex)
    {
        if (hashesHex.Count == 0)
            throw new ArgumentException("No se puede calcular una raíz de Merkle de un lote vacío.");

        var nivelActual = hashesHex.Select(h => Convert.FromHexString(h)).ToList();

        while (nivelActual.Count > 1)
        {
            var siguienteNivel = new List<byte[]>();
            for (var i = 0; i < nivelActual.Count; i += 2)
            {
                var izquierda = nivelActual[i];
                var derecha = i + 1 < nivelActual.Count ? nivelActual[i + 1] : nivelActual[i]; // duplica el último si es impar
                siguienteNivel.Add(SHA256.HashData(izquierda.Concat(derecha).ToArray()));
            }
            nivelActual = siguienteNivel;
        }

        return Convert.ToHexString(nivelActual[0]).ToLowerInvariant();
    }

    /// <summary>
    /// Genera la prueba de inclusión (Merkle proof) de un hash específico dentro
    /// del lote, permitiendo verificar que perteneció al lote sellado sin
    /// necesidad de revelar los demás eventos del lote (que pueden ser de otros
    /// tenants si el registro permisionado agrega across tenants).
    /// </summary>
    public static IReadOnlyList<(string HashHermano, bool EsIzquierdo)> GenerarPrueba(IReadOnlyList<string> hashesHex, int indice)
    {
        var prueba = new List<(string, bool)>();
        var nivelActual = hashesHex.Select(h => Convert.FromHexString(h)).ToList();
        var posicion = indice;

        while (nivelActual.Count > 1)
        {
            var esPosicionPar = posicion % 2 == 0;
            var indiceHermano = esPosicionPar ? posicion + 1 : posicion - 1;
            if (indiceHermano < nivelActual.Count)
                prueba.Add((Convert.ToHexString(nivelActual[indiceHermano]).ToLowerInvariant(), !esPosicionPar));

            var siguienteNivel = new List<byte[]>();
            for (var i = 0; i < nivelActual.Count; i += 2)
            {
                var izquierda = nivelActual[i];
                var derecha = i + 1 < nivelActual.Count ? nivelActual[i + 1] : nivelActual[i];
                siguienteNivel.Add(SHA256.HashData(izquierda.Concat(derecha).ToArray()));
            }
            nivelActual = siguienteNivel;
            posicion /= 2;
        }

        return prueba;
    }
}
