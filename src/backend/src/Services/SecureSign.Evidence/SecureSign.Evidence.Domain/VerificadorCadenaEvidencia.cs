namespace SecureSign.Evidence.Domain;

public sealed record ResultadoVerificacionCadena(bool CadenaValida, int TotalEventos, Guid? PrimerEventoRotoId);

/// <summary>
/// Recorre la cadena completa de un tenant y confirma que cada eslabón
/// referencia correctamente al anterior. Esta es la operación que respalda
/// el portal de verificación pública (verificar.securesign.pe) y el
/// endpoint GET /api/validacion/{codigo}.
/// </summary>
public static class VerificadorCadenaEvidencia
{
    public static ResultadoVerificacionCadena Verificar(IReadOnlyList<EventoEvidencia> cadenaOrdenadaCronologicamente)
    {
        string? hashAnteriorEsperado = null;

        foreach (var evento in cadenaOrdenadaCronologicamente)
        {
            if (!evento.EsConsistenteCon(hashAnteriorEsperado))
                return new ResultadoVerificacionCadena(false, cadenaOrdenadaCronologicamente.Count, evento.Id);

            hashAnteriorEsperado = evento.HashEvento;
        }

        return new ResultadoVerificacionCadena(true, cadenaOrdenadaCronologicamente.Count, null);
    }
}
