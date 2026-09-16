using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;

namespace SecureSign.UnitTests.Pades;

/// <summary>
/// Fixtures de PDF mínimo + firma de prueba, compartidas por las pruebas de
/// <see cref="PdfSignaturePlaceholder"/>, <see cref="PdfDssWriter"/> y
/// <see cref="PdfSelloArchivoTests"/> — antes triplicadas, una copia por
/// archivo de prueba.
/// </summary>
internal static class PdfDePruebaHelper
{
    internal static byte[] CrearPdfMinimo(string texto)
    {
        using var documento = new PdfDocument();
        var pagina = documento.AddPage();
        using var graficos = XGraphics.FromPdfPage(pagina);
        graficos.DrawString(texto, new XFont("Arial", 14), XBrushes.Black, new XPoint(40, 60));
        using var buffer = new MemoryStream();
        documento.Save(buffer);
        return buffer.ToArray();
    }

    internal static byte[] FirmarConCertificadoDePrueba(byte[] pdfActual, CertificadoDePruebaHelper.ParFirmante firmante)
    {
        var preparado = SecureSign.Pades.PdfSignaturePlaceholder.Preparar(
            pdfActual, firmante.Certificado.GetNameInfo(X509NameType.SimpleName, false), "Prueba automatizada", DateTimeOffset.UtcNow);
        byte[] cms = SecureSign.Pades.CmsBuilder.Firmar(preparado.ContenidoCubierto, firmante.Certificado, cadenaCertificacion: null,
            datos => firmante.Rsa.SignData(datos, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        return SecureSign.Pades.PdfSignaturePlaceholder.Inyectar(preparado, cms);
    }

    /// <summary>
    /// El CMS de la ÚLTIMA firma del PDF, tal como <c>FirmarLocalHandler</c>
    /// ya lo tiene disponible tras verificar la firma (ver <c>ResultadoVerificacionPades.CmsDer</c>)
    /// — <c>PdfDssWriter.AgregarDss</c> lo recibe como parámetro en vez de
    /// volver a extraerlo, así que las pruebas que lo llaman directamente
    /// necesitan este mismo paso.
    /// </summary>
    internal static byte[] CmsDeLaUltimaFirma(byte[] pdf) =>
        SecureSign.Pades.PdfSignatureVerifier.VerificarUltima(pdf).CmsDer!;
}
