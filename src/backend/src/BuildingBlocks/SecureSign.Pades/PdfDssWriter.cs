using System.Security.Cryptography;
using System.Text;

namespace SecureSign.Pades;

/// <param name="CadenaCertificadosDer">Certificados (DER) de la cadena que validó la firma — hoja, intermedios y raíz.</param>
/// <param name="CrlDer">La CRL (DER) que demostró "no revocado", si se pudo obtener una.</param>
/// <param name="OcspRespuestaDer">La respuesta OCSP (DER) completa que demostró "no revocado", si se pudo obtener una.</param>
public sealed record MaterialDss(
    IReadOnlyList<byte[]> CadenaCertificadosDer,
    byte[]? CrlDer,
    byte[]? OcspRespuestaDer);

/// <summary>
/// Agrega (o extiende) el Document Security Store (DSS) de un PDF ya firmado
/// — la pieza que falta para pasar de PAdES-B/T a PAdES-LT (informe de
/// preauditoría INDECOPI/IOFE, "PAdES-T/LT/LTA"; RUNBOOK.md 12.24). Embebe,
/// como UNA revisión incremental más (ISO 32000-1 §7.5.6, mismo mecanismo que
/// <see cref="PdfSignaturePlaceholder"/>), la cadena de certificados y la
/// CRL/OCSP que ya demostraron que la firma era de fiar EN EL MOMENTO DE
/// FIRMAR — así la firma se puede seguir validando más adelante sin volver a
/// consultar red, incluso si la CRL original ya expiró o el certificado del
/// firmante ya venció.
///
/// Estructura escrita (Adobe Supplement to ISO 32000, adoptada por ISO
/// 32000-2 §12.8.4.3 — ver RUNBOOK.md 12.24 para las fuentes consultadas):
/// <c>/DSS</c> en el Catálogo, con <c>/Certs</c>/<c>/CRLs</c>/<c>/OCSPs</c>
/// (arrays de referencias a objetos stream con el DER crudo) y
/// <c>/VRI</c> (un diccionario por firma, cuya CLAVE es el hexadecimal en
/// MAYÚSCULAS del hash SHA-1 del valor exacto — los bytes binarios, no el
/// texto hex — de <c>/Contents</c> de esa firma).
///
/// DELIBERADAMENTE NO escribe el diccionario <c>/Extensions</c> (ESIC) que
/// declara el nivel de extensión ISO 32000-2 en el Catálogo — es metadata de
/// descubrimiento, no la parte que hace la validación offline posible; se
/// documenta como simplificación explícita en vez de ocultarla.
///
/// Si el PDF YA tiene un <c>/DSS</c> (de una firma anterior de un documento
/// con varios firmantes, RUNBOOK.md 12.11), esta clase lo EXTIENDE: conserva
/// las entradas <c>/VRI</c> y las referencias de <c>/Certs</c>/<c>/CRLs</c>/<c>/OCSPs</c>
/// ya existentes (objetos de una revisión anterior, nunca se tocan ni se
/// borran) y agrega los de esta firma — solo entiende un <c>/DSS</c> que este
/// mismo componente escribió antes (mismo principio que
/// <see cref="PdfSignaturePlaceholder"/> para el lector propio: nunca se
/// intenta sobre un PDF de origen arbitrario). Puede repetir certificados
/// compartidos (p. ej. la misma raíz) entre revisiones sucesivas — el
/// formato lo permite (los validadores resuelven por contenido, no exigen
/// unicidad) y evita tener que decodificar el contenido de los streams ya
/// escritos solo para deduplicarlos.
/// </summary>
public static class PdfDssWriter
{
    /// <param name="cmsDeLaUltimaFirma">
    /// El CMS (DER) de la última firma del PDF, ya extraído y verificado por
    /// el llamador — normalmente <see cref="PdfSignatureVerifier.ResultadoVerificacionPades.CmsDer"/>
    /// de la misma llamada a <see cref="PdfSignatureVerifier.VerificarUltima"/>
    /// que el llamador ya tuvo que hacer para confirmar que la firma es
    /// válida antes de llegar aquí. Deliberadamente NO se vuelve a extraer
    /// ni a re-verificar aquí — eso implicaría decodificar el PDF completo y
    /// repetir la verificación criptográfica RSA por segunda vez en la misma
    /// operación de firma, solo para recalcular algo que el llamador ya tiene.
    /// </param>
    public static byte[] AgregarDss(byte[] pdfFirmado, byte[] cmsDeLaUltimaFirma, MaterialDss material)
    {
        // ISO 32000-2 §12.8.4.3: la clave VRI es el SHA-1, en hexadecimal
        // MAYÚSCULAS, del valor BINARIO exacto de /Contents de esa firma —
        // nunca del texto hexadecimal que aparece en el PDF, y nunca del
        // hueco completo relleno de ceros (por eso hace falta el CMS ya
        // recortado por longitud DER real, no los bytes crudos de /Contents).
        string claveVri = Convert.ToHexString(SHA1.HashData(cmsDeLaUltimaFirma));

        long longitudOriginal = pdfFirmado.LongLength;
        long prevStartXref = PdfSignaturePlaceholder.EncontrarUltimoStartXref(pdfFirmado);
        string texto = Encoding.Latin1.GetString(pdfFirmado);

        var mapa = PdfSignaturePlaceholder.CaminarCadenaXref(texto, prevStartXref, out int rootNum, out int rootGen);
        string textoCatalogo = PdfSignaturePlaceholder.ObtenerTextoObjeto(texto, mapa[rootNum].Offset);

        var refDssVieja = PdfSignaturePlaceholder.BuscarReferencia(textoCatalogo, "/DSS");
        List<(int Num, int Gen)> certsViejos = [];
        List<(int Num, int Gen)> crlsViejos = [];
        List<(int Num, int Gen)> ocspsViejos = [];
        string vriInnerViejo = "";

        if (refDssVieja is { } vieja)
        {
            string textoDssViejo = PdfSignaturePlaceholder.ObtenerTextoObjeto(texto, mapa[vieja.Num].Offset);
            certsViejos = PdfSignaturePlaceholder.BuscarArrayDeReferencias(textoDssViejo, "/Certs");
            crlsViejos = PdfSignaturePlaceholder.BuscarArrayDeReferencias(textoDssViejo, "/CRLs");
            ocspsViejos = PdfSignaturePlaceholder.BuscarArrayDeReferencias(textoDssViejo, "/OCSPs");
            vriInnerViejo = ExtraerVriInner(textoDssViejo);
        }

        int maxObjNum = mapa.Keys.Max();
        int siguienteNum = maxObjNum + 1;

        var objetosNuevos = new List<(int ObjNum, int Gen, string Texto)>();

        var refsCertsNuevos = new List<(int Num, int Gen)>();
        foreach (byte[] certDer in material.CadenaCertificadosDer)
        {
            int num = siguienteNum++;
            objetosNuevos.Add((num, 0, FormatearObjetoStream(num, certDer)));
            refsCertsNuevos.Add((num, 0));
        }

        var refsCrlNuevos = new List<(int Num, int Gen)>();
        if (material.CrlDer is { Length: > 0 } crlDer)
        {
            int num = siguienteNum++;
            objetosNuevos.Add((num, 0, FormatearObjetoStream(num, crlDer)));
            refsCrlNuevos.Add((num, 0));
        }

        var refsOcspNuevos = new List<(int Num, int Gen)>();
        if (material.OcspRespuestaDer is { Length: > 0 } ocspDer)
        {
            int num = siguienteNum++;
            objetosNuevos.Add((num, 0, FormatearObjetoStream(num, ocspDer)));
            refsOcspNuevos.Add((num, 0));
        }

        var certsTotales = certsViejos.Concat(refsCertsNuevos).ToList();
        var crlsTotales = crlsViejos.Concat(refsCrlNuevos).ToList();
        var ocspsTotales = ocspsViejos.Concat(refsOcspNuevos).ToList();

        string fechaVri = $"({PdfSignaturePlaceholder.FormatearFechaPdf(DateTimeOffset.UtcNow)})";
        string entradaVriNueva =
            $"/{claveVri} << /Cert [ {FormatearRefs(refsCertsNuevos)} ] /CRL [ {FormatearRefs(refsCrlNuevos)} ] /OCSP [ {FormatearRefs(refsOcspNuevos)} ] /TU {fechaVri} >>";

        int numDssNueva = siguienteNum++;
        string textoDssNueva =
            $"{numDssNueva} 0 obj\n<< /Type /DSS /Certs [ {FormatearRefs(certsTotales)} ] /CRLs [ {FormatearRefs(crlsTotales)} ] /OCSPs [ {FormatearRefs(ocspsTotales)} ] /VRI << {vriInnerViejo}{entradaVriNueva} >> >>\nendobj";
        objetosNuevos.Add((numDssNueva, 0, textoDssNueva));

        string textoCatalogoMutado = refDssVieja is { } vieja2
            ? ReemplazarReferencia(textoCatalogo, "/DSS", vieja2, (numDssNueva, 0))
            : InsertarClaveEnDiccionario(textoCatalogo, $"/DSS {numDssNueva} 0 R");
        objetosNuevos.Add((rootNum, rootGen, textoCatalogoMutado));

        var sb = new StringBuilder();
        var offsetsPorObjeto = new List<(int ObjNum, int Gen, long Offset)>();
        foreach (var (num, gen, textoObjeto) in objetosNuevos)
        {
            long offsetAbsoluto = longitudOriginal + sb.Length;
            sb.Append(textoObjeto);
            sb.Append('\n'); // separador — ver mismo motivo en PdfSignaturePlaceholder.PrepararConLectorPropio.
            offsetsPorObjeto.Add((num, gen, offsetAbsoluto));
        }
        byte[] bytesObjetos = Encoding.Latin1.GetBytes(sb.ToString());

        int maxObjNumFinal = Math.Max(maxObjNum, numDssNueva);
        long offsetXref = longitudOriginal + bytesObjetos.LongLength;
        byte[] bytesXrefTrailer = PdfSignaturePlaceholder.ConstruirXrefYTrailer(
            offsetsPorObjeto, maxObjNumFinal, (rootNum, rootGen), prevStartXref, offsetXref);

        byte[] resultado = new byte[longitudOriginal + bytesObjetos.LongLength + bytesXrefTrailer.LongLength];
        Buffer.BlockCopy(pdfFirmado, 0, resultado, 0, (int)longitudOriginal);
        Buffer.BlockCopy(bytesObjetos, 0, resultado, (int)longitudOriginal, bytesObjetos.Length);
        Buffer.BlockCopy(bytesXrefTrailer, 0, resultado, (int)(longitudOriginal + bytesObjetos.LongLength), bytesXrefTrailer.Length);
        return resultado;
    }

