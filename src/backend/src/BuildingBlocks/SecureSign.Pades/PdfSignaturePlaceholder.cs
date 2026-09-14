using System.Security.Cryptography;
using System.Text;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;

namespace SecureSign.Pades;

/// <param name="PdfConHueco">
/// El PDF completo, ya con el diccionario /Sig y el widget de firma
/// incrustados, pero con /Contents relleno de ceros — listo para calcular el
/// hash y, una vez firmado, para <see cref="PdfSignaturePlaceholder.Inyectar"/>.
/// </param>
/// <param name="ContenidoCubierto">
/// Los bytes exactos que el /ByteRange declara como firmados (todo el
/// archivo EXCEPTO los dígitos hexadecimales de /Contents) — esto es lo que
/// hay que pasarle a <see cref="CmsBuilder"/> como contenido a firmar/hashear,
/// nunca <paramref name="PdfConHueco"/> completo.
/// </param>
public sealed record ResultadoPreparacionPades(
    byte[] PdfConHueco,
    byte[] ContenidoCubierto,
    int OffsetHexContenido,
    int LongitudHexReservada);

/// <summary>
/// Construye el "hueco" de firma PAdES (diccionario /Sig con /ByteRange y
/// /Contents reservados, más el widget y el AcroForm que lo referencian) e,
/// tras obtener la firma CMS real, la inyecta en ese hueco — ver RUNBOOK.md
/// sección 12.8 para el porqué de esta pieza (antes solo guardábamos la
/// firma aparte, nunca incrustada en el PDF).
///
/// DISEÑO: en vez de reimplementar a mano el parseo de xref/trailer del PDF
/// original para hacer una actualización incremental "de verdad", se
/// construyen los objetos de firma con PdfSharpCore (que sí sabe leer/escribir
/// PDF correctamente) y se deja que <c>PdfDocument.Save()</c> reescriba el
/// archivo completo UNA sola vez, ya con esos objetos incluidos desde el
/// principio. Esto es válido para una firma única (nuestro caso: cada
/// llamada al Firmador Local firma un flujo) — no es una actualización
/// incremental en el sentido estricto de ISO 32000 (revisión N sobre N-1),
/// así que si un documento ya trae una firma PAdES de un firmante anterior,
/// una segunda pasada por aquí invalidaría el /ByteRange de esa firma previa
/// (los offsets cambian al reescribir). Ver limitación documentada en RUNBOOK.
/// </summary>
public static class PdfSignaturePlaceholder
{
    /// <summary>
    /// Bytes reservados para el CMS (firma + certificado(s) + atributos). Un
    /// certificado DNIe con su cadena completa más la firma RSA-2048 rara vez
    /// supera 8 KB — se deja bastante margen.
    /// </summary>
    private const int CapacidadCmsBytes = 16_000;

    /// <summary>Ancho fijo (en dígitos ASCII) de cada número del /ByteRange — ver comentario en <see cref="Preparar"/>.</summary>
    private const int AnchoNumeroByteRange = 10;

