using Org.BouncyCastle.Tsp;

namespace SecureSign.Tsa;

/// <summary>Resultado de verificar un TimeStampToken RFC 3161 ya incrustado en un CMS/CAdES.</summary>
/// <param name="FirmaTokenValida">
/// true si la firma CMS del propio token es matemáticamente consistente con
/// el certificado de la TSA que el mismo token trae incrustado — es decir,
/// que el token no fue alterado después de que la TSA lo emitió.
/// </param>
/// <param name="CadenaTsaConfiable">
/// SIEMPRE false en esta versión: no se construye ni valida la cadena X.509
/// del certificado de la TSA contra un almacén de raíces de confianza (ver
/// limitación explícita en RUNBOOK.md 12.14) — se sabe que el token no fue
/// alterado, pero NO se verifica todavía que la propia TSA emisora sea, en
/// sí misma, una autoridad de sellado de tiempo confiable/acreditada.
/// </param>
public sealed record ResultadoVerificacionTsa(
    bool FirmaTokenValida,
    bool CadenaTsaConfiable,
    DateTimeOffset GenTime,
    string? AutoridadEmisora,
    string? Error);

public static class VerificadorTokenTsa
{
    /// <param name="tokenDer">El TimeStampToken DER-encoded, tal como se incrustó en el CMS (ver CmsBuilder.AgregarSelloTiempo/ExtraerSelloTiempo).</param>
    public static ResultadoVerificacionTsa Verificar(byte[] tokenDer)
    {
        try
        {
            var token = new TimeStampToken(new Org.BouncyCastle.Cms.CmsSignedData(tokenDer));
            var certificadoTsa = token.GetCertificates().EnumerateMatches(token.SignerID).FirstOrDefault()
                ?? throw new InvalidOperationException("El token no incluye el certificado de la TSA que lo firmó.");

            bool firmaValida;
            try
            {
                token.Validate(certificadoTsa);
                firmaValida = true;
            }
            catch (TspValidationException)
            {
                firmaValida = false;
            }

            return new ResultadoVerificacionTsa(
                firmaValida, CadenaTsaConfiable: false, token.TimeStampInfo.GenTime,
                certificadoTsa.SubjectDN?.ToString(), Error: null);
        }
        catch (Exception ex)
        {
            return new ResultadoVerificacionTsa(false, false, default, null, $"No se pudo procesar el token de sello de tiempo: {ex.Message}");
        }
    }
}
