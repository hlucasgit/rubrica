using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using Org.BouncyCastle.Cms;
using Org.BouncyCastle.Tsp;

namespace SecureSign.Pades;

/// <summary>
/// Resultado de verificar UNA firma /Sig dentro de un PDF — nunca solo
/// verdadero/falso, siempre con el detalle suficiente para auditoría (ver
/// informe de preauditoría INDECOPI/IOFE, hallazgo P0-05: "el resultado
/// nunca debería limitarse a true/false").
/// </summary>
/// <param name="InstanteFirmaDeclarado">
/// El valor de /M de este /Sig, si se pudo parsear — el instante que el
/// FIRMANTE declara haber firmado. NO está probado por una autoridad de
/// sellado de tiempo — es la mejor aproximación disponible cuando no hay un
/// sello de tiempo real embebido (ver <see cref="TokenTsaDer"/>), y aun
/// cuando lo hay, SecureSign.Validator decide cuál de los dos usar.
/// </param>
/// <param name="TokenTsaDer">
/// El TimeStampToken RFC 3161 embebido en el CMS (DER-encoded), si lo hay —
/// ver SecureSign.Pades.CmsBuilder.AgregarSelloTiempo/ExtraerSelloTiempo y
/// RUNBOOK.md 12.14. null si esta firma es solo PAdES-B (sin sello de
/// tiempo). Que exista NO implica por sí solo que sea confiable — eso lo
/// decide SecureSign.Tsa.VerificadorTokenTsa.
/// </param>
/// <param name="CmsDer">
/// Los bytes DER exactos del CMS embebido en /Contents — los mismos que ya
/// se recortaron por longitud DER real (ver <see cref="RecortarPorLongitudDer"/>)
/// para poder parsearlo, nunca el hueco completo relleno de ceros. Los
/// necesita <c>PdfDssWriter</c> para calcular la clave VRI de PAdES-LT
/// (SHA-1 del valor exacto de /Contents, ISO 32000-2 §12.8.4.3, RUNBOOK.md
/// 12.24) — se reutilizan en vez de volver a parsear el PDF desde cero.
/// null cuando <see cref="EsSelloDeArchivo"/> es true (el token ya vive en
/// <see cref="TokenTsaDer"/>; no hay VRI que calcular para un sello, así que
/// nada lo necesita ahí).
/// </param>
/// <param name="EsSelloDeArchivo">
/// true si esta entrada es un sello de tiempo de ARCHIVO (PAdES-LTA,
/// <c>/Type /DocTimeStamp</c>, <c>/SubFilter /ETSI.RFC3161</c> — RUNBOOK.md
/// 12.24), no una firma de un firmante real. Cuando es true: <see cref="Certificado"/>
/// es el certificado de la TSA (no de un firmante), <see cref="InstanteFirmaDeclarado"/>
/// es el <c>genTime</c> del propio token (no un /M autodeclarado), y
/// <see cref="Valido"/> exige tanto que la firma interna del token sea
/// consistente como que su <c>messageImprint</c> coincida con el contenido
/// cubierto — nunca se interpreta como una firma de persona (ver
/// SecureSign.Validator.ValidadorDocumentoPades, que separa estas entradas
/// de la lista de firmas).
/// </param>
public sealed record ResultadoVerificacionPades(
    bool Valido,
    string? NombreFirma,
    X509Certificate2? Certificado,
    DateTimeOffset? InstanteFirmaDeclarado,
    byte[]? TokenTsaDer,
    string? Error,
    byte[]? CmsDer = null,
    bool EsSelloDeArchivo = false);

