using System.Security.Cryptography.X509Certificates;
using System.Text;
using Org.BouncyCastle.Cms;

namespace SecureSign.Pades;

/// <summary>
/// Resultado de verificar UNA firma /Sig dentro de un PDF — nunca solo
/// verdadero/falso, siempre con el detalle suficiente para auditoría (ver
/// informe de preauditoría INDECOPI/IOFE, hallazgo P0-05: "el resultado
/// nunca debería limitarse a true/false").
/// </summary>
public sealed record ResultadoVerificacionPades(
    bool Valido,
    string? NombreFirma,
    X509Certificate2? Certificado,
    string? Error);

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
            ? new ResultadoVerificacionPades(false, null, null, "El documento no contiene ningún diccionario /Sig con /ByteRange.")
            : todas[^1];
    }

    private static ResultadoVerificacionPades VerificarUna(byte[] pdf, string texto, int idxByteRange)
    {
        try
        {
            int inicioArray = texto.IndexOf('[', idxByteRange);
            int finArray = texto.IndexOf(']', inicioArray);
            if (inicioArray < 0 || finArray < 0)
                return new ResultadoVerificacionPades(false, null, null, "No se pudo parsear el array de /ByteRange.");

            long[] br = Array.ConvertAll(
                texto.Substring(inicioArray + 1, finArray - inicioArray - 1).Split(' ', StringSplitOptions.RemoveEmptyEntries),
                long.Parse);
            if (br.Length != 4)
                return new ResultadoVerificacionPades(false, null, null, $"/ByteRange tiene {br.Length} valores, se esperaban 4.");

            byte[] tramoA = pdf[(int)br[0]..(int)(br[0] + br[1])];
            byte[] tramoB = pdf[(int)br[2]..(int)(br[2] + br[3])];
            byte[] contenidoCubierto = new byte[tramoA.Length + tramoB.Length];
            Buffer.BlockCopy(tramoA, 0, contenidoCubierto, 0, tramoA.Length);
            Buffer.BlockCopy(tramoB, 0, contenidoCubierto, tramoA.Length, tramoB.Length);

            int idxContents = texto.IndexOf("/Contents", idxByteRange, StringComparison.Ordinal);
            if (idxContents < 0)
                return new ResultadoVerificacionPades(false, null, null, "No se encontró /Contents asociado a este /ByteRange.");

            int inicioHex = texto.IndexOf('<', idxContents) + 1;
            int finHex = texto.IndexOf('>', inicioHex);
            if (inicioHex <= 0 || finHex < 0)
                return new ResultadoVerificacionPades(false, null, null, "No se pudo parsear el valor hexadecimal de /Contents.");

            string hex = texto.Substring(inicioHex, finHex - inicioHex).TrimEnd('0');
            if (hex.Length % 2 != 0) hex += "0";
            if (hex.Length == 0)
                return new ResultadoVerificacionPades(false, null, null, "/Contents está vacío — no es una firma real, es un campo sin firmar.");

            byte[] cms = Convert.FromHexString(hex);

            var datosFirmados = new CmsSignedData(new CmsProcessableByteArray(contenidoCubierto), cms);
            var firmantes = datosFirmados.GetSignerInfos().GetSigners();
            if (firmantes.Count == 0)
                return new ResultadoVerificacionPades(false, null, null, "El CMS no contiene ningún SignerInfo.");

            var firmante = firmantes.First();
            var certificadosBc = datosFirmados.GetCertificates().EnumerateMatches(firmante.SignerID);
            var certificadoBc = certificadosBc.FirstOrDefault()
                ?? throw new InvalidOperationException("El CMS no incluye el certificado del firmante.");

            bool valido = firmante.Verify(certificadoBc);
            var certificadoNet = new X509Certificate2(certificadoBc.GetEncoded());

            string? nombreFirma = null;
            int idxName = texto.LastIndexOf("/Name", idxContents, idxContents - Math.Max(0, idxContents - 4000), StringComparison.Ordinal);
            if (idxName >= 0 && idxName < idxContents)
            {
                int inicioParen = texto.IndexOf('(', idxName);
                int finParen = inicioParen >= 0 ? texto.IndexOf(')', inicioParen) : -1;
                if (inicioParen >= 0 && finParen > inicioParen)
                    nombreFirma = texto.Substring(inicioParen + 1, finParen - inicioParen - 1);
            }

            return valido
                ? new ResultadoVerificacionPades(true, nombreFirma, certificadoNet, null)
                : new ResultadoVerificacionPades(false, nombreFirma, certificadoNet, "El CMS no verifica contra su propio certificado (message-digest o firma inválida).");
        }
        catch (Exception ex)
        {
            return new ResultadoVerificacionPades(false, null, null, $"Error al procesar esta firma: {ex.Message}");
        }
    }
}
