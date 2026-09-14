using System.Security.Cryptography.X509Certificates;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Cms;
using Org.BouncyCastle.Asn1.Pkcs;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Cms;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Utilities.Collections;
using BcX509Certificate = Org.BouncyCastle.X509.X509Certificate;
using BcX509CertificateParser = Org.BouncyCastle.X509.X509CertificateParser;

namespace SecureSign.Pades;

/// <summary>
/// Construye la estructura CMS/PKCS#7 (SignedData, perfil CAdES-BES/detached)
/// que exige un /Contents de PAdES real — ver PdfSignaturePlaceholder. La
/// operación de llave privada NUNCA ocurre aquí: se delega en
/// <paramref name="firmarRsaPkcs1"/> (que en el Firmador Local es el mismo
/// PKCS#11 que ya se usaba para la firma "desacoplada", ver Program.cs) para
/// que el diseño de "el PIN nunca sale de la máquina del firmante" siga
/// intacto — esta clase solo ensambla ASN.1 alrededor de esa firma.
///
/// CAdES exige firmar sobre los ATRIBUTOS FIRMADOS (que incluyen, entre
/// otros, el messageDigest del contenido), no directamente sobre el hash del
/// documento — por eso <paramref name="firmarRsaPkcs1"/> recibe los bytes DER
/// de esos atributos (ya construidos por BouncyCastle), no un hash: es él
/// quien debe hashear (SHA-256) y anteponer el prefijo DigestInfo antes de
/// invocar CKM_RSA_PKCS, exactamente igual que el resto del sistema.
/// </summary>
public static class CmsBuilder
{
    /// <param name="contenidoCubierto">Los bytes exactamente cubiertos por el /ByteRange del PDF — ver ResultadoPreparacionPades.ContenidoCubierto.</param>
    /// <param name="certificadoFirmante">Certificado (DER/X509Certificate2) del firmante — su llave pública, nunca la privada.</param>
    /// <param name="cadenaCertificacion">Certificados intermedios/raíz a incluir en el CMS (opcional; ayuda a la validación offline).</param>
    /// <param name="firmarRsaPkcs1">
    /// Recibe el DER de los atributos firmados (SignedAttributes) y debe
    /// devolver la firma RSA PKCS#1 v1.5 sobre SHA-256(atributos) — la misma
    /// operación PKCS#11 (CKM_RSA_PKCS + prefijo DigestInfo) usada en el resto del sistema.
    /// </param>
    public static byte[] Firmar(
        byte[] contenidoCubierto,
        X509Certificate2 certificadoFirmante,
        IReadOnlyList<X509Certificate2>? cadenaCertificacion,
        Func<byte[], byte[]> firmarRsaPkcs1)
    {
        var parser = new BcX509CertificateParser();
        BcX509Certificate certificadoBc = parser.ReadCertificate(certificadoFirmante.RawData);

        var certificados = new List<BcX509Certificate> { certificadoBc };
        if (cadenaCertificacion is not null)
        {
            foreach (var c in cadenaCertificacion)
                certificados.Add(parser.ReadCertificate(c.RawData));
        }

        var generador = new CmsSignedDataGenerator();
        generador.AddCertificates(CollectionUtilities.CreateStore(certificados));

        var algoritmoFirma = new AlgorithmIdentifier(PkcsObjectIdentifiers.Sha256WithRsaEncryption, DerNull.Instance);
        ISignatureFactory fabricaFirma = new FirmadorExternoFactory(algoritmoFirma, firmarRsaPkcs1);

        var generadorSignerInfo = new SignerInfoGeneratorBuilder()
            .Build(fabricaFirma, certificadoBc);
        generador.AddSignerInfoGenerator(generadorSignerInfo);

        var datosFirmados = generador.Generate(new CmsProcessableByteArray(contenidoCubierto), encapsulate: false);
        return datosFirmados.ContentInfo.GetDerEncoded();
    }

    /// <summary>
    /// El OID <c>id-aa-signatureTimeStampToken</c> (RFC 3161 §2.4.1 / RFC
    /// 5126 CAdES-T) — el atributo NO firmado donde va el TimeStampToken
    /// que convierte un CMS/CAdES-BES (PAdES-B) en CAdES-T (PAdES-T): sella
    /// el INSTANTE DE FIRMA con una Autoridad de Sellado de Tiempo real, no
    /// solo el /M autodeclarado por el firmante — ver RUNBOOK.md 12.14.
    /// </summary>
    private static readonly DerObjectIdentifier IdAaSignatureTimeStampToken = PkcsObjectIdentifiers.IdAASignatureTimeStampToken;

