using System.Formats.Asn1;
using System.Security.Cryptography.X509Certificates;

namespace SecureSign.Trust;

/// <summary>
/// Política opcional sobre ExtendedKeyUsage y CertificatePolicies del certificado del firmante (informe de
/// preauditoría INDECOPI/IOFE, sección 8: "no basta encontrar 'FIR' — falta verificar EKU/CertificatePolicies").
/// Por defecto <b>informativa</b>: el motor SIEMPRE lee y reporta ambas extensiones, pero no rechaza nada — no
/// existe todavía una referencia confiable del OID de política IOFE, y el DNIe real declara un EKU ("Secure
/// Email") que no es específico de firma de documentos, así que exigirlos hoy produciría falsos rechazos
/// (RUNBOOK.md 12.9). Cuando se tenga la lista oficial, basta con llenar los OID y poner <see cref="Exigir"/>.
/// </summary>
public sealed class OpcionesPoliticaCertificado
{
    public const string SeccionConfiguracion = "PoliticaCertificado";

    /// <summary>OID de política (CertificatePolicies, 2.5.29.32) aceptados. Vacío = no se evalúa.</summary>
    public string[] OidsPoliticaPermitidos { get; set; } = [];

    /// <summary>OID de ExtendedKeyUsage aceptados. Vacío = no se evalúa.</summary>
    public string[] OidsEkuPermitidos { get; set; } = [];

    /// <summary>
    /// false (por defecto): el resultado de la evaluación solo se REGISTRA. true: un certificado que no cumple
    /// las listas configuradas deja de tener propósito válido y por tanto no es confiable.
    /// </summary>
    public bool Exigir { get; set; }
}

public enum EstadoPolitica
{
    /// <summary>No hay listas configuradas: solo se reportan las extensiones que el certificado declara.</summary>
    SoloInformativa,

    /// <summary>Hay listas configuradas y el certificado cumple todas.</summary>
    Cumple,

    /// <summary>Hay listas configuradas y el certificado no cumple al menos una.</summary>
    NoCumple,
}

/// <param name="ExtendedKeyUsages">OID declarados en ExtendedKeyUsage (vacío si el certificado no la trae).</param>
/// <param name="PoliticasCertificado">OID declarados en CertificatePolicies (vacío si el certificado no la trae).</param>
public sealed record DatosPoliticaCertificado(
    IReadOnlyList<string> ExtendedKeyUsages,
    IReadOnlyList<string> PoliticasCertificado,
    EstadoPolitica Estado);

public static class AnalizadorPoliticaCertificado
{
    private const string OidCertificatePolicies = "2.5.29.32";

    public static DatosPoliticaCertificado Analizar(X509Certificate2 certificado, OpcionesPoliticaCertificado? opciones)
    {
        var eku = certificado.Extensions.OfType<X509EnhancedKeyUsageExtension>()
            .SelectMany(e => e.EnhancedKeyUsages.Cast<System.Security.Cryptography.Oid>())
            .Select(o => o.Value!)
            .ToList();
        var politicas = LeerPoliticas(certificado);

        bool evaluaPolitica = opciones is { OidsPoliticaPermitidos.Length: > 0 };
        bool evaluaEku = opciones is { OidsEkuPermitidos.Length: > 0 };
        if (!evaluaPolitica && !evaluaEku)
            return new DatosPoliticaCertificado(eku, politicas, EstadoPolitica.SoloInformativa);

        bool cumplePolitica = !evaluaPolitica || politicas.Intersect(opciones!.OidsPoliticaPermitidos, StringComparer.Ordinal).Any();
        bool cumpleEku = !evaluaEku || eku.Intersect(opciones!.OidsEkuPermitidos, StringComparer.Ordinal).Any();
        return new DatosPoliticaCertificado(eku, politicas, cumplePolitica && cumpleEku ? EstadoPolitica.Cumple : EstadoPolitica.NoCumple);
    }

    /// <summary>
    /// CertificatePolicies ::= SEQUENCE SIZE (1..MAX) OF PolicyInformation;
    /// PolicyInformation ::= SEQUENCE { policyIdentifier OBJECT IDENTIFIER, policyQualifiers ... OPTIONAL } (RFC 5280 §4.2.1.4).
    /// Solo se extraen los identificadores; los calificadores (CPS, avisos) se ignoran. Una extensión mal formada
    /// devuelve lista vacía en vez de lanzar: nunca debe tumbar la validación de un certificado.
    /// </summary>
    private static IReadOnlyList<string> LeerPoliticas(X509Certificate2 certificado)
    {
        var extension = certificado.Extensions.Cast<X509Extension>().FirstOrDefault(e => e.Oid?.Value == OidCertificatePolicies);
        if (extension is null) return [];

        try
        {
            var resultado = new List<string>();
            var lista = new AsnReader(extension.RawData, AsnEncodingRules.DER).ReadSequence();
            while (lista.HasData)
            {
                var informacion = lista.ReadSequence();
                resultado.Add(informacion.ReadObjectIdentifier());
            }
            return resultado;
        }
        catch (Exception)
        {
            return [];
        }
    }
}