/// <summary>
/// Verifica, de forma completamente independiente de <see cref="PdfSignaturePlaceholder"/>
/// y <see cref="CmsBuilder"/> (no reutiliza ningún estado ni asume que el PDF
/// fue producido por este mismo sistema), que un PDF trae una firma PAdES
/// real y criptográficamente válida: recalcula el hash sobre el
/// <c>/ByteRange</c> declarado y verifica el CMS/CAdES embebido en
/// <c>/Contents</c> contra su propio certificado.
///
/// NO valida vigencia, revocación ni cadena de confianza IOFE — eso es
/// responsabilidad de SecureSign.Trust. Esto solo responde: "¿esta firma es
/// matemáticamente consistente con el documento y el certificado que trae?".
/// </summary>
public static class PdfSignatureVerifier
{
    /// <summary>
    /// Un PDF puede tener más de un /Sig (firmas previas del documento
    /// original, o de firmantes anteriores) — se devuelven todas, en el
    /// orden en que aparecen en el archivo, para que el llamador decida
    /// (normalmente interesa la ÚLTIMA, la que SecureSign acaba de agregar).
    /// </summary>
    public static IReadOnlyList<ResultadoVerificacionPades> VerificarTodas(byte[] pdf)
    {
        string texto = Encoding.Latin1.GetString(pdf);
        var resultados = new List<ResultadoVerificacionPades>();

        int buscarDesde = 0;
        while (true)
        {
            int idxByteRange = texto.IndexOf("/ByteRange", buscarDesde, StringComparison.Ordinal);
            if (idxByteRange < 0) break;
            buscarDesde = idxByteRange + 1;

            resultados.Add(VerificarUna(pdf, texto, idxByteRange));
        }

        return resultados;
    }

    /// <summary>La última firma del archivo (normalmente la más reciente) — ver limitación de reescritura completa en RUNBOOK.md 12.8.</summary>
    public static ResultadoVerificacionPades VerificarUltima(byte[] pdf)
    {
        var todas = VerificarTodas(pdf);
        return todas.Count == 0
            ? new ResultadoVerificacionPades(false, null, null, null, null,"El documento no contiene ningún diccionario /Sig con /ByteRange.")
            : todas[^1];
    }

