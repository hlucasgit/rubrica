using System.Formats.Asn1;
using System.Security.Cryptography.X509Certificates;

namespace SecureSign.Trust;

/// <summary>
/// Política opcional sobre ExtendedKeyUsage y CertificatePolicies del certificado del firmante (informe de
/// preauditoría INDECOPI/IOFE, sección 8: "no basta encontrar 'FIR' — falta verificar EKU/CertificatePolicies").
/// Por defecto <b>informativa</b>: el motor SIEMPRE lee y reporta ambas extensiones, pero no rechaza nada.
///
/// Fuente oficial encontrada (RUNBOOK.md 12.49): "Política General de Certificación ECERNEP PERU" v4.0
/// (RENIEC, <c>https://pki.reniec.gob.pe/docs/docsrepo/CP_ECERNEP_PERU_CA_ROOT_3_v4-0-.pdf</c>), perfil
/// "Class 3 FIR ALTO" (el que corresponde al DNIe para firma) — <c>Certificate Policies</c> trae DOS
/// identificadores: el propio de RENIEC (<c>1.3.6.1.4.1.35300.2.1.3.1.0.101.1000.0</c>, "Política General de
/// Certificación") y el genérico ETSI EN 319 411-1 <c>0.4.0.2042.1.2</c> ("NCP+ con QSCD" — Normalized
/// Certificate Policy con dispositivo cualificado de creación de firma, el chip del DNIe). El mismo perfil
/// declara <c>ExtendedKeyUsage: EmailProtection (1.3.6.1.5.5.7.3.4)</c> con Obligatorio=No, Crítica=No — el
/// propio documento de RENIEC confirma que ese EKU NO es obligatorio ni específico de firma en el perfil de
/// firma, lo que respalda con una fuente citable la decisión ya tomada en RUNBOOK.md 12.9/12.34 de no exigirlo.
/// `appsettings.json` de Signature.Api ya trae <c>0.4.0.2042.1.2</c> configurado en
/// <see cref="OidsPoliticaPermitidos"/> (reporta Cumple/NoCumple en la evidencia), con <see cref="Exigir"/> en
/// false — la Política General de Certificación cubre solo ECERNEP/RENIEC, no necesariamente cada Entidad de
/// Certificación acreditada de la IOFE, y no se ha podido probar en vivo contra un DNIe físico con esta
/// configuración en esta sesión (Docker no disponible); exigir sigue siendo una decisión pendiente de
/// confirmación operativa, no de falta de fuente.
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
