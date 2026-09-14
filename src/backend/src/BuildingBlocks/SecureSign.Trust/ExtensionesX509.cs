using System.Security.Cryptography.X509Certificates;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.X509;

namespace SecureSign.Trust;

/// <summary>
/// .NET no expone tipados el contenido de Authority Information Access
/// (RFC 5280 §4.2.2.1) ni de CRL Distribution Points (§4.2.1.13) — solo el
/// texto formateado (<see cref="X509Extension.Format"/>). Aquí se
/// reparsean sus bytes ASN.1 crudos con BouncyCastle, que sí los modela.
/// </summary>
internal static class ExtensionesX509
{
    private const string OidAuthorityInfoAccess = "1.3.6.1.5.5.7.1.1";
    private const string OidCrlDistributionPoints = "2.5.29.31";

    public static string? ObtenerUrlEmisorCa(X509Certificate2 certificado) =>
        ObtenerUrlPorMetodoAia(certificado, AccessDescription.IdADCAIssuers);

    public static string? ObtenerUrlOcsp(X509Certificate2 certificado) =>
        ObtenerUrlPorMetodoAia(certificado, AccessDescription.IdADOcsp);

    private static string? ObtenerUrlPorMetodoAia(X509Certificate2 certificado, DerObjectIdentifier metodo)
    {
        var ext = certificado.Extensions[OidAuthorityInfoAccess];
        if (ext is null) return null;

        try
        {
            var aia = AuthorityInformationAccess.GetInstance(Asn1Object.FromByteArray(ext.RawData));
            foreach (var descripcion in aia.GetAccessDescriptions())
            {
                if (!descripcion.AccessMethod.Equals(metodo)) continue;
                string? uri = ExtraerUri(descripcion.AccessLocation);
                if (uri is not null) return uri;
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    public static IReadOnlyList<string> ObtenerUrlsCrl(X509Certificate2 certificado)
    {
        var ext = certificado.Extensions[OidCrlDistributionPoints];
        if (ext is null) return Array.Empty<string>();

        var urls = new List<string>();
        try
        {
            var puntos = CrlDistPoint.GetInstance(Asn1Object.FromByteArray(ext.RawData));
            foreach (var punto in puntos.GetDistributionPoints())
            {
                if (punto.DistributionPointName?.Type != DistributionPointName.FullName) continue;
                var nombres = GeneralNames.GetInstance(punto.DistributionPointName.Name);
                foreach (var nombre in nombres.GetNames())
                {
                    string? uri = ExtraerUri(nombre);
                    if (uri is not null) urls.Add(uri);
                }
            }
        }
        catch
        {
            return urls;
        }

        return urls;
    }

    private static string? ExtraerUri(GeneralName nombre)
    {
        // tag 6 = uniformResourceIdentifier (RFC 5280 GeneralName).
        if (nombre.TagNo != GeneralName.UniformResourceIdentifier) return null;
        return DerIA5String.GetInstance(nombre.Name).GetString();
    }
}