    private static ResultadoVerificacionPades VerificarUna(byte[] pdf, string texto, int idxByteRange)
    {
        try
        {
            int inicioArray = texto.IndexOf('[', idxByteRange);
            int finArray = texto.IndexOf(']', inicioArray);
            if (inicioArray < 0 || finArray < 0)
                return new ResultadoVerificacionPades(false, null, null, null, null,"No se pudo parsear el array de /ByteRange.");

            long[] br = Array.ConvertAll(
                texto.Substring(inicioArray + 1, finArray - inicioArray - 1).Split(' ', StringSplitOptions.RemoveEmptyEntries),
                long.Parse);
            if (br.Length != 4)
                return new ResultadoVerificacionPades(false, null, null, null, null,$"/ByteRange tiene {br.Length} valores, se esperaban 4.");

            byte[] tramoA = pdf[(int)br[0]..(int)(br[0] + br[1])];
            byte[] tramoB = pdf[(int)br[2]..(int)(br[2] + br[3])];
            byte[] contenidoCubierto = new byte[tramoA.Length + tramoB.Length];
            Buffer.BlockCopy(tramoA, 0, contenidoCubierto, 0, tramoA.Length);
            Buffer.BlockCopy(tramoB, 0, contenidoCubierto, tramoA.Length, tramoB.Length);

            int idxContents = texto.IndexOf("/Contents", idxByteRange, StringComparison.Ordinal);
            if (idxContents < 0)
                return new ResultadoVerificacionPades(false, null, null, null, null,"No se encontró /Contents asociado a este /ByteRange.");

            int inicioHex = texto.IndexOf('<', idxContents) + 1;
            int finHex = texto.IndexOf('>', inicioHex);
            if (inicioHex <= 0 || finHex < 0)
                return new ResultadoVerificacionPades(false, null, null, null, null, "No se pudo parsear el valor hexadecimal de /Contents.");

            // El campo /Contents reserva un ANCHO FIJO de dígitos hex (ver
            // PdfSignaturePlaceholder.CapacidadCmsBytes) y el CMS real solo
            // ocupa el principio — el resto queda relleno de ceros. NO se
            // puede recuperar dónde termina el CMS real recortando ceros
            // finales del texto (TrimEnd('0') — enfoque usado antes): un CMS
            // real cuyo último byte termina en un nibble '0' (1 de cada 16,
            // en la práctica ~1 de cada 15-20 firmas en documentos con
            // varios firmantes) pierde esos bytes genuinos y deja de
            // analizarse — bug real encontrado por la suite de pruebas
            // automatizada, ver RUNBOOK.md 12.15. El CMS es DER, así que su
            // propia cabecera de longitud (ISO/IEC 8825-1 §8.1.3) dice
            // exactamente cuántos bytes ocupa — se usa eso, nunca una
            // heurística sobre el texto.
            byte[] bytesReservados;
            try { bytesReservados = Convert.FromHexString(texto.Substring(inicioHex, finHex - inicioHex)); }
            catch (FormatException) { return new ResultadoVerificacionPades(false, null, null, null, null, "El valor de /Contents no es hexadecimal válido."); }

            if (bytesReservados.Length == 0 || bytesReservados[0] == 0x00)
                return new ResultadoVerificacionPades(false, null, null, null, null, "/Contents está vacío — no es una firma real, es un campo sin firmar.");

            byte[]? cmsRecortado = RecortarPorLongitudDer(bytesReservados);
            if (cmsRecortado is null)
                return new ResultadoVerificacionPades(false, null, null, null, null, "El contenido de /Contents no tiene una estructura DER válida (no es un CMS reconocible).");

            byte[] cms = cmsRecortado;

            // PAdES-LTA (RUNBOOK.md 12.24): un /DocTimeStamp trae en /Contents
            // un TimeStampToken RFC 3161, no un CMS de firma sobre
            // contenidoCubierto — tratarlo como si lo fuera daría "message-
            // digest inválido" siempre (el mensaje que realmente firmó la TSA
            // es el TSTInfo, no nuestro contenido directamente). Se detecta
            // ANTES de intentar CmsSignedData, por /SubFilter.
            if (ExtraerNombrePdf(texto, "/SubFilter", idxContents) == "ETSI.RFC3161")
                return VerificarSelloDeArchivo(contenidoCubierto, cms);

            var datosFirmados = new CmsSignedData(new CmsProcessableByteArray(contenidoCubierto), cms);
            var firmantes = datosFirmados.GetSignerInfos().GetSigners();
            if (firmantes.Count == 0)
                return new ResultadoVerificacionPades(false, null, null, null, null, "El CMS no contiene ningún SignerInfo.");

            var firmante = firmantes.First();
            var certificadosBc = datosFirmados.GetCertificates().EnumerateMatches(firmante.SignerID);
            var certificadoBc = certificadosBc.FirstOrDefault()
                ?? throw new InvalidOperationException("El CMS no incluye el certificado del firmante.");

            bool valido = firmante.Verify(certificadoBc);
            var certificadoNet = new X509Certificate2(certificadoBc.GetEncoded());

            // /Name y /M se codifican como cadena literal "(...)" cuando el
            // objeto lo escribió PdfSharpCore (primera firma) o como hex
            // UTF-16BE con BOM "<FEFF...>" cuando lo escribió el lector
            // propio (segunda firma en adelante — ver
            // PdfSignaturePlaceholder.FormatearCadenaUnicode) — hay que
            // entender ambos formatos, no solo el literal.
            string? nombreFirma = ExtraerCadenaPdf(texto, "/Name", idxContents);
            DateTimeOffset? instanteFirma = ParsearFechaPdf(ExtraerCadenaPdf(texto, "/M", idxContents));
            byte[]? tokenTsa = CmsBuilder.ExtraerSelloTiempo(cms);

            return valido
                ? new ResultadoVerificacionPades(true, nombreFirma, certificadoNet, instanteFirma, tokenTsa, null, cms)
                : new ResultadoVerificacionPades(false, nombreFirma, certificadoNet, instanteFirma, tokenTsa, "El CMS no verifica contra su propio certificado (message-digest o firma inválida).", cms);
        }
        catch (Exception ex)
        {
            return new ResultadoVerificacionPades(false, null, null, null, null,$"Error al procesar esta firma: {ex.Message}");
        }
    }

