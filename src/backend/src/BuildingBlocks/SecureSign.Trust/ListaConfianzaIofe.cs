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

    /// <summary>
    /// El certificado que firmó esta TSL, y la raíz contra la que se verificó — solo presentes cuando
    /// <see cref="FirmaVerificada"/> es true. Expuestos para que <see cref="VerificadorRevocacionFirmanteTsl"/>
    /// (RUNBOOK.md 12.50) pueda comprobar su revocación sin volver a leer ni re-verificar el archivo XML.
    /// </summary>
    public System.Security.Cryptography.X509Certificates.X509Certificate2? CertificadoFirmante { get; }

    /// <inheritdoc cref="CertificadoFirmante"/>
    public System.Security.Cryptography.X509Certificates.X509Certificate2? RaizConfiableFirmaTsl { get; }

    /// <summary>Fecha de emisión declarada por la TSL (<c>ListIssueDateTime</c>); null si no la trae o no se pudo leer.</summary>
    public DateTimeOffset? EmitidaEn { get; }

    /// <summary>
    /// Fecha en que el emisor promete publicar la siguiente TSL (<c>NextUpdate</c>). Pasada esa fecha la lista
    /// deja de estar garantizada como vigente (ETSI TS 119 612): un servicio acreditado pudo haber sido
    /// retirado o suspendido sin que este archivo lo refleje. Null si no la trae.
    /// </summary>
    public DateTimeOffset? ProximaActualizacion { get; }

    private ListaConfianzaIofe(IReadOnlyList<ServicioAcreditadoIofe> servicios, DateTimeOffset cargadaEn, bool firmaVerificada,
        DateTimeOffset? emitidaEn = null, DateTimeOffset? proximaActualizacion = null,
        System.Security.Cryptography.X509Certificates.X509Certificate2? certificadoFirmante = null,
        System.Security.Cryptography.X509Certificates.X509Certificate2? raizConfiableFirmaTsl = null)
    {
        ServiciosAcreditados = servicios;
        CargadaEn = cargadaEn;
        FirmaVerificada = firmaVerificada;
        EmitidaEn = emitidaEn;
        ProximaActualizacion = proximaActualizacion;
        CertificadoFirmante = certificadoFirmante;
        RaizConfiableFirmaTsl = raizConfiableFirmaTsl;
    }

    /// <summary>Estado de vigencia de la lista en <paramref name="ahora"/>; <paramref name="aviso"/> es la anticipación con que se marca "por vencer".</summary>
    public EstadoVigenciaTsl Vigencia(DateTimeOffset ahora, TimeSpan aviso) =>
        ProximaActualizacion is not { } proxima ? EstadoVigenciaTsl.Desconocida
        : ahora > proxima ? EstadoVigenciaTsl.Vencida
        : proxima - ahora <= aviso ? EstadoVigenciaTsl.PorVencer
        : EstadoVigenciaTsl.Vigente;

    /// <summary>Línea de evidencia para el expediente de validación (siempre la misma forma, para poder auditarla).</summary>
    public string DescribirVigencia(DateTimeOffset ahora, TimeSpan aviso)
    {
        string emitida = EmitidaEn is { } e ? e.ToString("yyyy-MM-dd") : "(no declarada)";
        string proxima = ProximaActualizacion is { } p ? p.ToString("yyyy-MM-dd") : "(no declarada)";
        return $"TSL de IOFE: emitida {emitida}, próxima actualización {proxima} — {Vigencia(ahora, aviso).ToString().ToUpperInvariant()}.";
    }

    private static (DateTimeOffset? Emitida, DateTimeOffset? Proxima) LeerVigencia(XDocument doc)
    {
        static DateTimeOffset? Fecha(string? texto) =>
            DateTimeOffset.TryParse(texto, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var f) ? f : null;

        var esquema = doc.Descendants(NsTsl + "SchemeInformation").FirstOrDefault();
        return (Fecha(esquema?.Element(NsTsl + "ListIssueDateTime")?.Value),
                Fecha(esquema?.Element(NsTsl + "NextUpdate")?.Element(NsTsl + "dateTime")?.Value));
    }

    /// <summary>
    /// Carga la TSL SIN verificar su firma XAdES — uso: pruebas con
    /// fixtures sintéticos sin firmar (ver TslDePruebaHelper). El código de
    /// producción real debe usar <see cref="CargarDesdeArchivoFirmado"/>.
    /// </summary>
    public static ListaConfianzaIofe CargarDesdeArchivo(string rutaXml)
    {
        var doc = XDocument.Load(rutaXml);
        var (emitida, proxima) = LeerVigencia(doc);
        return new(ParsearServicios(doc), DateTimeOffset.UtcNow, firmaVerificada: false, emitida, proxima);
    }

    /// <summary>
    /// Carga la TSL Y verifica su firma XAdES-BES (envolvente, XML-DSig
    /// estándar) contra <paramref name="raizConfiableFirmaTsl"/> antes de
    /// confiar en una sola línea del contenido — fail closed: si la firma
    /// no verifica, lanza en vez de devolver una lista parcial o sin marcar.
    /// </summary>
    /// <exception cref="InvalidOperationException">La TSL no trae firma XAdES, o su firma no verifica contra el ancla de confianza dada.</exception>
    public static ListaConfianzaIofe CargarDesdeArchivoFirmado(string rutaXml, System.Security.Cryptography.X509Certificates.X509Certificate2 raizConfiableFirmaTsl)
    {
        var certificadoFirmante = VerificadorFirmaTsl.VerificarOLanzar(rutaXml, raizConfiableFirmaTsl);
        var doc = XDocument.Load(rutaXml);
        var (emitida, proxima) = LeerVigencia(doc);
        return new ListaConfianzaIofe(ParsearServicios(doc), DateTimeOffset.UtcNow, firmaVerificada: true, emitida, proxima,
            certificadoFirmante, raizConfiableFirmaTsl);
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

public enum EstadoVigenciaTsl
{
    /// <summary>La lista no declara <c>NextUpdate</c>: no se puede saber.</summary>
    Desconocida,

    Vigente,

    /// <summary>Falta poco para <c>NextUpdate</c>: hay que descargar la TSL nueva.</summary>
    PorVencer,

    /// <summary>Pasó <c>NextUpdate</c>: la lista ya no está garantizada como vigente.</summary>
    Vencida,
}
