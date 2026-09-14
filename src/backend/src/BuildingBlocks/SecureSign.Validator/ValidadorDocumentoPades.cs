using SecureSign.Pades;
using SecureSign.Trust;
using SecureSign.Tsa;

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

        // Ver informe de preauditoría INDECOPI/IOFE, hallazgo P1 (TSA): si
        // esta firma trae un TimeStampToken RFC 3161 real embebido (PAdES-T
        // — ver SecureSign.Pades.CmsBuilder.AgregarSelloTiempo), se verifica
        // aquí su consistencia interna y se deja constancia en el
        // expediente, aunque todavía no se use como base de la validación
        // de confianza (ver comentario de InstanteFirmaConfiable).
        DateTimeOffset? instanteSello = null;
        string? autoridadSello = null;
        if (verificacion.TokenTsaDer is { } tokenDer)
        {
            var verificacionTsa = VerificadorTokenTsa.Verificar(tokenDer);
            if (verificacionTsa.FirmaTokenValida)
            {
                instanteSello = verificacionTsa.GenTime;
                autoridadSello = verificacionTsa.AutoridadEmisora;
                evidencia.Add($"Sello de tiempo RFC 3161 embebido (PAdES-T): OK, token consistente — TSA \"{autoridadSello}\", genTime {instanteSello:o}. La cadena de confianza de la TSA todavía no se verifica (ver RUNBOOK.md 12.14).");
            }
            else
            {
                evidencia.Add($"Sello de tiempo RFC 3161 embebido (PAdES-T): PRESENTE PERO INVÁLIDO — {verificacionTsa.Error ?? "la firma del token no verifica contra su propio certificado"}. Se ignora para esta validación.");
            }
        }

        if (!verificacion.Valido)
        {
            evidencia.Add($"Verificación criptográfica (ByteRange + CMS): FALLÓ — {verificacion.Error}");
            return new ResultadoValidacionFirmaPades(
                verificacion.NombreFirma, false, verificacion.Certificado, verificacion.InstanteFirmaDeclarado,
                false, instanteSello, autoridadSello, null, evidencia, verificacion.Error);
        }
        evidencia.Add("Verificación criptográfica (ByteRange + CMS): OK — la firma corresponde exactamente a este documento y a este certificado.");

        if (verificacion.Certificado is null)
        {
            const string error = "El CMS verificó pero no se pudo extraer el certificado del firmante — no se puede evaluar la confianza IOFE.";
            evidencia.Add(error);
            return new ResultadoValidacionFirmaPades(
                verificacion.NombreFirma, true, null, verificacion.InstanteFirmaDeclarado, false, instanteSello, autoridadSello, null, evidencia, error);
        }

        // El instante de VALIDACIÓN de vigencia/revocación sigue siendo el
        // /M autodeclarado (ver comentario extenso de InstanteFirmaConfiable
        // sobre por qué un sello de tiempo estructuralmente válido no basta
        // todavía para usarse como base de esa decisión) — nunca "ahora"
        // (penalizaría una firma antigua cuyo certificado ya venció, o
        // aceptaría una firma sobre un certificado revocado DESPUÉS de firmar).
        DateTimeOffset instanteValidacion = verificacion.InstanteFirmaDeclarado ?? DateTimeOffset.UtcNow;
        evidencia.Add(verificacion.InstanteFirmaDeclarado is { } m
            ? $"Instante de validación de vigencia/revocación: {m:o} (declarado por el firmante en /M)."
            : $"Instante de validación de vigencia/revocación: {instanteValidacion:o} (no se encontró /M legible en la firma; se usó el instante de esta verificación).");

        var validacionCertificado = await validadorCertificados.ValidarAsync(verificacion.Certificado, instanteValidacion, ct);
        evidencia.AddRange(validacionCertificado.Evidencia);

        return new ResultadoValidacionFirmaPades(
            verificacion.NombreFirma, true, verificacion.Certificado, verificacion.InstanteFirmaDeclarado,
            InstanteFirmaConfiable: false, instanteSello, autoridadSello, validacionCertificado, evidencia, Error: null);
    }
}
