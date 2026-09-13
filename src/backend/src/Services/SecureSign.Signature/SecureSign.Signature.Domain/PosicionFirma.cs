using SecureSign.Domain.Primitives;

namespace SecureSign.Signature.Domain;

/// <summary>
/// Ubicación, elegida por el propio firmante en el visor (estilo Firma
/// Perú/ONPE: arrastrar el recuadro de firma sobre la página antes de
/// firmar), donde debe aparecer la representación visual de su firma.
///
/// Coordenadas normalizadas (0..1) relativas al tamaño de la página, con
/// origen arriba-a-la-izquierda (como en CSS/canvas del visor) — así el
/// mismo valor sirve sin importar a qué resolución se renderizó la página
/// en el navegador. <see cref="NumeroPagina"/> es 1-based.
///
/// IMPORTANTE: esto NO es lo que se firma criptográficamente. El hash que
/// firma el Servicio Criptográfico siempre corresponde a los bytes
/// originales del documento (ver Documento.Hash) — esta posición solo
/// gobierna dónde se dibuja el sello visual de cortesía en el PDF
/// resultante (ver IEstampadorVisualDocumento).
/// </summary>
public sealed record PosicionFirma(int NumeroPagina, double X, double Y, double Ancho, double Alto)
{
    public static Result<PosicionFirma> Crear(int numeroPagina, double x, double y, double ancho, double alto)
    {
        if (numeroPagina < 1)
            return Result.Fallido<PosicionFirma>("El número de página debe ser 1 o mayor.");

        if (x < 0 || x > 1 || y < 0 || y > 1)
            return Result.Fallido<PosicionFirma>("Las coordenadas X/Y deben estar normalizadas entre 0 y 1.");

        if (ancho <= 0 || ancho > 1 || alto <= 0 || alto > 1)
            return Result.Fallido<PosicionFirma>("El ancho y alto del recuadro de firma deben estar entre 0 (excluido) y 1.");

        if (x + ancho > 1.0001 || y + alto > 1.0001)
            return Result.Fallido<PosicionFirma>("El recuadro de firma se sale de los límites de la página.");

        return Result.Exitoso(new PosicionFirma(numeroPagina, x, y, ancho, alto));
    }
}
