using System.Security.Cryptography.X509Certificates;

namespace SecureSign.Trust;

/// <summary>
/// Motor de confianza IOFE (ver informe de preauditoría INDECOPI/IOFE,
/// hallazgo P0-01: "RÚBRICA PODRÍA AFIRMAR que la firma fue creada con la
/// clave privada correspondiente al certificado, pero todavía no puede
/// afirmar de manera robusta que ese certificado era válido, no estaba
/// revocado, tenía propósito de firma y pertenecía a una cadena confiable
/// de la IOFE"). Esta clase es exactamente esa segunda afirmación.
///
/// Deliberadamente NO verifica que la firma criptográfica en sí sea
/// matemáticamente correcta — eso ya lo hacen <c>VerificadorFirmaExterna</c>
/// y <c>PdfSignatureVerifier</c>. Esta clase solo responde: "¿en el
/// instante de validación, este certificado era de fiar?".
/// </summary>
public sealed class ValidadorCertificados(
    AlmacenRaicesConfiables raices,
    ListaConfianzaIofe listaIofe,
    DescargadorCertificadosIntermedios descargadorIntermedios,
    VerificadorRevocacionCrl verificadorCrl,
    VerificadorRevocacionOcsp verificadorOcsp)
{
    public async Task<ResultadoValidacionCertificado> ValidarAsync(
        X509Certificate2 certificado, DateTimeOffset instante, CancellationToken ct = default)
    {
        var evidencia = new List<string>();

        bool vigente = instante >= certificado.NotBefore && instante <= certificado.NotAfter;
        evidencia.Add(vigente
            ? $"Vigente: {certificado.NotBefore:o} a {certificado.NotAfter:o}, validado en {instante:o}."
            : $"NO vigente en {instante:o} (vigencia declarada: {certificado.NotBefore:o} a {certificado.NotAfter:o}).");

        bool propositoValido = TienePropositoDeFirma(certificado, evidencia);

        var intermedios = await descargadorIntermedios.DescargarCadenaAsync(certificado, ct: ct);
        var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        foreach (var raiz in raices.Raices) chain.ChainPolicy.CustomTrustStore.Add(raiz);
        foreach (var intermedio in intermedios) chain.ChainPolicy.ExtraStore.Add(intermedio);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck; // la revocación se hace aparte (OCSP/CRL propios).
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
        chain.ChainPolicy.VerificationTime = instante.UtcDateTime;

        bool cadenaValida = chain.Build(certificado);
        evidencia.Add(cadenaValida
            ? $"Cadena X.509 válida hasta una raíz de confianza propia ({chain.ChainElements.Count} certificado(s))."
            : $"Cadena X.509 inválida: {string.Join(", ", chain.ChainStatus.Select(s => s.Status))}.");

        var certificadosDeLaCadena = chain.ChainElements.Cast<X509ChainElement>().Select(e => e.Certificate.RawData);
        var servicioIofe = listaIofe.BuscarEnCadena(certificadosDeLaCadena);
        bool raizConfiableIofe = servicioIofe is not null;
        evidencia.Add(raizConfiableIofe
            ? $"La cadena incluye un servicio acreditado y bajo supervisión en la TSL de IOFE: \"{servicioIofe!.Nombre}\"."
            : "Ningún certificado de la cadena aparece en la TSL de IOFE como servicio acreditado y bajo supervisión.");

        // El emisor directo para OCSP/CRL es el primer certificado por encima de la hoja
        // en la cadena ya construida (no necesariamente el mismo que descargó AIA, por si
        // la cadena se cerró usando ExtraStore/CustomTrustStore en vez de la descarga).
        X509Certificate2? emisorDirecto = chain.ChainElements.Count > 1 ? chain.ChainElements[1].Certificate : null;

        var (estadoOcsp, detalleOcsp) = emisorDirecto is not null
            ? await verificadorOcsp.VerificarAsync(certificado, emisorDirecto, ct)
            : (EstadoRevocacion.Unavailable, "No se pudo determinar el certificado emisor para consultar OCSP.");
        var (estadoCrl, detalleCrl) = await verificadorCrl.VerificarAsync(certificado, emisorDirecto, ct);
        evidencia.Add($"OCSP: {estadoOcsp} — {detalleOcsp}");
        evidencia.Add($"CRL: {estadoCrl} — {detalleCrl}");

        var revocacion = new ResultadoRevocacion(estadoOcsp, estadoCrl, evidencia);

        return new ResultadoValidacionCertificado(
            CertificadoVigente: vigente,
            CadenaValida: cadenaValida,
            RaizConfiableIofe: raizConfiableIofe,
            PropositoValido: propositoValido,
            Revocacion: revocacion,
            InstanteValidacion: instante,
            Evidencia: evidencia,
            Error: null);
    }

    /// <summary>
    /// Un certificado de firma real debe declarar el bit NonRepudiation
    /// (contentCommitment) en KeyUsage y NO ser un certificado CA — esto se
    /// calibró contra un certificado DNIe real de RENIEC (ver RUNBOOK.md
    /// 12.9): trae KeyUsage=NonRepudiation y, curiosamente, un
    /// ExtendedKeyUsage de "Secure Email" — por eso NO se exige un EKU
    /// específico de firma de documentos (no todas las EC lo declaran así).
    /// </summary>
    private static bool TienePropositoDeFirma(X509Certificate2 certificado, List<string> evidencia)
    {
        var basicConstraints = certificado.Extensions.OfType<X509BasicConstraintsExtension>().FirstOrDefault();
        if (basicConstraints?.CertificateAuthority == true)
        {
            evidencia.Add("Propósito inválido: el certificado tiene BasicConstraints CA=true (es un certificado de Entidad de Certificación, no de firmante).");
            return false;
        }

        var keyUsage = certificado.Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault();
        if (keyUsage is null)
        {
            evidencia.Add("Propósito indeterminado: el certificado no declara la extensión KeyUsage.");
            return false;
        }

        bool tieneNoRepudio = keyUsage.KeyUsages.HasFlag(X509KeyUsageFlags.NonRepudiation);
        bool tieneFirmaDigital = keyUsage.KeyUsages.HasFlag(X509KeyUsageFlags.DigitalSignature);
        evidencia.Add($"KeyUsage: {keyUsage.KeyUsages} (NonRepudiation={tieneNoRepudio}, DigitalSignature={tieneFirmaDigital}).");

        return tieneNoRepudio || tieneFirmaDigital;
    }
}
