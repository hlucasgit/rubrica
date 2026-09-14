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
/// secciones 12.8, 12.10 y 12.11.
///
/// DISEÑO — actualización incremental real (ISO 32000-1 §7.5.6), no
/// reescritura completa: los bytes del archivo de entrada (0..N) se dejan
/// TOTALMENTE intactos; solo se AÑADEN al final los objetos nuevos/mutados
/// más una nueva sección xref clásica y un trailer con <c>/Prev</c>
/// apuntando al xref anterior. Esto es válido tanto si el archivo de entrada
/// nunca tuvo firma (primera firma) como si ya tenía una o más (de
/// SecureSign o de un tercero) — como esos bytes nunca se tocan, cualquier
/// /ByteRange previo sigue siendo válido.
///
/// Hay DOS implementaciones de la misma operación, elegidas por
/// <see cref="Preparar"/> según si el archivo YA tiene o no una
/// actualización incremental previa (ver <see cref="TieneRevisionPrevia"/>):
/// <see cref="PrepararConPdfSharpCore"/> usa el lector/escritor de
/// PdfSharpCore y SOLO es segura sobre la primera firma (un PDF de una sola
/// revisión, sin /Prev) — PdfSharpCore corrompe internamente el árbol de
/// páginas al REABRIR un archivo que ya tiene /Prev, sin importar qué se
/// llame después de abrirlo (ver RUNBOOK.md 12.11 para el detalle del bug).
/// <see cref="PrepararConLectorPropio"/> es un lector/escritor de texto PDF
/// propio y deliberadamente mínimo (sin dependencias externas) que solo
/// entiende el formato exacto que este mismo componente produce — se usa
/// para la segunda firma en adelante, y por diseño no tiene límite de
/// profundidad (documentos con hasta 20+ firmantes) — ver RUNBOOK.md 12.11.
/// </summary>
public static class PdfSignaturePlaceholder
{
    /// <summary>
    /// Bytes reservados para el CMS (firma + certificado(s) + atributos). Un
    /// certificado DNIe con su cadena completa más la firma RSA-2048 rara vez
    /// supera 8 KB — se deja bastante margen.
    /// </summary>
    private const int CapacidadCmsBytes = 16_000;

    /// <summary>Ancho fijo (en dígitos ASCII) de cada número del /ByteRange — ver comentario en <see cref="PrepararConPdfSharpCore"/>.</summary>
    private const int AnchoNumeroByteRange = 10;

    private static readonly string MarcadorByteRange =
        "[" + new string('0', AnchoNumeroByteRange) +
        " " + new string('0', AnchoNumeroByteRange) +
        " " + new string('0', AnchoNumeroByteRange) +
        " " + new string('0', AnchoNumeroByteRange) + "]";

    private static readonly string MarcadorContenido = "<" + new string('0', CapacidadCmsBytes * 2) + ">";

    public static ResultadoPreparacionPades Preparar(
        byte[] pdfActual,
        string nombreFirmante,
        string razon,
        DateTimeOffset momento)
    {
        if (TieneRevisionPrevia(pdfActual))
        {
            // Ver RUNBOOK.md 12.11: el archivo YA tiene al menos una
            // actualización incremental (una firma previa, nuestra o de un
            // formato clásico compatible) — PdfSharpCore corrompe el árbol
            // de páginas AL REABRIR un archivo así (lo hace internamente
            // dentro de PdfReader.Open, independientemente de qué se llame
            // después), así que a partir de la segunda firma se usa siempre
            // el lector propio, sin techo de profundidad.
            return PrepararConLectorPropio(pdfActual, nombreFirmante, razon, momento);
        }

        try
        {
            return PrepararConPdfSharpCore(pdfActual, nombreFirmante, razon, momento);
        }
        catch (LimitePdfSharpCoreException)
        {
            return PrepararConLectorPropio(pdfActual, nombreFirmante, razon, momento);
        }
    }

