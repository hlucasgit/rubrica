namespace SecureSign.Trust;

/// <summary>
/// Material de validación que viaja DENTRO del documento (PAdES-LT, DSS —
/// RUNBOOK.md 12.24/12.33): certificados intermedios, CRLs y respuestas OCSP en
/// DER. Es información NO confiable por sí misma: cada pieza se verifica igual
/// que si viniera de la red (la firma de la CRL/OCSP contra el emisor del
/// certificado, la cadena contra las raíces configuradas y la TSL) — que el
/// material esté embebido solo permite validar sin red, no confiar más.
/// </summary>
public sealed record MaterialRevocacionEmbebido(
    IReadOnlyList<byte[]> CertificadosDer,
    IReadOnlyList<byte[]> CrlsDer,
    IReadOnlyList<byte[]> OcspsDer);
