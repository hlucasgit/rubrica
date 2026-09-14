using System.Security.Cryptography;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Cms;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Tsp;
using Org.BouncyCastle.Utilities.Collections;
using Org.BouncyCastle.X509;
using SecureSign.Pades;
using SecureSign.Tsa;
using BcX509Certificate = Org.BouncyCastle.X509.X509Certificate;

namespace SecureSign.UnitTests.Pades;

/// <summary>
/// Pruebas del ciclo PAdES-T (RUNBOOK.md 12.14) — deliberadamente SIN red:
/// en vez de contactar una TSA pública real (ya probado manualmente contra
/// tres TSAs reales, ver RUNBOOK.md), estas pruebas construyen una "TSA de
/// prueba" 100% offline con BouncyCastle, para que la suite sea rápida y
/// determinística. Lo que se prueba es la MECÁNICA de incrustar/extraer/
/// verificar el token — no la disponibilidad de ninguna TSA real.
/// </summary>
public sealed class SelloTiempoTests
{
    /// <summary>Emisor de sellos de tiempo 100% local — mismo protocolo RFC 3161 que SecureSign.Tsa.ClienteTsaRfc3161, pero firmando con una llave generada en memoria, sin ninguna llamada de red.</summary>
    private static (TimeStampTokenGenerator Generador, BcX509Certificate Certificado) CrearTsaDePrueba()
    {
        var generadorLlaves = GeneratorUtilities.GetKeyPairGenerator("RSA");
        generadorLlaves.Init(new KeyGenerationParameters(new SecureRandom(), 2048));
        var parLlaves = generadorLlaves.GenerateKeyPair();

        var nombre = new X509Name("CN=TSA de prueba offline");
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
        // SetCertReq(true) es imprescindible: BouncyCastle solo incrusta el
        // certificado de la TSA en el token si la SOLICITUD lo pidió, sin
        // importar que el generador ya tenga SetCertificates(...) — el
        // cliente real (ClienteTsaRfc3161) ya hace esto correctamente, este
        // detalle solo faltaba aquí, en el ayudante de pruebas offline.
        var generadorSolicitud = new TimeStampRequestGenerator();
        generadorSolicitud.SetCertReq(true);
        var solicitud = generadorSolicitud.Generate(TspAlgorithms.Sha256, hash, BigInteger.ValueOf(new Random().Next()));
        var token = tsa.Generate(solicitud, BigInteger.ValueOf(1), DateTime.UtcNow);
        return token.GetEncoded();
    }

    [Fact]
    public void Sello_incrustado_se_extrae_identico_y_verifica()
    {
        var (tsa, _) = CrearTsaDePrueba();

        using var firmante = CertificadoDePruebaHelper.GenerarFirmante("Firmante PAdES-T");
        byte[] contenido = "contenido de prueba"u8.ToArray();
        byte[] cms = CmsBuilder.Firmar(contenido, firmante.Certificado, cadenaCertificacion: null,
            datos => firmante.Rsa.SignData(datos, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

        byte[] hashFirma = SHA256.HashData(CmsBuilder.ObtenerBytesFirma(cms));
        byte[] tokenDer = SellarLocalmente(tsa, hashFirma);

        byte[] cmsConSello = CmsBuilder.AgregarSelloTiempo(cms, tokenDer);
        byte[]? tokenExtraido = CmsBuilder.ExtraerSelloTiempo(cmsConSello);

        Assert.NotNull(tokenExtraido);
        Assert.Equal(tokenDer, tokenExtraido);

        var verificacion = VerificadorTokenTsa.Verificar(tokenExtraido!);
        Assert.True(verificacion.FirmaTokenValida);
        Assert.False(verificacion.CadenaTsaConfiable); // ver RUNBOOK.md 12.14 — deliberadamente nunca true todavía.
    }

    [Fact]
    public void Agregar_sello_no_invalida_la_firma_cms_original()
    {
        var (tsa, _) = CrearTsaDePrueba();

        using var firmante = CertificadoDePruebaHelper.GenerarFirmante("Firmante PAdES-T");
        byte[] contenido = "contenido cubierto"u8.ToArray();
        byte[] cms = CmsBuilder.Firmar(contenido, firmante.Certificado, cadenaCertificacion: null,
            datos => firmante.Rsa.SignData(datos, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

        byte[] tokenDer = SellarLocalmente(tsa, SHA256.HashData(CmsBuilder.ObtenerBytesFirma(cms)));
        byte[] cmsConSello = CmsBuilder.AgregarSelloTiempo(cms, tokenDer);

        var datosFirmados = new CmsSignedData(new CmsProcessableByteArray(contenido), cmsConSello);
        var firmanteCms = datosFirmados.GetSignerInfos().GetSigners().Cast<SignerInformation>().First();
        var certificadoBc = datosFirmados.GetCertificates().EnumerateMatches(firmanteCms.SignerID).First();

        Assert.True(firmanteCms.Verify(certificadoBc));
    }

    [Fact]
    public void Token_alterado_no_verifica()
    {
        var (tsa, _) = CrearTsaDePrueba();
        byte[] tokenDer = SellarLocalmente(tsa, SHA256.HashData("cualquier cosa"u8.ToArray()));

        byte[] tokenAlterado = (byte[])tokenDer.Clone();
        tokenAlterado[^10] ^= 0xFF; // altera un byte cerca del final (dentro de la firma del token)

        var verificacion = VerificadorTokenTsa.Verificar(tokenAlterado);

        Assert.False(verificacion.FirmaTokenValida);
    }

    [Fact]
    public void Cms_sin_sello_no_expone_ningun_token()
    {
        using var firmante = CertificadoDePruebaHelper.GenerarFirmante("Firmante Sin Sello");
        byte[] cms = CmsBuilder.Firmar("contenido"u8.ToArray(), firmante.Certificado, cadenaCertificacion: null,
            datos => firmante.Rsa.SignData(datos, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

        Assert.Null(CmsBuilder.ExtraerSelloTiempo(cms));
    }
}
