using System.Security.Cryptography;
using SecureSign.Pades;
using static SecureSign.UnitTests.Pades.PdfDePruebaHelper;

namespace SecureSign.UnitTests.Pades;

/// <summary>
/// Matriz de pruebas del informe de preauditoría INDECOPI/IOFE, sección 13
/// — filas de PAdES/integridad documental que se pueden probar de forma
/// 100% determinística, sin red ni hardware PKCS#11 (las filas que sí la
/// necesitan —vigencia/revocación/TSL/OCSP/CRL real, PKCS#11 con
/// tarjeta/SoftHSM— quedan fuera de esta suite; ver RUNBOOK.md 12.15).
/// </summary>
public sealed class PdfSignaturePlaceholderTests
{
    /// <summary>Fila del informe: "PAdES con una firma → Todas válidas".</summary>
    [Fact]
    public void Una_firma_es_valida()
    {
        using var firmante = CertificadoDePruebaHelper.GenerarFirmante("Firmante Único");
        byte[] pdf = FirmarConCertificadoDePrueba(CrearPdfMinimo("documento de prueba"), firmante);

        var resultados = PdfSignatureVerifier.VerificarTodas(pdf);

        Assert.Single(resultados);
        Assert.True(resultados[0].Valido, resultados[0].Error);
        Assert.Null(resultados[0].Error);
    }

    /// <summary>
    /// Criterio de aceptación EXACTO del hallazgo P0-02 del informe: firmar A,
    /// validar A; firmar B incremental, validar A y B; firmar C incremental,
    /// validar A, B y C — ninguna firma anterior se invalida al agregar la
    /// siguiente. Filas del informe: "PAdES con dos firmas" y "PAdES con tres
    /// firmas → Ambas/Las tres válidas".
    /// </summary>
    [Fact]
    public void Tres_firmas_incrementales_dejan_las_tres_validas()
    {
        byte[] pdf = CrearPdfMinimo("documento con varios firmantes");

        using var firmanteA = CertificadoDePruebaHelper.GenerarFirmante("Firmante A");
        pdf = FirmarConCertificadoDePrueba(pdf, firmanteA);
        Assert.All(PdfSignatureVerifier.VerificarTodas(pdf), r => Assert.True(r.Valido));

        using var firmanteB = CertificadoDePruebaHelper.GenerarFirmante("Firmante B");
        pdf = FirmarConCertificadoDePrueba(pdf, firmanteB);
        var trasB = PdfSignatureVerifier.VerificarTodas(pdf);
        Assert.Equal(2, trasB.Count);
        Assert.All(trasB, r => Assert.True(r.Valido));

        using var firmanteC = CertificadoDePruebaHelper.GenerarFirmante("Firmante C");
        pdf = FirmarConCertificadoDePrueba(pdf, firmanteC);
        var trasC = PdfSignatureVerifier.VerificarTodas(pdf);
        Assert.Equal(3, trasC.Count);
        Assert.All(trasC, r => Assert.True(r.Valido));
    }

    /// <summary>Regresión del hallazgo P0-02/12.11: hasta 20 firmantes, no solo 3 — ver RUNBOOK.md 12.11.</summary>
    [Fact]
    public void Veinte_firmas_sucesivas_quedan_todas_validas()
    {
        byte[] pdf = CrearPdfMinimo("documento con veinte firmantes");

        for (int i = 1; i <= 20; i++)
        {
            using var firmante = CertificadoDePruebaHelper.GenerarFirmante($"Firmante {i:D2}");
            pdf = FirmarConCertificadoDePrueba(pdf, firmante);
        }

        var resultados = PdfSignatureVerifier.VerificarTodas(pdf);
        Assert.Equal(20, resultados.Count);
        Assert.All(resultados, r => Assert.True(r.Valido));
    }

    /// <summary>Fila del informe: "PDF alterado después de firmar → Firma inválida".</summary>
    [Fact]
    public void Documento_alterado_despues_de_firmar_invalida_la_firma()
    {
        using var firmante = CertificadoDePruebaHelper.GenerarFirmante("Firmante Alterado");
        byte[] pdf = FirmarConCertificadoDePrueba(CrearPdfMinimo("contenido original"), firmante);

        // Altera un byte del CONTENIDO (antes del área firmada final, dentro
        // del stream de texto) — cualquier cambio a los bytes cubiertos por
        // el /ByteRange debe invalidar la firma.
        byte[] pdfAlterado = (byte[])pdf.Clone();
        int posicionAlterar = Array.IndexOf(pdfAlterado, (byte)'o'); // primera 'o' del contenido, bien dentro del ByteRange
        pdfAlterado[posicionAlterar] = (byte)'0';

        var resultado = PdfSignatureVerifier.VerificarUltima(pdfAlterado);

        Assert.False(resultado.Valido);
    }

    /// <summary>Fila del informe: certificado que no corresponde a la firma → no debe verificar.</summary>
    [Fact]
    public void Firma_no_verifica_contra_un_certificado_ajeno()
    {
        using var firmanteReal = CertificadoDePruebaHelper.GenerarFirmante("Firmante Real");
        using var firmanteAjeno = CertificadoDePruebaHelper.GenerarFirmante("Firmante Ajeno");

        var preparado = PdfSignaturePlaceholder.Preparar(CrearPdfMinimo("contenido"), "Firmante Real", "Prueba", DateTimeOffset.UtcNow);

        // Firma con la llave del firmante REAL pero declara el certificado AJENO en el CMS.
        byte[] cms = CmsBuilder.Firmar(preparado.ContenidoCubierto, firmanteAjeno.Certificado, cadenaCertificacion: null,
            datos => firmanteReal.Rsa.SignData(datos, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        byte[] pdf = PdfSignaturePlaceholder.Inyectar(preparado, cms);

        var resultado = PdfSignatureVerifier.VerificarUltima(pdf);

        Assert.False(resultado.Valido);
    }

    /// <summary>El nombre e instante de firma deben leerse correctamente en TODAS las firmas, no solo la primera — ver RUNBOOK.md 12.12 (bug real encontrado con la firma #2 en adelante).</summary>
    [Fact]
    public void Nombre_e_instante_de_firma_se_leen_correctamente_en_todas_las_firmas()
    {
        byte[] pdf = CrearPdfMinimo("documento");
        using var firmanteA = CertificadoDePruebaHelper.GenerarFirmante("Firmante A");
        pdf = FirmarConCertificadoDePrueba(pdf, firmanteA);
        using var firmanteB = CertificadoDePruebaHelper.GenerarFirmante("Firmante B");
        pdf = FirmarConCertificadoDePrueba(pdf, firmanteB);

        var resultados = PdfSignatureVerifier.VerificarTodas(pdf);

        Assert.Equal(2, resultados.Count);
        Assert.All(resultados, r => Assert.NotNull(r.NombreFirma));
        Assert.All(resultados, r => Assert.NotNull(r.InstanteFirmaDeclarado));
    }
}
