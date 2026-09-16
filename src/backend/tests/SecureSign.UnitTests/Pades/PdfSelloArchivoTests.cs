using System.Security.Cryptography;
using Org.BouncyCastle.Tsp;
using SecureSign.Pades;
using static SecureSign.UnitTests.Pades.PdfDePruebaHelper;

namespace SecureSign.UnitTests.Pades;

/// <summary>
/// PAdES-LTA: sello de tiempo de ARCHIVO (<c>/DocTimeStamp</c>,
/// <c>/SubFilter /ETSI.RFC3161</c>) sobre el documento ya firmado (y, en la
/// práctica, con su DSS) — ver RUNBOOK.md 12.24. Reusa la misma "TSA de
/// prueba offline" que <see cref="SelloTiempoTests"/> (<see cref="TsaDePruebaHelper"/>,
/// mismo protocolo RFC 3161, sin red) para que la suite sea determinística.
/// </summary>
public sealed class PdfSelloArchivoTests
{
    /// <summary>El paso completo que hace FirmarLocalHandler: preparar el hueco, hashear lo cubierto, sellar, inyectar.</summary>
    private static byte[] AgregarSelloDeArchivo(byte[] pdfFirmado, TimeStampTokenGenerator tsa)
    {
        var preparado = PdfSignaturePlaceholder.PrepararSelloDeArchivo(pdfFirmado, DateTimeOffset.UtcNow);
        byte[] hash = SHA256.HashData(preparado.ContenidoCubierto);
        byte[] tokenDer = TsaDePruebaHelper.SellarLocalmente(tsa, hash);
        return PdfSignaturePlaceholder.Inyectar(preparado, tokenDer);
    }

    [Fact]
    public void Sello_de_archivo_valido_se_detecta_como_tal_y_verifica()
    {
        var (tsa, _) = TsaDePruebaHelper.CrearTsaDePrueba();
        using var firmante = CertificadoDePruebaHelper.GenerarFirmante("Firmante LTA");
        byte[] pdf = FirmarConCertificadoDePrueba(CrearPdfMinimo("documento LTA"), firmante);

        pdf = AgregarSelloDeArchivo(pdf, tsa);

        var resultados = PdfSignatureVerifier.VerificarTodas(pdf);
        Assert.Equal(2, resultados.Count); // la firma real + el sello de archivo

        var sello = resultados[^1];
        Assert.True(sello.EsSelloDeArchivo);
        Assert.True(sello.Valido, sello.Error);
        Assert.NotNull(sello.Certificado); // el certificado de la TSA, no de un firmante
        Assert.NotNull(sello.InstanteFirmaDeclarado); // el GenTime del token

        var firmaReal = resultados[0];
        Assert.False(firmaReal.EsSelloDeArchivo);
    }

    [Fact]
    public void Agregar_sello_de_archivo_no_invalida_la_firma_real_anterior()
    {
        var (tsa, _) = TsaDePruebaHelper.CrearTsaDePrueba();
        using var firmante = CertificadoDePruebaHelper.GenerarFirmante("Firmante LTA");
        byte[] pdf = FirmarConCertificadoDePrueba(CrearPdfMinimo("documento LTA"), firmante);

        pdf = AgregarSelloDeArchivo(pdf, tsa);

        var firmaReal = PdfSignatureVerifier.VerificarTodas(pdf)[0];
        Assert.True(firmaReal.Valido, firmaReal.Error);
    }

    [Fact]
    public void Sello_de_archivo_tambien_cubre_el_dss_previo()
    {
        var (tsa, _) = TsaDePruebaHelper.CrearTsaDePrueba();
        using var firmante = CertificadoDePruebaHelper.GenerarFirmante("Firmante LTA");
        byte[] pdf = FirmarConCertificadoDePrueba(CrearPdfMinimo("documento LTA con DSS"), firmante);

        byte[] certDer = SHA1.HashData("cert-de-prueba"u8.ToArray());
        pdf = PdfDssWriter.AgregarDss(pdf, CmsDeLaUltimaFirma(pdf), new MaterialDss([certDer], CrlDer: null, OcspRespuestaDer: null));
        pdf = AgregarSelloDeArchivo(pdf, tsa);

        var resultados = PdfSignatureVerifier.VerificarTodas(pdf);
        var sello = Assert.Single(resultados, r => r.EsSelloDeArchivo);
        Assert.True(sello.Valido, sello.Error);
    }

    [Fact]
    public void Documento_alterado_despues_del_sello_de_archivo_lo_invalida()
    {
        var (tsa, _) = TsaDePruebaHelper.CrearTsaDePrueba();
        using var firmante = CertificadoDePruebaHelper.GenerarFirmante("Firmante LTA");
        byte[] pdf = FirmarConCertificadoDePrueba(CrearPdfMinimo("contenido original LTA"), firmante);
        pdf = AgregarSelloDeArchivo(pdf, tsa);

        // Altera un byte bien dentro del contenido original (cubierto tanto
        // por la firma real como por el sello de archivo).
        byte[] pdfAlterado = (byte[])pdf.Clone();
        int posicion = Array.IndexOf(pdfAlterado, (byte)'o');
        pdfAlterado[posicion] = (byte)'0';

        var sello = PdfSignatureVerifier.VerificarTodas(pdfAlterado).Single(r => r.EsSelloDeArchivo);
        Assert.False(sello.Valido);
        Assert.Contains("messageImprint", sello.Error);
    }

    [Fact]
    public void Token_de_archivo_alterado_no_verifica()
    {
        var (tsa, _) = TsaDePruebaHelper.CrearTsaDePrueba();
        using var firmante = CertificadoDePruebaHelper.GenerarFirmante("Firmante LTA");
        byte[] pdf = FirmarConCertificadoDePrueba(CrearPdfMinimo("documento LTA"), firmante);

        var preparado = PdfSignaturePlaceholder.PrepararSelloDeArchivo(pdf, DateTimeOffset.UtcNow);
        byte[] tokenDer = TsaDePruebaHelper.SellarLocalmente(tsa, SHA256.HashData(preparado.ContenidoCubierto));
        byte[] tokenAlterado = (byte[])tokenDer.Clone();
        tokenAlterado[^10] ^= 0xFF;
        pdf = PdfSignaturePlaceholder.Inyectar(preparado, tokenAlterado);

        var sello = PdfSignatureVerifier.VerificarTodas(pdf).Single(r => r.EsSelloDeArchivo);
        Assert.False(sello.Valido);
    }

    [Fact]
    public void Sin_ninguna_firma_previa_lanza()
    {
        byte[] pdfSinFirmar = CrearPdfMinimo("documento sin firmar");
        Assert.Throws<InvalidOperationException>(() => PdfSignaturePlaceholder.PrepararSelloDeArchivo(pdfSinFirmar, DateTimeOffset.UtcNow));
    }
}