    /// <summary>Objeto stream binario mínimo (solo <c>/Length</c>) — el contenido es DER crudo, nunca hex-codificado (a diferencia de <c>/Contents</c> de la firma, aquí no hace falta interoperar con ese convenio).</summary>
    private static string FormatearObjetoStream(int objNum, byte[] contenido)
    {
        // Latin1 es biyectivo byte<->char en 0-255 — el mismo truco que usa
        // todo este archivo para tratar el PDF completo (incluido contenido
        // binario) como una sola cadena de texto sin perder ni un byte.
        string cuerpo = Encoding.Latin1.GetString(contenido);
        return $"{objNum} 0 obj\n<< /Length {contenido.Length} >>\nstream\n{cuerpo}\nendstream\nendobj";
    }

    private static string FormatearRefs(IReadOnlyList<(int Num, int Gen)> refs) =>
        string.Join(' ', refs.Select(r => $"{r.Num} {r.Gen} R"));

    private static string ReemplazarReferencia(string textoDict, string clave, (int Num, int Gen) vieja, (int Num, int Gen) nueva)
    {
        string tokenViejo = $"{clave} {vieja.Num} {vieja.Gen} R";
        string tokenNuevo = $"{clave} {nueva.Num} {nueva.Gen} R";
        if (!textoDict.Contains(tokenViejo, StringComparison.Ordinal))
            throw new InvalidOperationException($"No se encontró '{tokenViejo}' para reemplazar — ¿el catálogo no fue escrito por este mismo componente?");
        return textoDict.Replace(tokenViejo, tokenNuevo, StringComparison.Ordinal);
    }