    /// <summary>
    /// True si el trailer más reciente del PDF ya tiene <c>/Prev</c> — es
    /// decir, si el archivo ya es el resultado de al menos una actualización
    /// incremental (típicamente, una firma previa). Ver comentario en
    /// <see cref="Preparar"/> sobre por qué esto decide la ruta a usar.
    /// </summary>
    private static bool TieneRevisionPrevia(byte[] pdf)
    {
        long xrefOffset = EncontrarUltimoStartXref(pdf);
        string texto = Encoding.Latin1.GetString(pdf);
        int idxTrailer = texto.IndexOf("trailer", (int)xrefOffset, StringComparison.Ordinal);
        if (idxTrailer < 0) return false;
        int idxFinTrailer = texto.IndexOf(">>", idxTrailer, StringComparison.Ordinal);
        if (idxFinTrailer < 0) return false;
        string textoTrailer = texto.Substring(idxTrailer, idxFinTrailer - idxTrailer);
        return textoTrailer.Contains("/Prev", StringComparison.Ordinal);
    }

    private sealed class LimitePdfSharpCoreException(Exception interna)
        : InvalidOperationException(
            "PdfSharpCore no pudo abrir este PDF de una sola revisión (sin firmas previas) — reintentando con el lector propio (RUNBOOK.md 12.11)...", interna);

    // ==================================================================
    //  RUTA PRINCIPAL — PdfSharpCore (fiable hasta ~3 revisiones acumuladas)
    // ==================================================================

