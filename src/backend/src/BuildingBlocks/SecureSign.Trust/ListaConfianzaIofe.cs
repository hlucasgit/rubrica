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
/// La firma XAdES de la propia TSL se verifica en <see cref="CargarDesdeArchivoFirmado"/>
/// (ver RUNBOOK.md 12.22) — el ancla de confianza es la raíz real de
/// INDECOPI ("INDECOPI AAC RAIZ"), obtenida de la URL AIA oficial embebida
/// en el propio certificado firmante de la TSL
/// (https://recursos.indecopi.gob.pe/iofe/crt/ca_root_indecopi.crt),
/// deliberadamente SEPARADA de las raíces DNIe de <c>AlmacenRaicesConfiables</c>
/// — son jerarquías PKI distintas (quién puede firmar la TSL no tiene nada
/// que ver con quién puede emitir un DNIe) y mezclarlas ampliaría sin
/// querer el conjunto de raíces confiables para validar certificados de
/// firmante.
/// </summary>
public sealed class ListaConfianzaIofe
{
    private static readonly XNamespace NsTsl = "http://uri.etsi.org/02231/v2#";

    private const string TipoServicioCaQc = "http://uri.etsi.org/TrstSvc/Svctype/CA/QC";
    private const string EstadoBajoSupervision = "http://uri.etsi.org/TrstSvc/eSigDir-1999-93-EC-TrustedList/Svcstatus/undersupervision";

    public IReadOnlyList<ServicioAcreditadoIofe> ServiciosAcreditados { get; }
    public DateTimeOffset CargadaEn { get; }

    /// <summary>true solo si esta instancia se cargó vía <see cref="CargarDesdeArchivoFirmado"/> y su firma XAdES verificó contra un ancla de confianza real.</summary>
    public bool FirmaVerificada { get; }

    private ListaConfianzaIofe(IReadOnlyList<ServicioAcreditadoIofe> servicios, DateTimeOffset cargadaEn, bool firmaVerificada)
    {
        ServiciosAcreditados = servicios;
        CargadaEn = cargadaEn;
        FirmaVerificada = firmaVerificada;
    }

    /// <summary>
    /// Carga la TSL SIN verificar su firma XAdES — uso: pruebas con
    /// fixtures sintéticos sin firmar (ver TslDePruebaHelper). El código de
    /// producción real debe usar <see cref="CargarDesdeArchivoFirmado"/>.
    /// </summary>
    public static ListaConfianzaIofe CargarDesdeArchivo(string rutaXml) =>
        new(ParsearServicios(XDocument.Load(rutaXml)), DateTimeOffset.UtcNow, firmaVerificada: false);

    /// <summary>
    /// Carga la TSL Y verifica su firma XAdES-BES (envolvente, XML-DSig
    /// estándar) contra <paramref name="raizConfiableFirmaTsl"/> antes de
    /// confiar en una sola línea del contenido — fail closed: si la firma
    /// no verifica, lanza en vez de devolver una lista parcial o sin marcar.
    /// </summary>
    /// <exception cref="InvalidOperationException">La TSL no trae firma XAdES, o su firma no verifica contra el ancla de confianza dada.</exception>
    public static ListaConfianzaIofe CargarDesdeArchivoFirmado(string rutaXml, System.Security.Cryptography.X509Certificates.X509Certificate2 raizConfiableFirmaTsl)
    {
        VerificadorFirmaTsl.VerificarOLanzar(rutaXml, raizConfiableFirmaTsl);
        var doc = XDocument.Load(rutaXml);
        return new ListaConfianzaIofe(ParsearServicios(doc), DateTimeOffset.UtcNow, firmaVerificada: true);
    }

    private static List<ServicioAcreditadoIofe> ParsearServicios(XDocument doc)
    {
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

        return servicios;
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
