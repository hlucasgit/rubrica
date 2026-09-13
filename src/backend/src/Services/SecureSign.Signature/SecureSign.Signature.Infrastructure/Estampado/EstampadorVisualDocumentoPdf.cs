using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf.IO;
using SecureSign.Signature.Application.Estampado;

namespace SecureSign.Signature.Infrastructure.Estampado;

/// <summary>
/// Implementación con PdfSharpCore (MIT) — ver limitaciones deliberadas en
/// IEstampadorVisualDocumento. Solo entiende PDF; cualquier otro tipo de
/// contenido devuelve <c>null</c> para que el llamador sirva el original.
/// </summary>
public sealed class EstampadorVisualDocumentoPdf : IEstampadorVisualDocumento
{
    public byte[]? Estampar(byte[] contenidoOriginal, string tipoContenido, IReadOnlyList<MarcaVisualFirma> marcas)
    {
        if (!tipoContenido.Contains("pdf", StringComparison.OrdinalIgnoreCase))
            return null;

        if (marcas.Count == 0)
            return contenidoOriginal;

        using var entrada = new MemoryStream(contenidoOriginal);
        using var documento = PdfReader.Open(entrada, PdfDocumentOpenMode.Modify);

        foreach (var marca in marcas)
        {
            if (marca.NumeroPagina < 1 || marca.NumeroPagina > documento.PageCount)
                continue;

            var pagina = documento.Pages[marca.NumeroPagina - 1];
            using var gfx = XGraphics.FromPdfPage(pagina, XGraphicsPdfPageOptions.Append);

            double anchoPagina = pagina.Width.Point;
            double altoPagina = pagina.Height.Point;
            double anchoPx = marca.Ancho * anchoPagina;
            double altoPx = marca.Alto * altoPagina;
            double xPx = marca.X * anchoPagina;
            // NO hay que invertir Y: XGraphics.FromPdfPage ya expone un
            // sistema de coordenadas con origen arriba-izquierda y Y
            // creciendo hacia abajo (como GDI+/canvas), igual que el visor
            // en el navegador — es PdfSharpCore quien traduce esto a las
            // coordenadas PDF nativas (origen abajo-izquierda) al dibujar.
            // Invertir aquí además duplicaba la conversión y dejaba el
            // sello cerca del borde superior en vez de donde se eligió.
            double yPx = marca.Y * altoPagina;

            var recuadro = new XRect(xPx, yPx, anchoPx, altoPx);
            gfx.DrawRectangle(new XPen(XColors.DarkBlue, 1), recuadro);

            // "Liberation Sans" (no "Arial"): la imagen del contenedor no
            // trae fuentes propietarias — ver el Dockerfile de Signature.Api.
            var fuente = new XFont("Liberation Sans", 7);
            var lineas = new[]
            {
                $"Firmado digitalmente por {marca.NombreFirmante}",
                $"{marca.FirmadoEn:dd/MM/yyyy HH:mm} UTC",
                $"Verificar: {marca.CodigoVerificacion}"
            };

            double alturaLinea = fuente.GetHeight() + 1;
            for (int i = 0; i < lineas.Length; i++)
            {
                var filaTexto = new XRect(recuadro.X + 3, recuadro.Y + 2 + i * alturaLinea, recuadro.Width - 6, alturaLinea);
                gfx.DrawString(lineas[i], fuente, XBrushes.DarkBlue, filaTexto, XStringFormats.TopLeft);
            }
        }

        using var salida = new MemoryStream();
        documento.Save(salida);
        return salida.ToArray();
    }
}