    private static ResultadoPreparacionPades PrepararConPdfSharpCore(
        byte[] pdfActual,
        string nombreFirmante,
        string razon,
        DateTimeOffset momento)
    {
        long longitudOriginal = pdfActual.LongLength;
        long prevStartXref = EncontrarUltimoStartXref(pdfActual);

        // Esta ruta SOLO se llama (ver Preparar/TieneRevisionPrevia) cuando
        // el archivo de entrada todavía NO tiene ninguna actualización
        // incremental previa — o sea, la primera firma sobre un PDF recién
        // generado o ajeno de una sola revisión. En ese caso concreto
        // documento.Pages/documento.Internals.Catalog son seguros (el bug
        // real de PdfSharpCore que mezcla Página/Pages solo se dispara al
        // REABRIR un archivo que ya tiene /Prev — ver RUNBOOK.md 12.11).
        using var entrada = new MemoryStream(pdfActual);
        PdfDocument documento;
        try
        {
            documento = PdfReader.Open(entrada, PdfDocumentOpenMode.Modify);
        }
        catch (Exception ex)
        {
            throw new LimitePdfSharpCoreException(ex);
        }
        using var _ = documento;

        if (documento.PageCount == 0)
            throw new InvalidOperationException("El documento no tiene páginas.");
        PdfDictionary pagina = documento.Pages[0];
        PdfDictionary catalogo = documento.Internals.Catalog;

        // ---------- 1. Diccionario /Sig, con /ByteRange y /Contents reservados ----------
        //
        // El truco estándar (el mismo que usan Adobe/iText/PDFBox): reservar
        // un ANCHO FIJO de dígitos para el /ByteRange y de caracteres hex para
        // /Contents, serializar los objetos con esos huecos rellenos de ceros,
        // localizar sus offsets reales por búsqueda de bytes dentro de lo que
        // ACABAMOS de generar (nunca en el archivo original, que puede traer
        // cualquier contenido binario), y sobrescribir el /ByteRange in-place
        // con el mismo ancho, así el archivo no cambia de tamaño. El relleno
        // de ceros que sobra en /Contents tras el CMS real es inocuo: un
        // parser ASN.1/DER se detiene en la longitud que el propio CMS declara.
        var sigDict = new PdfDictionary(documento);
        documento.Internals.AddObject(sigDict);
        sigDict.Elements.SetName("/Type", "/Sig");
        sigDict.Elements.SetName("/Filter", "/Adobe.PPKLite");
        sigDict.Elements.SetName("/SubFilter", "/ETSI.CAdES.detached");
        sigDict.Elements.SetString("/Name", nombreFirmante);
        sigDict.Elements.SetString("/Reason", razon);
        sigDict.Elements.SetString("/M", FormatearFechaPdf(momento));
        sigDict.Elements["/ByteRange"] = new PdfLiteral(MarcadorByteRange);
        sigDict.Elements["/Contents"] = new PdfLiteral(MarcadorContenido);

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
        bool catalogoTocado = false;
        var acroForm = catalogo.Elements.GetDictionary("/AcroForm");
        if (acroForm is null)
        {
            acroForm = new PdfDictionary(documento);
            documento.Internals.AddObject(acroForm);
            acroForm.Elements.SetValue("/Fields", new PdfArray(documento));
            catalogo.Elements.SetReference("/AcroForm", acroForm);
            catalogoTocado = true;
        }
        acroForm.Elements.SetInteger("/SigFlags", 3); // SignaturesExist | AppendOnly
        var campos = acroForm.Elements.GetArray("/Fields");
        if (campos is null)
        {
            campos = new PdfArray(documento);
            acroForm.Elements.SetValue("/Fields", campos);
        }
        campos.Elements.Add(widget.Reference);

        // ---------- 4. Serializar SOLO los objetos nuevos/mutados ----------
        var objetosTocados = new List<PdfDictionary> { sigDict, widget, acroForm, pagina };
        if (catalogoTocado) objetosTocados.Add(catalogo);

        using var bufferObjetos = new MemoryStream();
        var offsetsPorObjeto = new List<(int ObjNum, int Gen, long Offset)>();
        foreach (var obj in objetosTocados)
        {
            long offsetRelativo = bufferObjetos.Length;
            documento.Internals.WriteObject(bufferObjetos, obj);
            bufferObjetos.WriteByte((byte)'\n');
            var id = obj.Reference.ObjectID;
            offsetsPorObjeto.Add((id.ObjectNumber, id.GenerationNumber, longitudOriginal + offsetRelativo));
        }
        byte[] bytesObjetos = bufferObjetos.ToArray();

        // ---------- 5. Localizar los marcadores DENTRO de lo recién generado ----------
        (int posContenidoRel, int posByteRangeRel) = LocalizarMarcadores(bytesObjetos);

        // ---------- 6. Construir la sección xref + trailer de esta revisión ----------
        //
        // El número de objeto del /Root NO se lee de catalogo.Reference cuando
        // el catálogo no se tocó en esta revisión: PdfSharpCore tiene un bug
        // real al re-resolver PdfDictionary.Reference para objetos que pasaron
        // por su mecanismo interno de "ascenso de tipo" (genérico -> PdfCatalog)
        // al reabrir un archivo que YA es una actualización incremental — el
        // objeto ascendido pierde su _iref original y termina con un número
        // arbitrario (se observó consistentemente "1", chocando con otro
        // objeto real). Comprobado con PdfReadAccuracy.Moderate también sin
        // efecto. Por eso, cuando reutilizamos un catálogo ya existente, el
        // /Root se relee directamente del texto del trailer anterior — que
        // SIEMPRE es el formato clásico que este mismo método produce.
        int maxObjNum = documento.Internals.GetAllObjects().Max(o => o.Reference.ObjectID.ObjectNumber);

        (int ObjNum, int Gen) root = catalogoTocado
            ? (catalogo.Reference.ObjectID.ObjectNumber, catalogo.Reference.ObjectID.GenerationNumber)
            : EncontrarRootDelTrailer(pdfActual, prevStartXref);

        long offsetXref = longitudOriginal + bytesObjetos.LongLength;
        byte[] bytesXrefTrailer = ConstruirXrefYTrailer(offsetsPorObjeto, maxObjNum, root, prevStartXref, offsetXref);

        return Ensamblar(pdfActual, bytesObjetos, bytesXrefTrailer, posContenidoRel, posByteRangeRel);
    }

    // ==================================================================
    //  RUTA DE RESPALDO — lector/escritor de texto PDF propio, sin techo
    //  de profundidad (ver RUNBOOK.md 12.11)
    // ==================================================================

