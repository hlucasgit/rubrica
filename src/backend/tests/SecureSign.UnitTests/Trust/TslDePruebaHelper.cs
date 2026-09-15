using System.Xml.Linq;

namespace SecureSign.UnitTests.Trust;

/// <summary>Genera un archivo de Trust Service Status List mínimo, con la misma forma (ETSI TS 119 612) que ListaConfianzaIofe espera parsear.</summary>
internal static class TslDePruebaHelper
{
    private static readonly XNamespace Ns = "http://uri.etsi.org/02231/v2#";

    internal static string EscribirArchivoTemporal(IReadOnlyList<(string Nombre, byte[] CertificadoDer)> serviciosAcreditados)
    {
        var proveedores = serviciosAcreditados.Select(CrearProveedor).ToArray();
        var doc = new XDocument(
            new XElement(Ns + "TrustServiceStatusList",
                new XElement(Ns + "TrustServiceProviderList", proveedores)));

        string ruta = Path.Combine(Path.GetTempPath(), $"tsl-prueba-{Guid.NewGuid():N}.xml");
        doc.Save(ruta);
        return ruta;
    }

    private static XElement CrearProveedor((string Nombre, byte[] CertificadoDer) servicio)
    {
        var nombre = new XElement(Ns + "ServiceName", new XElement(Ns + "Name", new XAttribute(XNamespace.Xml + "lang", "es"), servicio.Nombre));
        var identidad = new XElement(Ns + "ServiceDigitalIdentity",
            new XElement(Ns + "DigitalId",
                new XElement(Ns + "X509Certificate", Convert.ToBase64String(servicio.CertificadoDer))));
        var estado = new XElement(Ns + "ServiceStatus", "http://uri.etsi.org/TrstSvc/eSigDir-1999-93-EC-TrustedList/Svcstatus/undersupervision");
        var tipo = new XElement(Ns + "ServiceTypeIdentifier", "http://uri.etsi.org/TrstSvc/Svctype/CA/QC");
        var informacion = new XElement(Ns + "ServiceInformation", tipo, nombre, identidad, estado);
        var servicioXml = new XElement(Ns + "TSPService", informacion);
        var servicios = new XElement(Ns + "TSPServices", servicioXml);
        return new XElement(Ns + "TrustServiceProvider", servicios);
    }
}