    public static ResultadoPreparacionPades Preparar(
        byte[] pdfOriginal,
        string nombreFirmante,
        string razon,
        DateTimeOffset momento)
    {
        using var entrada = new MemoryStream(pdfOriginal);
        using var documento = PdfReader.Open(entrada, PdfDocumentOpenMode.Modify);

        if (documento.PageCount == 0)
            throw new InvalidOperationException("El documento no tiene páginas.");

        var pagina = documento.Pages[0];

        // ---------- 1. Diccionario /Sig, con /ByteRange y /Contents reservados ----------
        //
        // El truco estándar (el mismo que usan Adobe/iText/PDFBox): reservar
        // un ANCHO FIJO de dígitos para el /ByteRange y de caracteres hex para
        // /Contents, guardar el PDF UNA vez con esos huecos rellenos de ceros,
        // localizar sus offsets reales por búsqueda de bytes en el archivo ya
        // guardado, y sobrescribir ESOS MISMOS BYTES en el mismo sitio (sin
        // cambiar el tamaño del archivo, así los offsets no se corren). Los
        // ceros son enteros PDF válidos con ceros a la izquierda, y el
        // relleno de ceros que sobra en /Contents tras el CMS real es
        // inocuo: un parser ASN.1/DER se detiene en la longitud declarada
        // dentro del propio CMS, ignorando el resto.
        string marcadorByteRange =
            "[" + new string('0', AnchoNumeroByteRange) +
            " " + new string('0', AnchoNumeroByteRange) +
            " " + new string('0', AnchoNumeroByteRange) +
            " " + new string('0', AnchoNumeroByteRange) + "]";
        string marcadorContenido = "<" + new string('0', CapacidadCmsBytes * 2) + ">";

        var sigDict = new PdfDictionary(documento);
        documento.Internals.AddObject(sigDict);
        sigDict.Elements.SetName("/Type", "/Sig");
        sigDict.Elements.SetName("/Filter", "/Adobe.PPKLite");
        sigDict.Elements.SetName("/SubFilter", "/ETSI.CAdES.detached");
        sigDict.Elements.SetString("/Name", nombreFirmante);
        sigDict.Elements.SetString("/Reason", razon);
        sigDict.Elements.SetString("/M", FormatearFechaPdf(momento));
        sigDict.Elements["/ByteRange"] = new PdfLiteral(marcadorByteRange);
        sigDict.Elements["/Contents"] = new PdfLiteral(marcadorContenido);

        // ---------- 2. Widget de firma (anotación invisible) ----------
        //
        // El recuadro VISIBLE con el nombre/fecha/código de verificación ya
        // lo dibuja EstampadorVisualDocumentoPdf como contenido normal de
        // página (ver IEstampadorVisualDocumento) — este widget solo existe
        // para que el /Sig tenga un campo de formulario que lo referencie,
        // como exige el AcroForm; por eso su /Rect es un punto (invisible).
        var widget = new PdfDictionary(documento);
        documento.Internals.AddObject(widget);
        widget.Elements.SetName("/Type", "/Annot");
        widget.Elements.SetName("/Subtype", "/Widget");
        widget.Elements.SetName("/FT", "/Sig");
        widget.Elements.SetString("/T", $"Firma-{Guid.NewGuid():N}");
        widget.Elements.SetReference("/V", sigDict);
        widget.Elements.SetReference("/P", pagina);
        widget.Elements.SetInteger("/F", 4); // bit 3 = Print
        widget.Elements.SetRectangle("/Rect", new PdfRectangle(new XPoint(0, 0), new XPoint(0, 0)));

        var annots = pagina.Elements.GetArray("/Annots");
        if (annots is null)
        {
            annots = new PdfArray(documento);
            pagina.Elements.SetValue("/Annots", annots);
        }
        annots.Elements.Add(widget.Reference);

        // ---------- 3. AcroForm en el catálogo (crear o reutilizar) ----------
        var catalogo = documento.Internals.Catalog;
        var acroForm = catalogo.Elements.GetDictionary("/AcroForm");
        if (acroForm is null)
        {
            acroForm = new PdfDictionary(documento);
            documento.Internals.AddObject(acroForm);
            acroForm.Elements.SetValue("/Fields", new PdfArray(documento));
            catalogo.Elements.SetReference("/AcroForm", acroForm);
        }
        acroForm.Elements.SetInteger("/SigFlags", 3); // SignaturesExist | AppendOnly
        var campos = acroForm.Elements.GetArray("/Fields");
        if (campos is null)
        {
            campos = new PdfArray(documento);
            acroForm.Elements.SetValue("/Fields", campos);
        }
        campos.Elements.Add(widget.Reference);

        using var salida = new MemoryStream();
        documento.Save(salida);
        byte[] pdfConHueco = salida.ToArray();

        // ---------- 4. Localizar los marcadores en el archivo ya guardado ----------
        byte[] bytesMarcadorContenido = Encoding.ASCII.GetBytes(marcadorContenido);
        int posContenido = BuscarBytes(pdfConHueco, bytesMarcadorContenido);
        if (posContenido < 0)
            throw new InvalidOperationException("No se pudo ubicar el marcador de /Contents tras guardar el PDF (¿PdfSharpCore cambió su formato de escritura?).");

        int offsetHexInicio = posContenido + 1; // saltar el '<'
        int longitudHex = CapacidadCmsBytes * 2;
        int offsetHexFin = offsetHexInicio + longitudHex; // posición del '>'

        byte[] bytesMarcadorByteRange = Encoding.ASCII.GetBytes(marcadorByteRange);
        int posByteRange = BuscarBytes(pdfConHueco, bytesMarcadorByteRange);
        if (posByteRange < 0)
            throw new InvalidOperationException("No se pudo ubicar el marcador de /ByteRange tras guardar el PDF.");

        long longitudTotal = pdfConHueco.LongLength;
        long[] valores = { 0, offsetHexInicio, offsetHexFin, longitudTotal - offsetHexFin };

        var byteRangeTexto = new StringBuilder("[");
        for (int i = 0; i < valores.Length; i++)
        {
            if (i > 0) byteRangeTexto.Append(' ');
            byteRangeTexto.Append(valores[i].ToString().PadLeft(AnchoNumeroByteRange, '0'));
        }
        byteRangeTexto.Append(']');

        byte[] byteRangeFinal = Encoding.ASCII.GetBytes(byteRangeTexto.ToString());
        if (byteRangeFinal.Length != bytesMarcadorByteRange.Length)
            throw new InvalidOperationException("El /ByteRange calculado no cabe en el ancho reservado (documento demasiado grande).");

        Array.Copy(byteRangeFinal, 0, pdfConHueco, posByteRange, byteRangeFinal.Length);

        // ---------- 5. Extraer los bytes exactamente cubiertos por el ByteRange ----------
        int longitudSegundoTramo = (int)(longitudTotal - offsetHexFin);
        byte[] contenidoCubierto = new byte[offsetHexInicio + longitudSegundoTramo];
        Buffer.BlockCopy(pdfConHueco, 0, contenidoCubierto, 0, offsetHexInicio);
        Buffer.BlockCopy(pdfConHueco, offsetHexFin, contenidoCubierto, offsetHexInicio, longitudSegundoTramo);

        return new ResultadoPreparacionPades(pdfConHueco, contenidoCubierto, offsetHexInicio, longitudHex);
    }