    /// <summary>
    /// Los bytes exactos que hay que sellar con la TSA (ver
    /// SecureSign.Tsa.ClienteTsaRfc3161.SellarAsync): RFC 3161 exige
    /// sellar el HASH del VALOR DE LA FIRMA (el campo <c>signature</c> del
    /// SignerInfo), no el hash del documento — esto es lo que ata el sello
    /// de tiempo a ESTA firma concreta, no al contenido en general.
    /// </summary>
    public static byte[] ObtenerBytesFirma(byte[] cms)
    {
        var datosFirmados = new CmsSignedData(cms);
        var firmante = datosFirmados.GetSignerInfos().GetSigners().Cast<SignerInformation>().First();
        return firmante.GetSignature();
    }

    /// <summary>
    /// Incrusta un TimeStampToken RFC 3161 ya obtenido (ver
    /// SecureSign.Tsa.ClienteTsaRfc3161) como atributo NO firmado del
    /// SignerInfo — el CMS resultante es CAdES-T/PAdES-T real, verificable
    /// por cualquier validador estándar, no solo por SecureSign.Validator.
    /// </summary>
    public static byte[] AgregarSelloTiempo(byte[] cms, byte[] tokenTsaDer)
    {
        var datosFirmados = new CmsSignedData(cms);
        var firmantes = datosFirmados.GetSignerInfos();
        var firmante = firmantes.GetSigners().Cast<SignerInformation>().First();

        var tokenContentInfo = Org.BouncyCastle.Asn1.Cms.ContentInfo.GetInstance(Asn1Sequence.GetInstance(tokenTsaDer));
        var atributoSello = new Org.BouncyCastle.Asn1.Cms.Attribute(
            IdAaSignatureTimeStampToken, new DerSet(tokenContentInfo));

        var vector = firmante.UnsignedAttributes?.ToAsn1EncodableVector() ?? new Asn1EncodableVector();
        vector.Add(atributoSello);

        var firmanteConSello = SignerInformation.ReplaceUnsignedAttributes(firmante, new Org.BouncyCastle.Asn1.Cms.AttributeTable(vector));
        var datosFirmadosConSello = CmsSignedData.ReplaceSigners(
            datosFirmados, new SignerInformationStore(firmanteConSello));

        return datosFirmadosConSello.ContentInfo.GetDerEncoded();
    }

    /// <summary>Lee de vuelta el TimeStampToken embebido (si lo hay) — ver SecureSign.Validator/VerificadorTokenTsa.</summary>
    public static byte[]? ExtraerSelloTiempo(byte[] cms)
    {
        var datosFirmados = new CmsSignedData(cms);
        var firmante = datosFirmados.GetSignerInfos().GetSigners().Cast<SignerInformation>().First();
        var atributo = firmante.UnsignedAttributes?[IdAaSignatureTimeStampToken];
        if (atributo is null) return null;

        var valores = atributo.AttrValues;
        if (valores.Count == 0) return null;

        var contentInfo = Org.BouncyCastle.Asn1.Cms.ContentInfo.GetInstance(valores[0]);
        return contentInfo.GetDerEncoded();
    }

    private sealed class FirmadorExternoFactory(AlgorithmIdentifier algoritmo, Func<byte[], byte[]> firmar) : ISignatureFactory
    {
        public object AlgorithmDetails { get; } = algoritmo;

        public IStreamCalculator<IBlockResult> CreateCalculator() => new Calculador(firmar);
    }

    private sealed class Calculador(Func<byte[], byte[]> firmar) : IStreamCalculator<IBlockResult>
    {
        private readonly MemoryStream _buffer = new();

        public Stream Stream => _buffer;

        public IBlockResult GetResult() => new ResultadoBloque(firmar(_buffer.ToArray()));
    }

    private sealed class ResultadoBloque(byte[] valor) : IBlockResult
    {
        public byte[] Collect() => valor;

        public int Collect(byte[] buf, int off)
        {
            Buffer.BlockCopy(valor, 0, buf, off, valor.Length);
            return valor.Length;
        }

        public int Collect(Span<byte> output)
        {
            valor.CopyTo(output);
            return valor.Length;
        }

        public int GetMaxResultLength() => valor.Length;
    }
}
