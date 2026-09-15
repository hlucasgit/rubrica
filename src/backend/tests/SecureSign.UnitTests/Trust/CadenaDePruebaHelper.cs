using System.Security.Cryptography.X509Certificates;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using BcCertificate = Org.BouncyCastle.X509.X509Certificate;

namespace SecureSign.UnitTests.Trust;

/// <summary>
/// Cadena de certificados X.509 de un solo uso, CON las extensiones AIA
/// (RFC 5280 §4.2.2.1) y CRL Distribution Points (§4.2.1.13) reales que
/// SecureSign.Trust necesita para poder probarse sin depender de red real
/// contra RENIEC/INDECOPI — ver RUNBOOK.md 12.9/12.17. Deliberadamente
/// separado de CertificadoDePruebaHelper (SecureSign.Pades), que no
/// necesita ninguna cadena de confianza porque solo prueba mecánica CMS/PAdES.
/// </summary>
internal static class CadenaDePruebaHelper
{
    internal sealed record NodoCadena(BcCertificate CertificadoBc, AsymmetricCipherKeyPair Llaves)
    {
        public X509Certificate2 ComoDotNet() => new(CertificadoBc.GetEncoded());
    }

    internal static AsymmetricCipherKeyPair GenerarLlaves(int bits = 2048)
    {
        var generador = new RsaKeyPairGenerator();
        generador.Init(new KeyGenerationParameters(new SecureRandom(), bits));
        return generador.GenerateKeyPair();
    }

    private static BigInteger NuevoSerial() => BigInteger.ValueOf(Random.Shared.NextInt64(1, long.MaxValue));

    internal static NodoCadena GenerarRaiz(string commonName)
    {
        var llaves = GenerarLlaves();
        var nombre = new X509Name($"CN={commonName}");
        var generador = new X509V3CertificateGenerator();
        generador.SetSerialNumber(NuevoSerial());
        generador.SetIssuerDN(nombre);
        generador.SetSubjectDN(nombre);
        generador.SetNotBefore(DateTime.UtcNow.AddDays(-1));
        generador.SetNotAfter(DateTime.UtcNow.AddYears(10));
        generador.SetPublicKey(llaves.Public);
        generador.AddExtension(X509Extensions.BasicConstraints, true, new BasicConstraints(true));
        generador.AddExtension(X509Extensions.KeyUsage, true, new KeyUsage(KeyUsage.KeyCertSign | KeyUsage.CrlSign));

        var cert = generador.Generate(new Asn1SignatureFactory("SHA256WITHRSA", llaves.Private));
        return new NodoCadena(cert, llaves);
    }

    internal static NodoCadena GenerarIntermedia(NodoCadena raiz, string commonName, string urlEmisorCa)
    {
        var llaves = GenerarLlaves();
        var generador = new X509V3CertificateGenerator();
        generador.SetSerialNumber(NuevoSerial());
        generador.SetIssuerDN(raiz.CertificadoBc.SubjectDN);
        generador.SetSubjectDN(new X509Name($"CN={commonName}"));
        generador.SetNotBefore(DateTime.UtcNow.AddDays(-1));
        generador.SetNotAfter(DateTime.UtcNow.AddYears(5));
        generador.SetPublicKey(llaves.Public);
        generador.AddExtension(X509Extensions.BasicConstraints, true, new BasicConstraints(true));
        generador.AddExtension(X509Extensions.KeyUsage, true, new KeyUsage(KeyUsage.KeyCertSign | KeyUsage.CrlSign));
        generador.AddExtension(X509Extensions.AuthorityInfoAccess, false,
            new AuthorityInformationAccess(new AccessDescription(AccessDescription.IdADCAIssuers,
                new GeneralName(GeneralName.UniformResourceIdentifier, urlEmisorCa))));

        var cert = generador.Generate(new Asn1SignatureFactory("SHA256WITHRSA", raiz.Llaves.Private));
        return new NodoCadena(cert, llaves);
    }

