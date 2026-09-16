using System.Security.Cryptography;
using Org.BouncyCastle.Cms;
using SecureSign.Pades;
using SecureSign.Tsa;

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
    [Fact]
    public void Sello_incrustado_se_extrae_identico_y_verifica()
    {
        var (tsa, _) = TsaDePruebaHelper.CrearTsaDePrueba();

        using var firmante = CertificadoDePruebaHelper.GenerarFirmante("Firmante PAdES-T");
        byte[] contenido = "contenido de prueba"u8.ToArray();
        byte[] cms = CmsBuilder.Firmar(contenido, firmante.Certificado, cadenaCertificacion: null,
            datos => firmante.Rsa.SignData(datos, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

        byte[] hashFirma = SHA256.HashData(CmsBuilder.ObtenerBytesFirma(cms));
        byte[] tokenDer = TsaDePruebaHelper.SellarLocalmente(tsa, hashFirma);

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
        var (tsa, _) = TsaDePruebaHelper.CrearTsaDePrueba();

        using var firmante = CertificadoDePruebaHelper.GenerarFirmante("Firmante PAdES-T");
        byte[] contenido = "contenido cubierto"u8.ToArray();
        byte[] cms = CmsBuilder.Firmar(contenido, firmante.Certificado, cadenaCertificacion: null,
            datos => firmante.Rsa.SignData(datos, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

        byte[] tokenDer = TsaDePruebaHelper.SellarLocalmente(tsa, SHA256.HashData(CmsBuilder.ObtenerBytesFirma(cms)));
        byte[] cmsConSello = CmsBuilder.AgregarSelloTiempo(cms, tokenDer);

        var datosFirmados = new CmsSignedData(new CmsProcessableByteArray(contenido), cmsConSello);
        var firmanteCms = datosFirmados.GetSignerInfos().GetSigners().Cast<SignerInformation>().First();
        var certificadoBc = datosFirmados.GetCertificates().EnumerateMatches(firmanteCms.SignerID).First();

        Assert.True(firmanteCms.Verify(certificadoBc));
    }

    [Fact]
    public void Token_alterado_no_verifica()
    {
        var (tsa, _) = TsaDePruebaHelper.CrearTsaDePrueba();
        byte[] tokenDer = TsaDePruebaHelper.SellarLocalmente(tsa, SHA256.HashData("cualquier cosa"u8.ToArray()));

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
