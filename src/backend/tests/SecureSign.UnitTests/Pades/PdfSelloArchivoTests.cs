using System.Security.Cryptography;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Tsp;
using Org.BouncyCastle.Utilities.Collections;
using Org.BouncyCastle.X509;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using SecureSign.Pades;
using BcX509Certificate = Org.BouncyCastle.X509.X509Certificate;

namespace SecureSign.UnitTests.Pades;

/// <summary>
/// PAdES-LTA: sello de tiempo de ARCHIVO (<c>/DocTimeStamp</c>,
/// <c>/SubFilter /ETSI.RFC3161</c>) sobre el documento ya firmado (y, en la
/// práctica, con su DSS) — ver RUNBOOK.md 12.24. Reusa la misma "TSA de
/// prueba offline" que <see cref="SelloTiempoTests"/> (mismo protocolo RFC
/// 3161, sin red) para que la suite sea determinística.
/// </summary>
public sealed class PdfSelloArchivoTests
{
    private static byte[] CrearPdfMinimo(string texto)
    {
        using var documento = new PdfDocument();
        var pagina = documento.AddPage();
        using var graficos = XGraphics.FromPdfPage(pagina);
        graficos.DrawString(texto, new XFont("Arial", 14), XBrushes.Black, new XPoint(40, 60));
        using var buffer = new MemoryStream();
        documento.Save(buffer);
        return buffer.ToArray();
    }

    private static byte[] FirmarConCertificadoDePrueba(byte[] pdfActual, CertificadoDePruebaHelper.ParFirmante firmante)
    {
        var preparado = PdfSignaturePlaceholder.Preparar(pdfActual, firmante.Certificado.GetNameInfo(System.Security.Cryptography.X509Certificates.X509NameType.SimpleName, false), "Prueba automatizada", DateTimeOffset.UtcNow);
        byte[] cms = CmsBuilder.Firmar(preparado.ContenidoCubierto, firmante.Certificado, cadenaCertificacion: null,
            datos => firmante.Rsa.SignData(datos, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        return PdfSignaturePlaceholder.Inyectar(preparado, cms);
    }

    /// <summary>Misma "TSA de prueba offline" que SelloTiempoTests — ver ese archivo para el porqué de cada línea.</summary>
    private static (TimeStampTokenGenerator Generador, BcX509Certificate Certificado) CrearTsaDePrueba()
    {
        var generadorLlaves = GeneratorUtilities.GetKeyPairGenerator("RSA");
        generadorLlaves.Init(new KeyGenerationParameters(new SecureRandom(), 2048));
        var parLlaves = generadorLlaves.GenerateKeyPair();

        var nombre = new X509Name("CN=TSA de prueba offline (LTA)");
        var generadorCert = new X509V3CertificateGenerator();
        generadorCert.SetSerialNumber(BigInteger.ValueOf(1));
        generadorCert.SetIssuerDN(nombre);
        generadorCert.SetSubjectDN(nombre);
        generadorCert.SetNotBefore(DateTime.UtcNow.AddDays(-1));
        generadorCert.SetNotAfter(DateTime.UtcNow.AddYears(1));
        generadorCert.SetPublicKey(parLlaves.Public);
        generadorCert.AddExtension(X509Extensions.ExtendedKeyUsage, true, new ExtendedKeyUsage(KeyPurposeID.id_kp_timeStamping));
        generadorCert.AddExtension(X509Extensions.BasicConstraints, true, new BasicConstraints(false));
        BcX509Certificate certificado = generadorCert.Generate(new Asn1SignatureFactory("SHA256WITHRSA", parLlaves.Private));

        var generadorToken = new TimeStampTokenGenerator(parLlaves.Private, certificado, TspAlgorithms.Sha256, "1.2.3.4.5.6");
        generadorToken.SetCertificates(CollectionUtilities.CreateStore(new[] { certificado }));

        return (generadorToken, certificado);
    }

    private static byte[] SellarLocalmente(TimeStampTokenGenerator tsa, byte[] hash)
    {
        var generadorSolicitud = new TimeStampRequestGenerator();
        generadorSolicitud.SetCertReq(true);
        var solicitud = generadorSolicitud.Generate(TspAlgorithms.Sha256, hash, BigInteger.ValueOf(new Random().Next()));
        var token = tsa.Generate(solicitud, BigInteger.ValueOf(1), DateTime.UtcNow);
        return token.GetEncoded();
    }

    /// <summary>El paso completo que hace FirmarLocalHandler: preparar el hueco, hashear lo cubierto, sellar, inyectar.</summary>
    private static byte[] AgregarSelloDeArchivo(byte[] pdfFirmado, TimeStampTokenGenerator tsa)
    {
        var preparado = PdfSignaturePlaceholder.PrepararSelloDeArchivo(pdfFirmado, DateTimeOffset.UtcNow);
        byte[] hash = SHA256.HashData(preparado.ContenidoCubierto);
        byte[] tokenDer = SellarLocalmente(tsa, hash);
        return PdfSignaturePlaceholder.Inyectar(preparado, tokenDer);
    }

    [Fact]
    public void Sello_de_archivo_valido_se_detecta_como_tal_y_verifica()
    {
        var (tsa, _) = CrearTsaDePrueba();
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
        var (tsa, _) = CrearTsaDePrueba();
        using var firmante = CertificadoDePruebaHelper.GenerarFirmante("Firmante LTA");
        byte[] pdf = FirmarConCertificadoDePrueba(CrearPdfMinimo("documento LTA"), firmante);

        pdf = AgregarSelloDeArchivo(pdf, tsa);

        var firmaReal = PdfSignatureVerifier.VerificarTodas(pdf)[0];
        Assert.True(firmaReal.Valido, firmaReal.Error);
    }

    [Fact]
    public void Sello_de_archivo_tambien_cubre_el_dss_previo()
    {
        var (tsa, _) = CrearTsaDePrueba();
        using var firmante = CertificadoDePruebaHelper.GenerarFirmante("Firmante LTA");
        byte[] pdf = FirmarConCertificadoDePrueba(CrearPdfMinimo("documento LTA con DSS"), firmante);

        byte[] certDer = SHA1.HashData("cert-de-prueba"u8.ToArray());
        pdf = PdfDssWriter.AgregarDss(pdf, new MaterialDss([certDer], CrlDer: null, OcspRespuestaDer: null));
        pdf = AgregarSelloDeArchivo(pdf, tsa);

        var resultados = PdfSignatureVerifier.VerificarTodas(pdf);
        var sello = Assert.Single(resultados, r => r.EsSelloDeArchivo);
        Assert.True(sello.Valido, sello.Error);
    }

    [Fact]
    public void Documento_alterado_despues_del_sello_de_archivo_lo_invalida()
    {
        var (tsa, _) = CrearTsaDePrueba();
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
        var (tsa, _) = CrearTsaDePrueba();
        using var firmante = CertificadoDePruebaHelper.GenerarFirmante("Firmante LTA");
        byte[] pdf = FirmarConCertificadoDePrueba(CrearPdfMinimo("documento LTA"), firmante);

        var preparado = PdfSignaturePlaceholder.PrepararSelloDeArchivo(pdf, DateTimeOffset.UtcNow);
        byte[] tokenDer = SellarLocalmente(tsa, SHA256.HashData(preparado.ContenidoCubierto));
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
