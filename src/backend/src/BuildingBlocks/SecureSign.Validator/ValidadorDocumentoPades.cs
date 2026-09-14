using SecureSign.Pades;
using SecureSign.Trust;

namespace SecureSign.Validator;

/// <summary>
/// Validador independiente de documentos PAdES — ver informe de
/// preauditoría INDECOPI/IOFE, hallazgo P0-05: "no debe depender del mismo
/// camino de código que genera la firma para concluir que ella es válida".
///
/// Este componente NUNCA referencia <c>PdfSignaturePlaceholder</c> (el
/// generador): solo compone dos piezas ya independientes entre sí —
/// <see cref="PdfSignatureVerifier"/> (verificación matemática pura del
/// CMS/ByteRange, sin conocer cómo se generó el PDF) y
/// <see cref="ValidadorCertificados"/> (motor de confianza IOFE, sin
/// conocer nada de PDF). Un PDF firmado por CUALQUIER otro software que
/// produzca PAdES/CMS estándar se valida exactamente igual.
/// </summary>
public sealed class ValidadorDocumentoPades(ValidadorCertificados validadorCertificados)
{
    public async Task<ResultadoValidacionDocumentoPades> ValidarAsync(byte[] pdf, CancellationToken ct = default)
    {
        var verificaciones = PdfSignatureVerifier.VerificarTodas(pdf);
        var firmas = new List<ResultadoValidacionFirmaPades>(verificaciones.Count);

        foreach (var verificacion in verificaciones)
            firmas.Add(await ValidarUnaAsync(verificacion, ct));

        return new ResultadoValidacionDocumentoPades(verificaciones.Count, firmas);
    }

    private async Task<ResultadoValidacionFirmaPades> ValidarUnaAsync(ResultadoVerificacionPades verificacion, CancellationToken ct)
    {
        var evidencia = new List<string>();

        if (!verificacion.Valido)
        {
            evidencia.Add($"Verificación criptográfica (ByteRange + CMS): FALLÓ — {verificacion.Error}");
            return new ResultadoValidacionFirmaPades(
                verificacion.NombreFirma, false, verificacion.Certificado, verificacion.InstanteFirmaDeclarado,
                false, null, evidencia, verificacion.Error);
        }
        evidencia.Add("Verificación criptográfica (ByteRange + CMS): OK — la firma corresponde exactamente a este documento y a este certificado.");

        if (verificacion.Certificado is null)
        {
            const string error = "El CMS verificó pero no se pudo extraer el certificado del firmante — no se puede evaluar la confianza IOFE.";
            evidencia.Add(error);
            return new ResultadoValidacionFirmaPades(
                verificacion.NombreFirma, true, null, verificacion.InstanteFirmaDeclarado, false, null, evidencia, error);
        }

        // Sin TSA real todavía (ver RUNBOOK.md 12.9), el mejor instante
        // disponible para evaluar vigencia/revocación es el /M que el propio
        // firmante declaró — nunca "ahora" (que penalizaría injustamente una
        // firma antigua cuyo certificado ya venció, o aceptaría una firma
        // sobre un certificado que se revocó DESPUÉS de firmar). Se deja
        // constancia explícita de que ese instante no está probado.
        DateTimeOffset instanteValidacion = verificacion.InstanteFirmaDeclarado ?? DateTimeOffset.UtcNow;
        evidencia.Add(verificacion.InstanteFirmaDeclarado is { } m
            ? $"Instante de validación: {m:o} (declarado por el firmante en /M — NO probado por una TSA independiente; ver RUNBOOK.md 12.9)."
            : $"Instante de validación: {instanteValidacion:o} (no se encontró /M legible en la firma; se usó el instante de esta verificación).");

        var validacionCertificado = await validadorCertificados.ValidarAsync(verificacion.Certificado, instanteValidacion, ct);
        evidencia.AddRange(validacionCertificado.Evidencia);

        return new ResultadoValidacionFirmaPades(
            verificacion.NombreFirma, true, verificacion.Certificado, verificacion.InstanteFirmaDeclarado,
            InstanteFirmaConfiable: false, validacionCertificado, evidencia, Error: null);
    }
}