    internal static NodoCadena GenerarHoja(
        NodoCadena emisora, string commonName, string urlEmisorCa, string urlOcsp, string urlCrl,
        DateTime? notBefore = null, DateTime? notAfter = null,
        bool esCa = false, int keyUsage = KeyUsage.NonRepudiation | KeyUsage.DigitalSignature)
    {
        var llaves = GenerarLlaves();
        var generador = new X509V3CertificateGenerator();
        generador.SetSerialNumber(NuevoSerial());
        generador.SetIssuerDN(emisora.CertificadoBc.SubjectDN);
        generador.SetSubjectDN(new X509Name($"CN={commonName}"));
        generador.SetNotBefore(notBefore ?? DateTime.UtcNow.AddDays(-1));
        generador.SetNotAfter(notAfter ?? DateTime.UtcNow.AddYears(1));
        generador.SetPublicKey(llaves.Public);
        generador.AddExtension(X509Extensions.BasicConstraints, true, new BasicConstraints(esCa));
        generador.AddExtension(X509Extensions.KeyUsage, true, new KeyUsage(keyUsage));
        generador.AddExtension(X509Extensions.AuthorityInfoAccess, false, new AuthorityInformationAccess(new[]
        {
            new AccessDescription(AccessDescription.IdADCAIssuers, new GeneralName(GeneralName.UniformResourceIdentifier, urlEmisorCa)),
            new AccessDescription(AccessDescription.IdADOcsp, new GeneralName(GeneralName.UniformResourceIdentifier, urlOcsp)),
        }));
        generador.AddExtension(X509Extensions.CrlDistributionPoints, false, new CrlDistPoint(new[]
        {
            new DistributionPoint(new DistributionPointName(new GeneralNames(new GeneralName(GeneralName.UniformResourceIdentifier, urlCrl))), null, null),
        }));

        var cert = generador.Generate(new Asn1SignatureFactory("SHA256WITHRSA", emisora.Llaves.Private));
        return new NodoCadena(cert, llaves);
    }

    /// <summary>Certificado delegado de firma OCSP (RFC 6960 §4.2.2.2): emitido por la misma CA, con el EKU id-kp-OCSPSigning.</summary>
    internal static NodoCadena GenerarFirmanteOcspDelegado(NodoCadena emisora, string commonName)
    {
        var llaves = GenerarLlaves();
        var generador = new X509V3CertificateGenerator();
        generador.SetSerialNumber(NuevoSerial());
        generador.SetIssuerDN(emisora.CertificadoBc.SubjectDN);
        generador.SetSubjectDN(new X509Name($"CN={commonName}"));
        generador.SetNotBefore(DateTime.UtcNow.AddDays(-1));
        generador.SetNotAfter(DateTime.UtcNow.AddYears(1));
        generador.SetPublicKey(llaves.Public);
        generador.AddExtension(X509Extensions.BasicConstraints, true, new BasicConstraints(false));
        generador.AddExtension(X509Extensions.ExtendedKeyUsage, false, new ExtendedKeyUsage(new[] { KeyPurposeID.id_kp_OCSPSigning }));

        var cert = generador.Generate(new Asn1SignatureFactory("SHA256WITHRSA", emisora.Llaves.Private));
        return new NodoCadena(cert, llaves);
    }

    internal static byte[] GenerarCrl(NodoCadena emisora, IEnumerable<BigInteger> serialesRevocados, AsymmetricKeyParameter? llaveFirmante = null, DateTime? proximaActualizacion = null)
    {
        var generador = new X509V2CrlGenerator();
        generador.SetIssuerDN(emisora.CertificadoBc.SubjectDN);
        generador.SetThisUpdate(DateTime.UtcNow.AddHours(-1));
        generador.SetNextUpdate(proximaActualizacion ?? DateTime.UtcNow.AddDays(1));
        foreach (var serial in serialesRevocados)
            generador.AddCrlEntry(serial, DateTime.UtcNow.AddHours(-2), 0);

        var crl = generador.Generate(new Asn1SignatureFactory("SHA256WITHRSA", llaveFirmante ?? emisora.Llaves.Private));
        return crl.GetEncoded();
    }
}
