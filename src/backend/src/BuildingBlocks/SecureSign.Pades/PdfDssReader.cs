using System.Text;
using System.Text.RegularExpressions;

namespace SecureSign.Pades;

/// <summary>Material (DER crudo) que un PDF lleva embebido en su <c>/DSS</c> — ver <see cref="PdfDssReader"/>.</summary>
public sealed record MaterialDssLeido(
    IReadOnlyList<byte[]> CertificadosDer,
    IReadOnlyList<byte[]> CrlsDer,
    IReadOnlyList<byte[]> OcspsDer)
{
    public bool EstaVacio => CertificadosDer.Count == 0 && CrlsDer.Count == 0 && OcspsDer.Count == 0;
}

/// <summary>
/// Lee el Document Security Store (DSS) que <see cref="PdfDssWriter"/> escribió
/// (PAdES-LT, RUNBOOK.md 12.24) — la mitad que faltaba: sin poder LEERLO, el
/// material embebido no servía para nada al validar. Devuelve TODO el material
/// del <c>/DSS</c> más reciente (los arrays globales <c>/Certs</c>, <c>/CRLs</c>,
/// <c>/OCSPs</c>), sin filtrar por la entrada <c>/VRI</c> de cada firma: este
/// componente solo extrae bytes; quien los USA (SecureSign.Trust) verifica
/// criptográficamente cada pieza —firma de la CRL/OCSP contra el emisor, cadena
/// contra las raíces configuradas—, así que material ajeno o inyectado por un
/// atacante no puede hacer pasar por confiable algo que no lo es.
///
/// Igual que <see cref="PdfDssWriter"/>, solo entiende PDF con xref clásico
/// (el que escribe este mismo proyecto); ante cualquier PDF que no pueda
/// recorrer devuelve <c>null</c> ("sin DSS utilizable"), nunca lanza — el
/// llamador cae a validar contra la red, que es el comportamiento anterior.
/// </summary>
public static class PdfDssReader
{
    public static MaterialDssLeido? Leer(byte[] pdf)
    {
        try
        {
            string texto = Encoding.Latin1.GetString(pdf);
            long startXref = PdfSignaturePlaceholder.EncontrarUltimoStartXref(pdf);
            var mapa = PdfSignaturePlaceholder.CaminarCadenaXref(texto, startXref, out int rootNum, out _);
            string catalogo = PdfSignaturePlaceholder.ObtenerTextoObjeto(texto, mapa[rootNum].Offset);

            if (PdfSignaturePlaceholder.BuscarReferencia(catalogo, "/DSS") is not { } refDss) return null;
            string textoDss = PdfSignaturePlaceholder.ObtenerTextoObjeto(texto, mapa[refDss.Num].Offset);

            var material = new MaterialDssLeido(
                LeerStreams(texto, mapa, PdfSignaturePlaceholder.BuscarArrayDeReferencias(textoDss, "/Certs")),
                LeerStreams(texto, mapa, PdfSignaturePlaceholder.BuscarArrayDeReferencias(textoDss, "/CRLs")),
                LeerStreams(texto, mapa, PdfSignaturePlaceholder.BuscarArrayDeReferencias(textoDss, "/OCSPs")));
            return material.EstaVacio ? null : material;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static List<byte[]> LeerStreams(
        string texto, Dictionary<int, PdfSignaturePlaceholder.EntradaXref> mapa, List<(int Num, int Gen)> referencias)
    {
        var resultado = new List<byte[]>(referencias.Count);
        foreach (var (num, _) in referencias)
        {
            if (!mapa.TryGetValue(num, out var entrada)) continue;

            // El contenido es DER binario: se corta por /Length, nunca buscando "endobj"/"endstream"
            // (podrían aparecer por casualidad dentro de los bytes).
            int inicioObjeto = (int)entrada.Offset;
            var longitud = Regex.Match(texto.Substring(inicioObjeto, Math.Min(200, texto.Length - inicioObjeto)), @"/Length\s+(\d+)");
            int idxStream = texto.IndexOf("stream\n", inicioObjeto, StringComparison.Ordinal);
            if (!longitud.Success || idxStream < 0) continue;

            int inicioContenido = idxStream + "stream\n".Length;
            int largo = int.Parse(longitud.Groups[1].Value);
            if (inicioContenido + largo > texto.Length) continue;

            resultado.Add(Encoding.Latin1.GetBytes(texto.Substring(inicioContenido, largo)));
        }
        return resultado;
    }
}
