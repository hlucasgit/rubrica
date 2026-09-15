using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using SecureSign.Signature.Application.Estampado;
using SecureSign.Signature.Infrastructure.Estampado;

namespace SecureSign.UnitTests.Pades;

/// <summary>
/// Smoke test del camino real de PdfSharpCore + SixLabors.Fonts que ejercita
/// este repositorio (dibujar rectángulo + texto) — escrito al fijar
/// SixLabors.ImageSharp a la última versión 2.x (Apache 2.0, sin licencia
/// comercial) para resolver las vulnerabilidades HIGH/Moderate de la 1.0.4
/// transitiva (ver SecureSign.Pades.csproj). Nada en el producto llama
/// XImage/decodifica imágenes rasterizadas, pero este test prueba en vivo
/// que el pipeline de dibujo de PdfSharpCore sigue funcionando con la
/// versión nueva, en vez de asumirlo solo porque compila.
/// </summary>
public sealed class EstampadoVisualTests
{
    private static byte[] CrearPdfMinimo()
    {
        using var documento = new PdfDocument();
        documento.AddPage();
        using var buffer = new MemoryStream();
        documento.Save(buffer);
        return buffer.ToArray();
    }

    [Fact]
    public void Estampar_dibuja_marca_visual_y_produce_un_pdf_reabrible()
    {
        byte[] original = CrearPdfMinimo();
        var marca = new MarcaVisualFirma(
            NumeroPagina: 1, X: 0.1, Y: 0.1, Ancho: 0.3, Alto: 0.08,
            NombreFirmante: "Juan Pérez de Prueba", FirmadoEn: DateTimeOffset.UtcNow,
            CodigoVerificacion: "ABC123");

        IEstampadorVisualDocumento estampador = new EstampadorVisualDocumentoPdf();
        byte[]? resultado = estampador.Estampar(original, "application/pdf", [marca]);

        Assert.NotNull(resultado);
        Assert.NotEqual(original.Length, resultado!.Length);

        using var reabierto = PdfReader.Open(new MemoryStream(resultado), PdfDocumentOpenMode.ReadOnly);
        Assert.Equal(1, reabierto.PageCount);
    }

    [Fact]
    public void Estampar_sin_marcas_devuelve_el_original_sin_tocar_ImageSharp()
    {
        byte[] original = CrearPdfMinimo();
        IEstampadorVisualDocumento estampador = new EstampadorVisualDocumentoPdf();
        byte[]? resultado = estampador.Estampar(original, "application/pdf", []);

        Assert.Same(original, resultado);
    }

    [Fact]
    public void Estampar_con_contenido_no_pdf_devuelve_null()
    {
        IEstampadorVisualDocumento estampador = new EstampadorVisualDocumentoPdf();
        byte[]? resultado = estampador.Estampar([1, 2, 3], "image/png", []);

        Assert.Null(resultado);
    }
}
