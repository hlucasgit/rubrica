using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Tsp;
using Org.BouncyCastle.Utilities.Collections;
using Org.BouncyCastle.X509;
using BcX509Certificate = Org.BouncyCastle.X509.X509Certificate;

namespace SecureSign.UnitTests.Pades;

/// <summary>
/// TSA de prueba 100% local — mismo protocolo RFC 3161 que
/// SecureSign.Tsa.ClienteTsaRfc3161, pero firmando con una llave generada en
/// memoria, sin ninguna llamada de red. Compartida por <see cref="SelloTiempoTests"/>
/// (PAdES-T, RUNBOOK.md 12.14) y <see cref="PdfSelloArchivoTests"/> (PAdES-LTA,
/// RUNBOOK.md 12.25) — mismo mecanismo, dos puntos de incrustación distintos.
/// </summary>
internal static class TsaDePruebaHelper
{
    internal static (TimeStampTokenGenerator Generador, BcX509Certificate Certificado) CrearTsaDePrueba()
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

    internal static byte[] SellarLocalmente(TimeStampTokenGenerator tsa, byte[] hash)
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
}