    /// <summary>Inserta una entrada antes del cierre del PRIMER diccionario <c>&lt;&lt; ... &gt;&gt;</c> del texto — soporta diccionarios anidados (p. ej. si el objeto ya trae algo como <c>/Names &lt;&lt; ... &gt;&gt;</c> inline).</summary>
    private static string InsertarClaveEnDiccionario(string textoObjeto, string entrada)
    {
        int inicioDict = textoObjeto.IndexOf("<<", StringComparison.Ordinal);
        if (inicioDict < 0)
            throw new InvalidOperationException("El objeto no contiene ningún diccionario '<< ... >>'.");
        int posCierre = EncontrarCierreDiccionario(textoObjeto, inicioDict);
        return textoObjeto.Insert(posCierre, $" {entrada}");
    }

    /// <summary>Extrae el contenido INTERNO del diccionario `/VRI &lt;&lt; ... &gt;&gt;` de un DSS ya existente — cadena vacía si no hay `/VRI` (DSS recién creado por primera vez, no debería pasar aquí porque solo se llama cuando ya había un `/DSS`, pero se tolera igual).</summary>
    private static string ExtraerVriInner(string textoDssViejo)
    {
        int idxVri = textoDssViejo.IndexOf("/VRI", StringComparison.Ordinal);
        if (idxVri < 0) return "";
        int inicioDict = textoDssViejo.IndexOf("<<", idxVri, StringComparison.Ordinal);
        if (inicioDict < 0) return "";
        int posCierre = EncontrarCierreDiccionario(textoDssViejo, inicioDict);
        string inner = textoDssViejo.Substring(inicioDict + 2, posCierre - (inicioDict + 2)).Trim();
        return inner.Length > 0 ? inner + " " : "";
    }

    /// <summary>
    /// Encuentra la posición del <c>&gt;&gt;</c> que cierra el <c>&lt;&lt;</c>
    /// que empieza en <paramref name="posApertura"/>, contando profundidad —
    /// a diferencia de buscar el "último &gt;&gt;" a ciegas, esto es correcto
    /// aunque el diccionario contenga otros diccionarios anidados dentro.
    /// </summary>
    private static int EncontrarCierreDiccionario(string texto, int posApertura)
    {
        int p = posApertura + 2;
        int profundidad = 1;
        while (true)
        {
            int siguienteApertura = texto.IndexOf("<<", p, StringComparison.Ordinal);
            int siguienteCierre = texto.IndexOf(">>", p, StringComparison.Ordinal);
            if (siguienteCierre < 0)
                throw new InvalidOperationException("Diccionario sin '>>' de cierre.");

            if (siguienteApertura >= 0 && siguienteApertura < siguienteCierre)
            {
                profundidad++;
                p = siguienteApertura + 2;
            }
            else
            {
                profundidad--;
                p = siguienteCierre + 2;
                if (profundidad == 0) return siguienteCierre;
            }
        }
    }
}