    /// <summary>
    /// Igual que <see cref="PrepararConPdfSharpCore"/> pero SIN usar
    /// <c>PdfReader.Open</c> ni el modelo de objetos de PdfSharpCore para
    /// nada — ni para leer ni para escribir. Solo entiende el formato
    /// clásico exacto (xref + trailer en texto plano) que este mismo
    /// componente y PdfSharpCore producen, así que únicamente es seguro
    /// llamarlo sobre un archivo que ya pasó por aquí (nunca sobre un PDF
    /// de origen arbitrario) — <see cref="Preparar"/> solo lo usa como
    /// respaldo tras confirmar (por el fallo de PdfSharpCore) que se llegó
    /// a ese punto.
    /// </summary>
    private static ResultadoPreparacionPades PrepararConLectorPropio(
        byte[] pdfActual,
        string nombreFirmante,
        string razon,
        DateTimeOffset momento)
    {
        long longitudOriginal = pdfActual.LongLength;
        long prevStartXref = EncontrarUltimoStartXref(pdfActual);
        string texto = Encoding.Latin1.GetString(pdfActual);

        var mapa = CaminarCadenaXref(texto, prevStartXref, out int rootNum, out int rootGen);
        string textoCatalogo = ObtenerTextoObjeto(texto, mapa[rootNum].Offset);
        var refPages = BuscarReferencia(textoCatalogo, "/Pages")
            ?? throw new InvalidOperationException("El catálogo no tiene /Pages — no se puede continuar por el lector propio.");

        var (paginaNum, paginaGen) = ResolverPrimeraPagina(texto, mapa, refPages.Num, refPages.Gen, new HashSet<int>());
        string textoPagina = ObtenerTextoObjeto(texto, mapa[paginaNum].Offset);

        var refAcroForm = BuscarReferencia(textoCatalogo, "/AcroForm")
            ?? throw new InvalidOperationException(
                "El documento no tiene /AcroForm — el lector propio solo funciona sobre documentos que ya pasaron por aquí al menos una vez.");
        string textoAcroForm = ObtenerTextoObjeto(texto, mapa[refAcroForm.Num].Offset);

        int maxObjNum = mapa.Keys.Max();
        int numSig = maxObjNum + 1;
        int numWidget = maxObjNum + 2;

        string textoSigNuevo = FormatearObjetoSig(numSig, nombreFirmante, razon, momento);
        string textoWidgetNuevo = FormatearObjetoWidget(numWidget, numSig, paginaNum, paginaGen);
        string textoPaginaMutado = InsertarEnArray(textoPagina, "/Annots", $"{numWidget} 0 R");
        string textoAcroFormMutado = InsertarEnArray(textoAcroForm, "/Fields", $"{numWidget} 0 R");

        var partes = new (int ObjNum, int Gen, string Texto)[]
        {
            (numSig, 0, textoSigNuevo),
            (numWidget, 0, textoWidgetNuevo),
            (paginaNum, paginaGen, textoPaginaMutado),
            (refAcroForm.Num, refAcroForm.Gen, textoAcroFormMutado),
        };

        using var bufferObjetos = new MemoryStream();
        var offsetsPorObjeto = new List<(int ObjNum, int Gen, long Offset)>();
        foreach (var (objNum, gen, textoObjeto) in partes)
        {
            long offsetAbsoluto = longitudOriginal + bufferObjetos.Length;
            byte[] bytesObjeto = Encoding.ASCII.GetBytes(textoObjeto);
            bufferObjetos.Write(bytesObjeto, 0, bytesObjeto.Length);
            // ObtenerTextoObjeto corta justo tras "endobj", sin salto de
            // línea — hay que añadirlo explícitamente entre cada objeto: sin
            // esto, "endobj" del objeto N queda pegado al "N+1 0 obj" (o al
            // "xref" final) siguiente, produciendo un PDF que NUESTRO propio
            // lector (basado en offsets, no en tokens) sigue aceptando, pero
            // que cualquier parser estricto (Adobe, PdfSharpCore) rechaza.
            bufferObjetos.WriteByte((byte)'\n');
            offsetsPorObjeto.Add((objNum, gen, offsetAbsoluto));
        }
        byte[] bytesObjetos = bufferObjetos.ToArray();

        (int posContenidoRel, int posByteRangeRel) = LocalizarMarcadores(bytesObjetos);

        int nuevoMax = Math.Max(maxObjNum, numWidget);
        long offsetXref = longitudOriginal + bytesObjetos.LongLength;
        byte[] bytesXrefTrailer = ConstruirXrefYTrailer(offsetsPorObjeto, nuevoMax, (rootNum, rootGen), prevStartXref, offsetXref);

        return Ensamblar(pdfActual, bytesObjetos, bytesXrefTrailer, posContenidoRel, posByteRangeRel);
    }

    // ---------- Lector de texto PDF mínimo (solo lo que hace falta) ----------

    private sealed record EntradaXref(long Offset, int Gen);

