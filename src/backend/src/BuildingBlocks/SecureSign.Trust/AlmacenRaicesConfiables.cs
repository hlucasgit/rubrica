using System.Security.Cryptography.X509Certificates;

namespace SecureSign.Trust;

/// <summary>
/// Anclas de confianza para CONSTRUIR la cadena X.509 (mecánica PKIX pura) —
/// deliberadamente SEPARADO de <see cref="ListaConfianzaIofe"/>, que decide
/// si esa cadena ya construida pertenece a una entidad ACREDITADA por
/// INDECOPI. La razón de la separación: la TSL de INDECOPI lista el
/// certificado operativo de cada Entidad de Certificación (p. ej.
/// "CN=ECEP-RENIEC"), que casi nunca es la raíz autofirmada real de esa
/// jerarquía (en el caso de RENIEC, la raíz autofirmada es
/// "CN=ECERNEP PERU CA ROOT 3", un nivel por encima) — <see cref="X509Chain"/>
/// con <see cref="X509ChainTrustMode.CustomRootTrust"/> exige un ancla
/// realmente autofirmada para poder cerrar la cadena, así que aquí se
/// guardan esas raíces reales, y la comprobación "¿está en la TSL?" se hace
/// aparte, sobre cualquier certificado de la cadena ya construida.
///
/// Se cargan desde archivos .cer/.crt en un directorio — deliberadamente
/// simple (no hay UI de gestión): agregar una raíz nueva es copiar el
/// archivo al directorio configurado. Ver RUNBOOK.md para cómo se obtuvo la
/// raíz de RENIEC (http://crt.reniec.gob.pe/crt/sha2/ecernep.crt).
/// </summary>
public sealed class AlmacenRaicesConfiables
{
    public IReadOnlyList<X509Certificate2> Raices { get; }

    private AlmacenRaicesConfiables(IReadOnlyList<X509Certificate2> raices) => Raices = raices;

    public static AlmacenRaicesConfiables CargarDesdeDirectorio(string directorio)
    {
        var raices = new List<X509Certificate2>();
        if (Directory.Exists(directorio))
        {
            foreach (var archivo in Directory.EnumerateFiles(directorio)
                         .Where(f => f.EndsWith(".cer", StringComparison.OrdinalIgnoreCase)
                                  || f.EndsWith(".crt", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    var cert = new X509Certificate2(archivo);
                    if (cert.Subject != cert.Issuer)
                        throw new InvalidOperationException($"'{archivo}' no es un certificado autofirmado (Subject != Issuer) — no sirve como ancla de confianza.");
                    raices.Add(cert);
                }
                catch (Exception ex) when (ex is not InvalidOperationException)
                {
                    throw new InvalidOperationException($"No se pudo cargar la raíz de confianza '{archivo}'.", ex);
                }
            }
        }

        return new AlmacenRaicesConfiables(raices);
    }
}
