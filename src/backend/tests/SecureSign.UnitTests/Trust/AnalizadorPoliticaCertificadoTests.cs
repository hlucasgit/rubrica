using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using SecureSign.Trust;

namespace SecureSign.UnitTests.Trust;

/// <summary>Lectura de EKU y CertificatePolicies y evaluación contra los OID configurados (RUNBOOK.md 12.34).</summary>
public sealed class AnalizadorPoliticaCertificadoTests
{
    private static X509Certificate2 Certificado(string[]? eku = null, string[]? politicas = null, byte[]? politicasCrudas = null)
    {
        using var rsa = RSA.Create(2048);
        var solicitud = new CertificateRequest("CN=Prueba", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        if (eku is not null)
        {
            var oids = new OidCollection();
            foreach (var o in eku) oids.Add(new Oid(o));
            solicitud.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(oids, false));
        }
        if (politicas is not null)
            solicitud.CertificateExtensions.Add(new X509Extension(new Oid("2.5.29.32"), ConstruirPoliticas(politicas), false));
        if (politicasCrudas is not null)
            solicitud.CertificateExtensions.Add(new X509Extension(new Oid("2.5.29.32"), politicasCrudas, false));
        using var conLlave = solicitud.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        return new X509Certificate2(conLlave.Export(X509ContentType.Cert));
    }

    private static byte[] ConstruirPoliticas(string[] oids)
    {
        var w = new System.Formats.Asn1.AsnWriter(System.Formats.Asn1.AsnEncodingRules.DER);
        using (w.PushSequence())
            foreach (var oid in oids)
                using (w.PushSequence())
                    w.WriteObjectIdentifier(oid);
        return w.Encode();
    }

    [Fact]
    public void Lee_los_EKU_y_las_politicas_que_declara_el_certificado()
    {
        var datos = AnalizadorPoliticaCertificado.Analizar(Certificado(["1.3.6.1.5.5.7.3.4"], ["1.2.3.4", "1.2.3.5"]), opciones: null);

        Assert.Equal(["1.3.6.1.5.5.7.3.4"], datos.ExtendedKeyUsages);
        Assert.Equal(["1.2.3.4", "1.2.3.5"], datos.PoliticasCertificado);
        Assert.Equal(EstadoPolitica.SoloInformativa, datos.Estado);
    }

    [Fact]
    public void Un_certificado_sin_extensiones_devuelve_listas_vacias()
    {
        var datos = AnalizadorPoliticaCertificado.Analizar(Certificado(), opciones: null);

        Assert.Empty(datos.ExtendedKeyUsages);
        Assert.Empty(datos.PoliticasCertificado);
    }

    [Fact]
    public void Una_extension_CertificatePolicies_mal_formada_no_lanza_y_se_lee_como_vacia()
    {
        var datos = AnalizadorPoliticaCertificado.Analizar(Certificado(politicasCrudas: [0x30, 0x03, 0x01, 0x02]), opciones: null);

        Assert.Empty(datos.PoliticasCertificado);
    }

    [Fact]
    public void Con_politica_permitida_que_coincide_cumple()
    {
        var opciones = new OpcionesPoliticaCertificado { OidsPoliticaPermitidos = ["9.9.9", "1.2.3.5"] };
        Assert.Equal(EstadoPolitica.Cumple, AnalizadorPoliticaCertificado.Analizar(Certificado(politicas: ["1.2.3.4", "1.2.3.5"]), opciones).Estado);
    }

    [Fact]
    public void Con_politica_permitida_que_no_coincide_no_cumple()
    {
        var opciones = new OpcionesPoliticaCertificado { OidsPoliticaPermitidos = ["9.9.9"] };
        Assert.Equal(EstadoPolitica.NoCumple, AnalizadorPoliticaCertificado.Analizar(Certificado(politicas: ["1.2.3.4"]), opciones).Estado);
    }

    [Fact]
    public void Con_politica_permitida_y_un_certificado_sin_politicas_no_cumple()
    {
        var opciones = new OpcionesPoliticaCertificado { OidsPoliticaPermitidos = ["9.9.9"] };
        Assert.Equal(EstadoPolitica.NoCumple, AnalizadorPoliticaCertificado.Analizar(Certificado(), opciones).Estado);
    }

    [Fact]
    public void Con_EKU_y_politica_configurados_deben_cumplirse_AMBOS()
    {
        var opciones = new OpcionesPoliticaCertificado { OidsPoliticaPermitidos = ["1.2.3.4"], OidsEkuPermitidos = ["1.3.6.1.5.5.7.3.4"] };

        Assert.Equal(EstadoPolitica.Cumple, AnalizadorPoliticaCertificado.Analizar(Certificado(["1.3.6.1.5.5.7.3.4"], ["1.2.3.4"]), opciones).Estado);
        Assert.Equal(EstadoPolitica.NoCumple, AnalizadorPoliticaCertificado.Analizar(Certificado(["1.3.6.1.5.5.7.3.2"], ["1.2.3.4"]), opciones).Estado);
    }

    /// <summary>
    /// Oráculo independiente sobre certificados REALES del repositorio (raíz ECERNEP y firmante de la TSL de
    /// INDECOPI): los OID que extrae el analizador propio deben coincidir con los que lee BouncyCastle. Se salta si no se encuentran los archivos.
    /// </summary>
    [Fact]
    public void Coincide_con_la_lectura_de_DotNet_sobre_certificados_reales_de_la_IOFE()
    {
        string? raiz = AppContext.BaseDirectory;
        while (raiz is not null && !Directory.Exists(Path.Combine(raiz, "src", "Services"))) raiz = Path.GetDirectoryName(raiz);
        if (raiz is null) return;

        var archivos = Directory.GetFiles(Path.Combine(raiz, "src", "Services", "SecureSign.Signature", "SecureSign.Signature.Api", "ConfianzaIofe"), "*.crt", SearchOption.AllDirectories);
        if (archivos.Length == 0) return;

        foreach (var archivo in archivos)
        {
            using var certificado = new X509Certificate2(archivo);
            var propia = AnalizadorPoliticaCertificado.Analizar(certificado, opciones: null).PoliticasCertificado;

            // Oráculo: BouncyCastle (independiente del código bajo prueba y sin depender del idioma del SO,
            // a diferencia de X509Extension.Format, que traduce anyPolicy a texto localizado).
            var bc = new Org.BouncyCastle.X509.X509CertificateParser().ReadCertificate(certificado.RawData);
            var valor = bc.GetExtensionValue(Org.BouncyCastle.Asn1.X509.X509Extensions.CertificatePolicies);
            var deBouncyCastle = valor is null
                ? new List<string>()
                : Org.BouncyCastle.Asn1.X509.CertificatePolicies.GetInstance(Org.BouncyCastle.X509.Extension.X509ExtensionUtilities.FromExtensionValue(valor))
                    .GetPolicyInformation().Select(p => p.PolicyIdentifier.Id).ToList();

            Assert.Equal(deBouncyCastle, propia);
        }
    }

    [Fact]
    public void Listas_vacias_significan_no_evaluar()
    {
        var opciones = new OpcionesPoliticaCertificado { Exigir = true };
        Assert.Equal(EstadoPolitica.SoloInformativa, AnalizadorPoliticaCertificado.Analizar(Certificado(), opciones).Estado);
    }
}