    /// <summary>Recorre toda la cadena /Prev (sin límite de profundidad) y arma el mapa "número de objeto -> posición".</summary>
    private static Dictionary<int, EntradaXref> CaminarCadenaXref(string texto, long xrefInicial, out int rootObjNum, out int rootGen)
    {
        var mapa = new Dictionary<int, EntradaXref>();
        var visitados = new HashSet<long>();
        long xrefPos = xrefInicial;
        rootObjNum = -1;
        rootGen = 0;
        bool primero = true;

        while (true)
        {
            if (!visitados.Add(xrefPos))
                throw new InvalidOperationException($"Ciclo detectado en la cadena /Prev en el offset {xrefPos}.");

            int p = (int)xrefPos;
            if (p < 0 || p + 4 > texto.Length || texto.Substring(p, 4) != "xref")
                throw new InvalidOperationException($"No se encontró 'xref' en el offset {xrefPos}.");
            p += 4;

            while (true)
            {
                SaltarEspacios(texto, ref p);
                if (p + 7 <= texto.Length && texto.Substring(p, 7) == "trailer") { p += 7; break; }

                int start = LeerEntero(texto, ref p);
                SaltarEspacios(texto, ref p);
                int count = LeerEntero(texto, ref p);
                SaltarEspacios(texto, ref p);
                for (int i = 0; i < count; i++)
                {
                    string entrada = texto.Substring(p, 20);
                    long off = long.Parse(entrada.Substring(0, 10));
                    int gen = int.Parse(entrada.Substring(11, 5));
                    char tipo = entrada[17];
                    int objNum = start + i;
                    if (tipo == 'n' && !mapa.ContainsKey(objNum))
                        mapa[objNum] = new EntradaXref(off, gen);
                    p += 20;
                }
            }

            SaltarEspacios(texto, ref p);
            int finTrailer = texto.IndexOf(">>", p, StringComparison.Ordinal);
            if (finTrailer < 0) throw new InvalidOperationException("Trailer sin '>>' de cierre.");
            string textoTrailer = texto.Substring(p, finTrailer - p);

            if (primero)
            {
                var raiz = BuscarReferencia(textoTrailer, "/Root")
                    ?? throw new InvalidOperationException("El trailer no tiene /Root.");
                rootObjNum = raiz.Num;
                rootGen = raiz.Gen;
                primero = false;
            }

            int idxPrev = textoTrailer.IndexOf("/Prev", StringComparison.Ordinal);
            if (idxPrev < 0) break;
            int pp = idxPrev + 5;
            SaltarEspacios(textoTrailer, ref pp);
            xrefPos = LeerEntero(textoTrailer, ref pp);
        }

        return mapa;
    }

    private static string ObtenerTextoObjeto(string texto, long offset)
    {
        int p = (int)offset;
        int fin = texto.IndexOf("endobj", p, StringComparison.Ordinal);
        if (fin < 0)
            throw new InvalidOperationException($"No se encontró 'endobj' para el objeto en el offset {offset}.");
        return texto.Substring(p, fin + "endobj".Length - p);
    }

    private static (int Num, int Gen)? BuscarReferencia(string textoDict, string clave)
    {
        int idx = textoDict.IndexOf(clave, StringComparison.Ordinal);
        if (idx < 0) return null;
        int p = idx + clave.Length;
        SaltarEspacios(textoDict, ref p);
        int num = LeerEntero(textoDict, ref p);
        SaltarEspacios(textoDict, ref p);
        int gen = LeerEntero(textoDict, ref p);
        return (num, gen);
    }

    private static string? BuscarNombre(string textoDict, string clave)
    {
        int idx = textoDict.IndexOf(clave, StringComparison.Ordinal);
        if (idx < 0) return null;
        int p = idx + clave.Length;
        SaltarEspacios(textoDict, ref p);
        int inicio = p;
        // El valor es un Name y por tanto EMPIEZA con '/' (p. ej. "/Pages")
        // — hay que consumir esa barra inicial antes de buscar el siguiente
        // delimitador, o el bucle de abajo termina de inmediato al verla.
        if (p < textoDict.Length && textoDict[p] == '/') p++;
        while (p < textoDict.Length && !char.IsWhiteSpace(textoDict[p]) && textoDict[p] != '/' && textoDict[p] != '>') p++;
        return textoDict.Substring(inicio, p - inicio);
    }

