namespace SecureSign.Crypto.Infrastructure.Pkcs11;

/// <summary>
/// Configuración del adaptador PKCS#11 real (ver ProveedorCriptograficoPkcs11).
/// Probado con el middleware de IDEMIA para el DNIe peruano
/// (idplug-pkcs11.dll) — cualquier módulo PKCS#11 estándar de otra EC
/// acreditada debería funcionar igual con solo cambiar la ruta.
/// </summary>
public sealed class Pkcs11Options
{
    public const string SeccionConfiguracion = "Pkcs11";

    /// <summary>Ruta absoluta a la librería PKCS#11 nativa (.dll en Windows, .so en Linux).</summary>
    public string RutaLibreria { get; set; } = default!;

    /// <summary>
    /// Filtro por etiqueta del certificado personal a usar como certificado
    /// de FIRMA (no de autenticación). El DNIe peruano distingue ambos con
    /// los sufijos "AUT" y "FIR" en la etiqueta — ver
    /// docs del hallazgo real en esta sesión de desarrollo.
    /// </summary>
    public string EtiquetaCertificadoFirma { get; set; } = "FIR";
}