    /// <summary>
    /// Extrae el valor de una clave de cadena PDF (p. ej. <c>/Name</c> o
    /// <c>/M</c>) buscando hacia atrás desde <paramref name="limiteSuperior"/>
    /// (normalmente el offset de <c>/Contents</c> del mismo /Sig) — entiende
    /// tanto la forma literal <c>(...)</c> como la hexadecimal
    /// <c>&lt;FEFF...&gt;</c> (UTF-16BE con BOM) que usa el lector propio.
    /// </summary>
    /// <summary>
    /// Recorta un buffer que empieza con un valor DER (aquí, siempre un CMS
    /// ContentInfo — una SEQUENCE) a su longitud REAL, leyendo su propia
    /// cabecera de longitud (ISO/IEC 8825-1 §8.1.3: forma corta de un solo
    /// byte &lt;0x80, o forma larga 0x80|N seguida de N bytes big-endian) en
    /// vez de adivinar dónde termina por el contenido — ver comentario en el
    /// llamador. Devuelve null si no parece un DER válido (tag distinto de
    /// SEQUENCE, longitud indefinida/no soportada, o longitud declarada que
    /// excede el buffer disponible).
    /// </summary>
    private static byte[]? RecortarPorLongitudDer(byte[] buffer)
    {
        if (buffer.Length < 2 || buffer[0] != 0x30) return null; // 0x30 = SEQUENCE (constructed) — todo ContentInfo/CMS empieza así.

        int p = 1;
        int primerByteLongitud = buffer[p++];
        int longitudContenido;

        if (primerByteLongitud < 0x80)
        {
            longitudContenido = primerByteLongitud;
        }
        else
        {
            int numOctetos = primerByteLongitud & 0x7F;
            if (numOctetos is 0 or > 4) return null; // longitud indefinida (BER, no DER) o irrazonablemente grande — nunca la produce este sistema.
            if (p + numOctetos > buffer.Length) return null;

            longitudContenido = 0;
            for (int i = 0; i < numOctetos; i++)
                longitudContenido = (longitudContenido << 8) | buffer[p++];
        }

        long longitudTotal = p + (long)longitudContenido;
        if (longitudTotal <= 0 || longitudTotal > buffer.Length) return null;

        return buffer[..(int)longitudTotal];
    }

    /// <summary>
    /// Verifica un sello de tiempo de ARCHIVO (PAdES-LTA, RUNBOOK.md 12.24)
    /// — distinto de una firma normal en dos aspectos: (1) lo que hay que
    /// comprobar no es "¿la firma corresponde al certificado?" sino "¿el
    /// TimeStampToken es internamente consistente Y su messageImprint
    /// coincide con el contenido cubierto?"; (2) el "certificado" relevante
    /// es el de la TSA, no el de un firmante.
    /// </summary>
    private static ResultadoVerificacionPades VerificarSelloDeArchivo(byte[] contenidoCubierto, byte[] tokenDer)
    {
        try
        {
            var token = new TimeStampToken(new CmsSignedData(tokenDer));
            var certificadoTsaBc = token.GetCertificates().EnumerateMatches(token.SignerID).FirstOrDefault();
            if (certificadoTsaBc is null)
                return new ResultadoVerificacionPades(false, null, null, null, tokenDer,
                    "El sello de tiempo de archivo no incluye el certificado de la TSA que lo firmó.", EsSelloDeArchivo: true);

            bool firmaTokenValida;
            try { token.Validate(certificadoTsaBc); firmaTokenValida = true; }
            catch (TspValidationException) { firmaTokenValida = false; }

            byte[] digestEsperado = SHA256.HashData(contenidoCubierto);
            bool imprintCoincide = token.TimeStampInfo.GetMessageImprintDigest().AsSpan().SequenceEqual(digestEsperado);

            var certificadoTsaNet = new X509Certificate2(certificadoTsaBc.GetEncoded());
            bool valido = firmaTokenValida && imprintCoincide;
            string? error = valido ? null
                : !firmaTokenValida ? "La firma interna del sello de tiempo de archivo no es válida — el token pudo alterarse después de emitirse."
                : "El sello de tiempo de archivo no corresponde al contenido actual del documento (messageImprint no coincide) — el archivo pudo alterarse después de sellarse.";

            return new ResultadoVerificacionPades(
                valido, null, certificadoTsaNet, token.TimeStampInfo.GenTime, tokenDer, error, EsSelloDeArchivo: true);
        }
        catch (Exception ex)
        {
            return new ResultadoVerificacionPades(false, null, null, null, null,
                $"No se pudo procesar el sello de tiempo de archivo: {ex.Message}", EsSelloDeArchivo: true);
        }
    }