    private static List<(int Num, int Gen)> BuscarArrayDeReferencias(string textoDict, string clave)
    {
        var resultado = new List<(int, int)>();
        int idx = textoDict.IndexOf(clave, StringComparison.Ordinal);
        if (idx < 0) return resultado;
        int inicio = textoDict.IndexOf('[', idx);
        int fin = textoDict.IndexOf(']', inicio);
        if (inicio < 0 || fin < 0) return resultado;

        int p = inicio + 1;
        while (p < fin)
        {
            SaltarEspacios(textoDict, ref p);
            if (p >= fin) break;
            int num = LeerEntero(textoDict, ref p);
            SaltarEspacios(textoDict, ref p);
            int gen = LeerEntero(textoDict, ref p);
            SaltarEspacios(textoDict, ref p);
            if (p < fin && textoDict[p] == 'R') p++;
            resultado.Add((num, gen));
        }
        return resultado;
    }

    /// <summary>Recorre /Pages -&gt; /Kids... hasta la primera hoja /Page — versión de texto plano de <c>FlattenPageTree</c>, sin el bug de PdfSharpCore.</summary>
    private static (int Num, int Gen) ResolverPrimeraPagina(string texto, Dictionary<int, EntradaXref> mapa, int objNum, int gen, HashSet<int> visitados)
    {
        if (!visitados.Add(objNum))
            throw new InvalidOperationException("Ciclo detectado recorriendo el árbol de páginas.");
        if (!mapa.TryGetValue(objNum, out var entrada))
            throw new InvalidOperationException($"El objeto {objNum} (referenciado en el árbol de páginas) no existe.");

        string textoNodo = ObtenerTextoObjeto(texto, entrada.Offset);
        if (BuscarNombre(textoNodo, "/Type") == "/Page")
            return (objNum, gen);

        foreach (var (n, g) in BuscarArrayDeReferencias(textoNodo, "/Kids"))
        {
            try { return ResolverPrimeraPagina(texto, mapa, n, g, visitados); }
            catch (InvalidOperationException) { /* probar el siguiente hijo */ }
        }
        throw new InvalidOperationException("No se encontró ninguna página hoja en el árbol de páginas.");
    }

    private static string InsertarEnArray(string textoDict, string clave, string nuevaReferenciaTexto)
    {
        int idx = textoDict.IndexOf(clave, StringComparison.Ordinal);
        if (idx < 0)
            throw new InvalidOperationException($"No se encontró '{clave}' para agregarle una entrada — se esperaba que ya existiera de una revisión anterior.");
        int apertura = textoDict.IndexOf('[', idx);
        int cierre = textoDict.IndexOf(']', apertura);
        if (apertura < 0 || cierre < 0)
            throw new InvalidOperationException($"'{clave}' no es un array válido.");
        return textoDict.Insert(cierre, $" {nuevaReferenciaTexto}");
    }

    private static void SaltarEspacios(string texto, ref int p)
    {
        while (p < texto.Length && char.IsWhiteSpace(texto[p])) p++;
    }

    private static int LeerEntero(string texto, ref int p)
    {
        int inicio = p;
        while (p < texto.Length && char.IsDigit(texto[p])) p++;
        if (p == inicio) throw new InvalidOperationException($"Se esperaba un número en la posición {inicio}.");
        return int.Parse(texto.Substring(inicio, p - inicio));
    }

    // ---------- Escritor de texto PDF mínimo (solo los dos tipos de objeto que crea) ----------

    private static string FormatearObjetoSig(int objNum, string nombreFirmante, string razon, DateTimeOffset momento) =>
        $"{objNum} 0 obj\n<<\n/Type /Sig\n/Filter /Adobe.PPKLite\n/SubFilter /ETSI.CAdES.detached\n" +
        $"/Name {FormatearCadenaUnicode(nombreFirmante)}\n/Reason {FormatearCadenaUnicode(razon)}\n" +
        $"/M {FormatearCadenaUnicode(FormatearFechaPdf(momento))}\n" +
        $"/ByteRange {MarcadorByteRange}\n/Contents {MarcadorContenido}\n>>\nendobj\n";

