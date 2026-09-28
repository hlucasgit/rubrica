using System.Security.Cryptography.X509Certificates;

namespace SecureSign.Trust;

/// <summary>
/// Revocación del certificado que firma la TSL de INDECOPI (informe de preauditoría INDECOPI/IOFE, hallazgo
/// P1-02): <see cref="VerificadorFirmaTsl"/> verifica la firma XAdES y la cadena contra el ancla oficial, pero
/// deliberadamente no comprueba si ESE certificado firmante fue revocado — si INDECOPI tuviera que revocar su
/// propio certificado de firma de TSL (compromiso de llave, por ejemplo), una TSL firmada con él seguiría
/// "verificando" para siempre sin este chequeo aparte.
///
/// Reutiliza <see cref="VerificadorRevocacionCrl"/>/<see cref="VerificadorRevocacionOcsp"/> ya endurecidos
/// (verificación de firma de la CRL/respuesta OCSP contra el emisor real, caché con NextUpdate, RUNBOOK.md
/// 12.17) — es la misma verificación que <c>ValidadorCertificados</c> hace para el certificado del firmante de
/// un documento, aplicada aquí al certificado firmante de la TSL en vez de reimplementar nada.
/// </summary>
public static class VerificadorRevocacionFirmanteTsl
{
    public static async Task<ResultadoRevocacion> VerificarAsync(
        X509Certificate2 certificadoFirmante,
        X509Certificate2 emisor,
        VerificadorRevocacionOcsp verificadorOcsp,
        VerificadorRevocacionCrl verificadorCrl,
        CancellationToken ct = default)
    {
        var evidencia = new List<string>();

        var (estadoOcsp, detalleOcsp, _) = await verificadorOcsp.VerificarAsync(certificadoFirmante, emisor, ct);
        evidencia.Add($"OCSP: {estadoOcsp} — {detalleOcsp}");

        var (estadoCrl, detalleCrl, _) = await verificadorCrl.VerificarAsync(certificadoFirmante, emisor, ct);
        evidencia.Add($"CRL: {estadoCrl} — {detalleCrl}");

        return new ResultadoRevocacion(estadoOcsp, estadoCrl, evidencia);
    }
}