    /// <summary>Extrae el valor de una clave cuyo valor es un Name PDF (p. ej. <c>/SubFilter /ETSI.RFC3161</c>) — sin la barra inicial del valor.</summary>
    private static string? ExtraerNombrePdf(string texto, string clave, int limiteSuperior)
    {
        int cuentaAtras = Math.Min(limiteSuperior, 4000);
        int idxClave = texto.LastIndexOf(clave, limiteSuperior, cuentaAtras, StringComparison.Ordinal);
        if (idxClave < 0) return null;

        int p = idxClave + clave.Length;
        while (p < texto.Length && char.IsWhiteSpace(texto[p])) p++;
        if (p >= texto.Length || texto[p] != '/') return null;

        p++; // consumir la barra inicial del Name.
        int inicio = p;
        while (p < texto.Length && !char.IsWhiteSpace(texto[p]) && texto[p] != '/' && texto[p] != '>') p++;
        return texto.Substring(inicio, p - inicio);
    }

    private static string? ExtraerCadenaPdf(string texto, string clave, int limiteSuperior)
    {
        int cuentaAtras = Math.Min(limiteSuperior, 4000);
        int idxClave = texto.LastIndexOf(clave, limiteSuperior, cuentaAtras, StringComparison.Ordinal);
        if (idxClave < 0) return null;

        int p = idxClave + clave.Length;
        while (p < texto.Length && char.IsWhiteSpace(texto[p])) p++;
        if (p >= texto.Length) return null;

        if (texto[p] == '(')
        {
            int fin = texto.IndexOf(')', p + 1);
            return fin < 0 ? null : texto.Substring(p + 1, fin - p - 1);
        }

        if (texto[p] == '<')
        {
            int fin = texto.IndexOf('>', p + 1);
            if (fin < 0) return null;
            string hex = texto.Substring(p + 1, fin - p - 1);
            try
            {
                byte[] bytes = Convert.FromHexString(hex);
                return bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF
                    ? Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2)
                    : Encoding.Latin1.GetString(bytes);
            }
            catch (FormatException) { return null; }
        }

        return null;
    }

    private static readonly Regex FormatoFechaPdf = new(
        @"^D:(\d{4})(\d{2})(\d{2})(\d{2})(\d{2})(\d{2})([+-])(\d{2})'(\d{2})'$", RegexOptions.Compiled);

    /// <summary>Inversa de PdfSignaturePlaceholder.FormatearFechaPdf — ver ISO 32000-1 §7.9.4.</summary>
    private static DateTimeOffset? ParsearFechaPdf(string? valor)
    {
        if (valor is null) return null;
        var m = FormatoFechaPdf.Match(valor);
        if (!m.Success) return null;

        try
        {
            int signo = m.Groups[7].Value == "-" ? -1 : 1;
            var desplazamiento = new TimeSpan(
                signo * int.Parse(m.Groups[8].Value),
                signo * int.Parse(m.Groups[9].Value), 0);
            return new DateTimeOffset(
                int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value),
                int.Parse(m.Groups[4].Value), int.Parse(m.Groups[5].Value), int.Parse(m.Groups[6].Value),
                desplazamiento);
        }
        catch (ArgumentOutOfRangeException) { return null; }
    }
}