    /// <summary>Sustituye el hueco de /Contents por el CMS ya firmado (el resto queda relleno de ceros — ver comentario en <see cref="Preparar"/>).</summary>
    public static byte[] Inyectar(ResultadoPreparacionPades preparado, byte[] cms)
    {
        if (cms.Length * 2 > preparado.LongitudHexReservada)
            throw new InvalidOperationException(
                $"La firma CMS ({cms.Length} bytes) excede la capacidad reservada en el PDF ({preparado.LongitudHexReservada / 2} bytes).");

        byte[] resultado = (byte[])preparado.PdfConHueco.Clone();
        byte[] hexBytes = Encoding.ASCII.GetBytes(Convert.ToHexString(cms));
        Array.Copy(hexBytes, 0, resultado, preparado.OffsetHexContenido, hexBytes.Length);
        return resultado;
    }

    private static string FormatearFechaPdf(DateTimeOffset momento)
    {
        // Formato de fecha PDF: D:YYYYMMDDHHmmSSOHH'mm' — ver ISO 32000-1 §7.9.4.
        string signo = momento.Offset < TimeSpan.Zero ? "-" : "+";
        var desplazamiento = momento.Offset.Duration();
        return $"D:{momento:yyyyMMddHHmmss}{signo}{desplazamiento.Hours:D2}'{desplazamiento.Minutes:D2}'";
    }

    private static int BuscarBytes(byte[] haystack, byte[] needle)
    {
        for (int i = 0; i <= haystack.Length - needle.Length; i++)
        {
            int j = 0;
            while (j < needle.Length && haystack[i + j] == needle[j]) j++;
            if (j == needle.Length) return i;
        }
        return -1;
    }
}