    private static string FormatearObjetoWidget(int objNum, int sigObjNum, int paginaObjNum, int paginaGen) =>
        $"{objNum} 0 obj\n<<\n/Type /Annot\n/Subtype /Widget\n/FT /Sig\n" +
        $"/T {FormatearCadenaUnicode($"Firma-{Guid.NewGuid():N}")}\n" +
        $"/V {sigObjNum} 0 R\n/P {paginaObjNum} {paginaGen} R\n/F 4\n/Rect [0 0 0 0]\n>>\nendobj\n";

    /// <summary>
    /// Codifica una cadena PDF como hexadecimal UTF-16BE con BOM
    /// (<c>&lt;FEFF...&gt;</c>) — válido para cualquier texto (nombres con
    /// tildes/ñ incluidos) sin tener que escapar paréntesis/backslash a mano
    /// ni preocuparse por la codificación de <see cref="Encoding.ASCII"/>
    /// usada para el resto del archivo.
    /// </summary>
    private static string FormatearCadenaUnicode(string valor)
    {
        byte[] utf16be = Encoding.BigEndianUnicode.GetBytes(valor);
        byte[] conBom = new byte[2 + utf16be.Length];
        conBom[0] = 0xFE;
        conBom[1] = 0xFF;
        Buffer.BlockCopy(utf16be, 0, conBom, 2, utf16be.Length);
        return "<" + Convert.ToHexString(conBom) + ">";
    }

    // ---------- Piezas compartidas por ambas rutas ----------

    private static (int PosContenidoRel, int PosByteRangeRel) LocalizarMarcadores(byte[] bytesObjetos)
    {
        byte[] bytesMarcadorContenido = Encoding.ASCII.GetBytes(MarcadorContenido);
        int posContenidoRel = BuscarBytes(bytesObjetos, bytesMarcadorContenido);
        if (posContenidoRel < 0)
            throw new InvalidOperationException("No se pudo ubicar el marcador de /Contents en los objetos serializados.");

        byte[] bytesMarcadorByteRange = Encoding.ASCII.GetBytes(MarcadorByteRange);
        int posByteRangeRel = BuscarBytes(bytesObjetos, bytesMarcadorByteRange);
        if (posByteRangeRel < 0)
            throw new InvalidOperationException("No se pudo ubicar el marcador de /ByteRange en los objetos serializados.");

        return (posContenidoRel, posByteRangeRel);
    }

    private static byte[] ConstruirXrefYTrailer(
        IReadOnlyList<(int ObjNum, int Gen, long Offset)> entradas, int maxObjNum, (int ObjNum, int Gen) root, long prevStartXref, long offsetXref)
    {
        var xref = new StringBuilder();
        xref.Append("xref\n");
        foreach (var (objNum, gen, offset) in entradas.OrderBy(e => e.ObjNum))
            xref.Append($"{objNum} 1\n{offset:D10} {gen:D5} n \n");
        xref.Append("trailer\n<< /Size ").Append(maxObjNum + 1)
            .Append(" /Root ").Append(root.ObjNum).Append(' ').Append(root.Gen).Append(" R")
            .Append(" /Prev ").Append(prevStartXref)
            .Append(" >>\nstartxref\n").Append(offsetXref).Append("\n%%EOF");
        return Encoding.ASCII.GetBytes(xref.ToString());
    }

    private static ResultadoPreparacionPades Ensamblar(
        byte[] pdfActual, byte[] bytesObjetos, byte[] bytesXrefTrailer, int posContenidoRel, int posByteRangeRel)
    {
        long longitudOriginal = pdfActual.LongLength;

        byte[] pdfConHueco = new byte[longitudOriginal + bytesObjetos.LongLength + bytesXrefTrailer.LongLength];
        Buffer.BlockCopy(pdfActual, 0, pdfConHueco, 0, (int)longitudOriginal);
        Buffer.BlockCopy(bytesObjetos, 0, pdfConHueco, (int)longitudOriginal, bytesObjetos.Length);
        Buffer.BlockCopy(bytesXrefTrailer, 0, pdfConHueco, (int)(longitudOriginal + bytesObjetos.LongLength), bytesXrefTrailer.Length);

        int posContenido = (int)longitudOriginal + posContenidoRel;
        int posByteRange = (int)longitudOriginal + posByteRangeRel;
        int offsetHexInicio = posContenido + 1; // saltar el '<'
        int longitudHex = CapacidadCmsBytes * 2;
        int offsetHexFin = offsetHexInicio + longitudHex; // posición del '>'

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
        byte[] bytesMarcadorByteRange = Encoding.ASCII.GetBytes(MarcadorByteRange);
        if (byteRangeFinal.Length != bytesMarcadorByteRange.Length)
            throw new InvalidOperationException("El /ByteRange calculado no cabe en el ancho reservado (documento demasiado grande).");

        Array.Copy(byteRangeFinal, 0, pdfConHueco, posByteRange, byteRangeFinal.Length);

        int longitudSegundoTramo = (int)(longitudTotal - offsetHexFin);
        byte[] contenidoCubierto = new byte[offsetHexInicio + longitudSegundoTramo];
        Buffer.BlockCopy(pdfConHueco, 0, contenidoCubierto, 0, offsetHexInicio);
        Buffer.BlockCopy(pdfConHueco, offsetHexFin, contenidoCubierto, offsetHexInicio, longitudSegundoTramo);

        return new ResultadoPreparacionPades(pdfConHueco, contenidoCubierto, offsetHexInicio, longitudHex);
    }

