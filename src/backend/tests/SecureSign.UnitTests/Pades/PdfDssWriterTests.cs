using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using SecureSign.Pades;
using static SecureSign.UnitTests.Pades.PdfDePruebaHelper;

namespace SecureSign.UnitTests.Pades;

/// <summary>
/// PAdES-LT: embeber el Document Security Store (DSS) con la cadena de
/// certificados + CRL/OCSP que ya demostraron "no revocado" al firmar — ver
/// RUNBOOK.md 12.24. Verificación deliberadamente INDEPENDIENTE de los
/// parsers internos de PdfSignaturePlaceholder/PdfDssWriter (parseo propio,
/// mínimo, en este mismo archivo) — si un bug estuviera en la lógica de
/// parseo compartida, reusarla aquí lo escondería en vez de detectarlo.
/// </summary>
public sealed class PdfDssWriterTests
{
    private static byte[] FabricarBytesDePrueba(string semilla, int longitud)
    {
        // No hace falta que sean DER real y válido — PdfDssWriter solo los
        // embebe como bytes crudos, nunca los interpreta; lo que importa
        // para esta prueba es poder reconocerlos byte a byte al leerlos de
        // vuelta del PDF.
        byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes(semilla));
        byte[] resultado = new byte[longitud];
        for (int i = 0; i < longitud; i++) resultado[i] = hash[i % hash.Length];
        return resultado;
    }

    /// <summary>Parseo propio, mínimo: ubica "N 0 obj ... /Length L ... stream\n" y devuelve exactamente L bytes — ver comentario de clase.</summary>
    private static byte[] ExtraerContenidoStream(byte[] pdf, int objNum)
    {
        string texto = Encoding.Latin1.GetString(pdf);
        // Sin ancla de '\n' al inicio: el primer objeto nuevo de una revisión
        // va pegado directamente al "%%EOF" de la revisión anterior, sin
        // separador — igual que ya hace PdfSignaturePlaceholder.Ensamblar.
        // Lookbehind niega que haya un dígito justo antes, para no matchear
        // "115" al buscar "15".
        var m = Regex.Match(texto, $@"(?<!\d){objNum} 0 obj\n");
        Assert.True(m.Success, $"No se encontró el objeto {objNum} 0 obj.");
        int idxObj = m.Index;

        int idxLength = texto.IndexOf("/Length", idxObj, StringComparison.Ordinal);
        Assert.True(idxLength >= 0, $"El objeto {objNum} no tiene /Length.");
        int p = idxLength + "/Length".Length;
        while (char.IsWhiteSpace(texto[p])) p++;
        int inicioNum = p;
        while (char.IsDigit(texto[p])) p++;
        int longitud = int.Parse(texto.Substring(inicioNum, p - inicioNum));

        int idxStream = texto.IndexOf("stream\n", idxLength, StringComparison.Ordinal);
        Assert.True(idxStream >= 0, $"El objeto {objNum} no tiene 'stream'.");
        int inicioContenido = idxStream + "stream\n".Length;

        return pdf[inicioContenido..(inicioContenido + longitud)];
    }

    private static string ExtraerTextoDss(byte[] pdf)
    {
        string texto = Encoding.Latin1.GetString(pdf);
        // La revisión del catálogo MÁS RECIENTE es la que importa — como las
        // revisiones se van apilando al final del archivo, es la ÚLTIMA
        // ocurrencia de "/DSS N 0 R" en el texto, no la primera (que sería
        // una versión vieja del catálogo, ya superada).
        var coincidencias = Regex.Matches(texto, @"/DSS (\d+) 0 R");
        Assert.True(coincidencias.Count > 0, "El catálogo no tiene /DSS.");
        int numDss = int.Parse(coincidencias[^1].Groups[1].Value);

        var mObj = Regex.Match(texto, $@"(?<!\d){numDss} 0 obj\n");
        Assert.True(mObj.Success, $"No se encontró el objeto DSS {numDss}.");
        int idxObj = mObj.Index;
        int idxEndobj = texto.IndexOf("endobj", idxObj, StringComparison.Ordinal);
        return texto[idxObj..(idxEndobj + "endobj".Length)];
    }

    private static int ContarReferenciasEnArray(string textoDss, string clave)
    {
        var m = Regex.Match(textoDss, $@"{Regex.Escape(clave)} \[([^\]]*)\]");
        if (!m.Success) return 0;
        return Regex.Matches(m.Groups[1].Value, @"\d+ \d+ R").Count;
    }

    [Fact]
    public void Agrega_dss_con_cadena_de_certificados_y_crl_embebidos_correctamente()
    {
        using var firmante = CertificadoDePruebaHelper.GenerarFirmante("Firmante LT");
        byte[] pdfFirmado = FirmarConCertificadoDePrueba(CrearPdfMinimo("documento LT"), firmante);

        byte[] certHoja = FabricarBytesDePrueba("cert-hoja", 400);
        byte[] certRaiz = FabricarBytesDePrueba("cert-raiz", 350);
        byte[] crl = FabricarBytesDePrueba("crl", 900);

        byte[] cms = CmsDeLaUltimaFirma(pdfFirmado);
        var material = new MaterialDss([certHoja, certRaiz], crl, OcspRespuestaDer: null);
        byte[] pdfConDss = PdfDssWriter.AgregarDss(pdfFirmado, cms, material);

        string textoDss = ExtraerTextoDss(pdfConDss);
        Assert.Contains("/Type /DSS", textoDss, StringComparison.Ordinal);
        Assert.Equal(2, ContarReferenciasEnArray(textoDss, "/Certs"));
        Assert.Equal(1, ContarReferenciasEnArray(textoDss, "/CRLs"));
        Assert.Equal(0, ContarReferenciasEnArray(textoDss, "/OCSPs"));

        // La clave VRI es el SHA-1 en hex MAYÚSCULAS del CMS exacto de la
        // última firma — se recalcula de forma independiente aquí, sin
        // llamar a PdfDssWriter, y debe coincidir.
        string claveVriEsperada = Convert.ToHexString(SHA1.HashData(cms));
        Assert.Contains($"/{claveVriEsperada} <<", textoDss, StringComparison.Ordinal);

        // Los objetos de /Certs y /CRLs referenciados deben contener EXACTAMENTE los bytes originales.
        var refsCerts = Regex.Matches(Regex.Match(textoDss, @"/Certs \[([^\]]*)\]").Groups[1].Value, @"(\d+) \d+ R")
            .Select(m => int.Parse(m.Groups[1].Value)).ToList();
        Assert.Equal(2, refsCerts.Count);
        Assert.Equal(certHoja, ExtraerContenidoStream(pdfConDss, refsCerts[0]));
        Assert.Equal(certRaiz, ExtraerContenidoStream(pdfConDss, refsCerts[1]));

        int refCrl = int.Parse(Regex.Match(Regex.Match(textoDss, @"/CRLs \[([^\]]*)\]").Groups[1].Value, @"(\d+) \d+ R").Groups[1].Value);
        Assert.Equal(crl, ExtraerContenidoStream(pdfConDss, refCrl));
    }

    [Fact]
    public void Agregar_dss_no_invalida_la_firma_ya_existente()
    {
        using var firmante = CertificadoDePruebaHelper.GenerarFirmante("Firmante LT");
        byte[] pdfFirmado = FirmarConCertificadoDePrueba(CrearPdfMinimo("documento LT"), firmante);

        var material = new MaterialDss([FabricarBytesDePrueba("c", 300)], FabricarBytesDePrueba("r", 500), null);
        byte[] pdfConDss = PdfDssWriter.AgregarDss(pdfFirmado, CmsDeLaUltimaFirma(pdfFirmado), material);

        var resultado = PdfSignatureVerifier.VerificarUltima(pdfConDss);
        Assert.True(resultado.Valido, resultado.Error);
    }

    [Fact]
    public void Sin_crl_ni_ocsp_los_arrays_correspondientes_quedan_vacios()
    {
        using var firmante = CertificadoDePruebaHelper.GenerarFirmante("Firmante Sin Revocacion");
        byte[] pdfFirmado = FirmarConCertificadoDePrueba(CrearPdfMinimo("documento"), firmante);

        var material = new MaterialDss([FabricarBytesDePrueba("c", 200)], CrlDer: null, OcspRespuestaDer: null);
        byte[] pdfConDss = PdfDssWriter.AgregarDss(pdfFirmado, CmsDeLaUltimaFirma(pdfFirmado), material);

        string textoDss = ExtraerTextoDss(pdfConDss);
        Assert.Equal(1, ContarReferenciasEnArray(textoDss, "/Certs"));
        Assert.Equal(0, ContarReferenciasEnArray(textoDss, "/CRLs"));
        Assert.Equal(0, ContarReferenciasEnArray(textoDss, "/OCSPs"));
    }

    /// <summary>Documento con dos firmantes (RUNBOOK.md 12.11): agregar DSS tras el segundo NO debe perder la entrada VRI ni los certificados del primero.</summary>
    [Fact]
    public void Dos_firmantes_cada_dss_extiende_el_anterior_sin_perder_entradas()
    {
        byte[] pdf = CrearPdfMinimo("documento con dos firmantes LT");

        using var firmanteA = CertificadoDePruebaHelper.GenerarFirmante("Firmante A");
        pdf = FirmarConCertificadoDePrueba(pdf, firmanteA);
        byte[] cmsA = CmsDeLaUltimaFirma(pdf);
        string claveVriA = Convert.ToHexString(SHA1.HashData(cmsA));
        byte[] certA = FabricarBytesDePrueba("cert-A", 300);
        pdf = PdfDssWriter.AgregarDss(pdf, cmsA, new MaterialDss([certA], FabricarBytesDePrueba("crl-A", 400), null));

        using var firmanteB = CertificadoDePruebaHelper.GenerarFirmante("Firmante B");
        pdf = FirmarConCertificadoDePrueba(pdf, firmanteB);
        byte[] cmsB = CmsDeLaUltimaFirma(pdf);
        string claveVriB = Convert.ToHexString(SHA1.HashData(cmsB));
        byte[] certB = FabricarBytesDePrueba("cert-B", 300);
        pdf = PdfDssWriter.AgregarDss(pdf, cmsB, new MaterialDss([certB], FabricarBytesDePrueba("crl-B", 400), null));

        string textoDssFinal = ExtraerTextoDss(pdf);
        Assert.Contains($"/{claveVriA} <<", textoDssFinal, StringComparison.Ordinal);
        Assert.Contains($"/{claveVriB} <<", textoDssFinal, StringComparison.Ordinal);
        Assert.Equal(2, ContarReferenciasEnArray(textoDssFinal, "/Certs"));
        Assert.Equal(2, ContarReferenciasEnArray(textoDssFinal, "/CRLs"));

        // Ambas firmas (A y B) siguen verificando después de las dos rondas de DSS.
        var todas = PdfSignatureVerifier.VerificarTodas(pdf);
        Assert.Equal(2, todas.Count);
        Assert.All(todas, r => Assert.True(r.Valido, r.Error));
    }

    /// <summary>
    /// AgregarDss ya no extrae el CMS por su cuenta (RUNBOOK.md 12.24, hallazgo
    /// de eficiencia: el llamador ya lo tiene tras verificar la firma, así que
    /// no tiene sentido volver a decodificar y re-verificar todo el PDF para
    /// recalcularlo) — por eso un PDF sin firmar ya no es una precondición
    /// que esta clase deba detectar: quien SÍ debe detectarlo es
    /// PdfSignatureVerifier, antes de siquiera intentar extraer un CMS que no
    /// existe (así es como FirmarLocalHandler nunca llega a llamar AgregarDss
    /// en ese caso — corta el flujo en verificacionPades.Valido == false).
    /// </summary>
    [Fact]
    public void Sin_ninguna_firma_previa_no_hay_cms_que_extraer()
    {
        byte[] pdfSinFirmar = CrearPdfMinimo("documento sin firmar");

        var verificacion = PdfSignatureVerifier.VerificarUltima(pdfSinFirmar);

        Assert.False(verificacion.Valido);
        Assert.Null(verificacion.CmsDer);
    }
}
