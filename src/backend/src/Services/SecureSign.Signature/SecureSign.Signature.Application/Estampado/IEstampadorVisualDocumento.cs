namespace SecureSign.Signature.Application.Estampado;

/// <summary>Un sello visual a dibujar sobre el documento — ver PosicionFirma.</summary>
public sealed record MarcaVisualFirma(
    int NumeroPagina, double X, double Y, double Ancho, double Alto,
    string NombreFirmante, DateTimeOffset FirmadoEn, string CodigoVerificacion);

/// <summary>
/// Dibuja la representación visual de cada firma (nombre, fecha, código de
/// verificación) en la posición que cada firmante eligió en el visor.
///
/// SIMPLIFICACIÓN IMPORTANTE: esto NO es una firma PAdES real. No se
/// incrusta un diccionario de firma (/Sig) ni se hace la actualización
/// incremental que preserva intacto el byte-range criptográficamente
/// firmado (ISO 32000-2). Es un sello visual de cortesía generado aparte:
/// el hash que realmente se firma (ver Documento.Hash / FirmarDocumentoHandler)
/// sigue siendo siempre el de los bytes ORIGINALES del documento, nunca el
/// de este archivo estampado. Implementar PAdES real queda como trabajo
/// futuro — ver docs/03-legal-normativo/marco-legal-peru.md.
/// </summary>
public interface IEstampadorVisualDocumento
{
    /// <returns>
    /// El documento con las marcas dibujadas, o <c>null</c> si
    /// <paramref name="tipoContenido"/> no es un formato soportado (por
    /// ahora solo PDF) — en ese caso el llamador debe servir el contenido
    /// original sin cambios.
    /// </returns>
    byte[]? Estampar(byte[] contenidoOriginal, string tipoContenido, IReadOnlyList<MarcaVisualFirma> marcas);
}