    /// <summary>Sustituye el hueco de /Contents por el CMS ya firmado (el resto queda relleno de ceros — ver comentario arriba).</summary>
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

    /// <summary>
    /// Busca, desde el final del archivo, el último <c>startxref</c> — el
    /// punto de entrada universal de CUALQUIER PDF válido (clásico o con
    /// flujos de referencias cruzadas de PDF 1.5+), que es donde esta nueva
    /// revisión debe enlazar su propio <c>/Prev</c>.
    /// </summary>
    private static long EncontrarUltimoStartXref(byte[] pdf)
    {
        byte[] marcador = Encoding.ASCII.GetBytes("startxref");
        int pos = -1;
        for (int i = pdf.Length - marcador.Length; i >= 0; i--)
        {
            bool coincide = true;
            for (int j = 0; j < marcador.Length && coincide; j++)
                coincide = pdf[i + j] == marcador[j];
            if (coincide) { pos = i; break; }
        }
        if (pos < 0)
            throw new InvalidOperationException("El PDF de entrada no tiene ningún 'startxref' — no parece un PDF válido.");

        int inicio = pos + marcador.Length;
        while (inicio < pdf.Length && (pdf[inicio] == (byte)'\r' || pdf[inicio] == (byte)'\n' || pdf[inicio] == (byte)' ')) inicio++;
        int fin = inicio;
        while (fin < pdf.Length && pdf[fin] >= (byte)'0' && pdf[fin] <= (byte)'9') fin++;

        string numero = Encoding.ASCII.GetString(pdf, inicio, fin - inicio);
        return long.Parse(numero);
    }

    /// <summary>
    /// Lee <c>/Root N G R</c> directamente del texto del trailer clásico que
    /// empieza en <paramref name="xrefOffset"/> — ver el comentario extenso
    /// en el llamador sobre por qué esto NO se lee vía PdfSharpCore aquí.
    /// Solo se usa sobre archivos que este mismo método produjo (siempre
    /// trailer clásico en texto plano), nunca sobre un PDF de origen arbitrario.
    /// </summary>
    private static (int ObjNum, int Gen) EncontrarRootDelTrailer(byte[] pdf, long xrefOffset)
    {
        string texto = Encoding.ASCII.GetString(pdf, (int)xrefOffset, pdf.Length - (int)xrefOffset);
        int idxTrailer = texto.IndexOf("trailer", StringComparison.Ordinal);
        int idxRoot = texto.IndexOf("/Root", idxTrailer, StringComparison.Ordinal);
        if (idxTrailer < 0 || idxRoot < 0)
            throw new InvalidOperationException("No se pudo ubicar '/Root' en el trailer anterior — ¿el PDF de entrada no fue producido por este mismo método?");

        int i = idxRoot + "/Root".Length;
        while (char.IsWhiteSpace(texto[i])) i++;
        int finObjNum = i;
        while (char.IsDigit(texto[finObjNum])) finObjNum++;
        int objNum = int.Parse(texto.Substring(i, finObjNum - i));

        int j = finObjNum;
        while (char.IsWhiteSpace(texto[j])) j++;
        int finGen = j;
        while (char.IsDigit(texto[finGen])) finGen++;
        int gen = int.Parse(texto.Substring(j, finGen - j));

        return (objNum, gen);
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
