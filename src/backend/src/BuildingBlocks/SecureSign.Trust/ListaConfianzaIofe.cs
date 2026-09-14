using System.Xml.Linq;

namespace SecureSign.Trust;

/// <summary>
/// La Trust Service Status List (TSL) oficial de INDECOPI para la IOFE
/// peruana — formato ETSI TS 119 612, publicada en
/// https://iofe.indecopi.gob.pe/TSL/tsl-pe.xml. Contiene, entre otros
/// servicios, las Entidades de Certificación actualmente acreditadas
/// ("bajo supervisión") junto con sus certificados — es la fuente oficial
/// para responder "¿esta cadena de certificación pertenece a la IOFE?".
///
/// LIMITACIÓN CONOCIDA (documentada, no oculta): esta clase NO verifica la
/// firma XAdES de la propia TSL. ETSI TS 119 612 recomienda validar esa
/// firma contra un "pointer"/ancla de confianza publicado por separado por
/// la Comisión Europea (o, en este caso, contra el certificado del propio
/// operador del esquema de INDECOPI) antes de confiar en el contenido. Se
/// mitiga parcialmente descargando el archivo solo por HTTPS desde el
/// dominio oficial y cacheándolo, pero la verificación criptográfica de la
/// firma de la TSL queda como trabajo pendiente (ver informe de
/// preauditoría INDECOPI/IOFE, sección 11).
/// </summary>
public sealed class ListaConfianzaIofe
{
    private static readonly XNamespace NsTsl = "http://uri.etsi.org/02231/v2#";

    private const string TipoServicioCaQc = "http://uri.etsi.org/TrstSvc/Svctype/CA/QC";
    private const string EstadoBajoSupervision = "http://uri.etsi.org/TrstSvc/eSigDir-1999-93-EC-TrustedList/Svcstatus/undersupervision";

    public IReadOnlyList<ServicioAcreditadoIofe> ServiciosAcreditados { get; }
    public DateTimeOffset CargadaEn { get; }

    private ListaConfianzaIofe(IReadOnlyList<ServicioAcreditadoIofe> servicios, DateTimeOffset cargadaEn)
    {
        ServiciosAcreditados = servicios;
        CargadaEn = cargadaEn;
    }

    public static ListaConfianzaIofe CargarDesdeArchivo(string rutaXml)
    {
        var doc = XDocument.Load(rutaXml);
        var servicios = new List<ServicioAcreditadoIofe>();

        foreach (var servicioXml in doc.Descendants(NsTsl + "TSPService"))
        {
            var info = servicioXml.Element(NsTsl + "ServiceInformation");
            if (info is null) continue;

            string? tipo = info.Element(NsTsl + "ServiceTypeIdentifier")?.Value;
            string? estado = info.Element(NsTsl + "ServiceStatus")?.Value;
            if (tipo != TipoServicioCaQc || estado != EstadoBajoSupervision) continue;

            string nombre = info.Element(NsTsl + "ServiceName")
                ?.Elements(NsTsl + "Name")
                .FirstOrDefault(n => (string?)n.Attribute(XNamespace.Xml + "lang") == "es")
                ?.Value?.Trim() ?? "(sin nombre)";

            foreach (var certXml in info.Descendants(NsTsl + "X509Certificate"))
            {
                try
                {
                    byte[] der = Convert.FromBase64String(certXml.Value);
                    servicios.Add(new ServicioAcreditadoIofe(nombre, der));
                }
                catch (FormatException)
                {
                    // Entrada malformada en la TSL — se ignora esta, no toda la lista.
                }
            }
        }

        return new ListaConfianzaIofe(servicios, DateTimeOffset.UtcNow);
    }

    /// <summary>¿Algún certificado de la cadena de confianza (de la hoja hacia arriba) es exactamente uno que la TSL lista como acreditado y bajo supervisión?</summary>
    public ServicioAcreditadoIofe? BuscarEnCadena(IEnumerable<byte[]> certificadosDeLaCadena)
    {
        foreach (var candidato in certificadosDeLaCadena)
        {
            var encontrado = ServiciosAcreditados.FirstOrDefault(s => s.CertificadoDer.AsSpan().SequenceEqual(candidato));
            if (encontrado is not null) return encontrado;
        }
        return null;
    }
}

public sealed record ServicioAcreditadoIofe(string Nombre, byte[] CertificadoDer);
