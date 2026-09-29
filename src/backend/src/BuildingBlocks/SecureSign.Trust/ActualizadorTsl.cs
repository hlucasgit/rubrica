using System.Security.Cryptography.X509Certificates;

namespace SecureSign.Trust;

public enum ResultadoActualizacionTsl
{
    /// <summary>Se descargó una TSL más nueva, verificó todo, y reemplazó a la anterior.</summary>
    Actualizada,

    /// <summary>La descarga verificó, pero no es más nueva que la actual — se conserva la actual sin tocarla.</summary>
    SinCambios,

    /// <summary>La firma XAdES, la cadena, la cobertura o el SigningCertificate de la descarga no verifican.</summary>
    RechazadaFirmaInvalida,

    /// <summary>El certificado que firma la TSL descargada está revocado.</summary>
    RechazadaRevocada,

    /// <summary>No se pudo completar (red, E/S, URL no HTTPS, etc.) — nunca se tocó el archivo vigente.</summary>
    RechazadaError,
}

public sealed record ResultadoDescargaTsl(ResultadoActualizacionTsl Resultado, string Detalle, ListaConfianzaIofe? NuevaLista);

/// <summary>
/// Actualizador seguro de la TSL de INDECOPI (informe de preauditoría INDECOPI/IOFE, hallazgo P2-01): descarga a
/// un archivo temporal EN EL MISMO DIRECTORIO que el archivo vigente (para que el reemplazo final sea un
/// <see cref="File.Move(string, string, bool)"/> atómico dentro del mismo volumen, nunca una escritura directa
/// sobre el archivo que otros hilos puedan estar leyendo a medias), verifica TODO lo que
/// <see cref="ListaConfianzaIofe.CargarDesdeArchivoFirmado"/> ya verifica (firma XAdES, cadena contra el ancla,
/// cobertura del documento completo, SigningCertificate contra KeyInfo — RUNBOOK.md 12.38/12.51) más la
/// revocación del certificado firmante (<see cref="VerificadorRevocacionFirmanteTsl"/>, RUNBOOK.md 12.50), y
/// rechaza cualquier TSL que no sea estrictamente MÁS RECIENTE que la vigente (anti-retroceso: sin esto, un
/// atacante o un mirror desactualizado podría reintroducir una TSL vieja con una entidad ya retirada, o firmada
/// por un certificado que hoy ya está revocado pero no lo estaba cuando esa TSL vieja se firmó).
///
/// Nunca reemplaza el archivo vigente si CUALQUIERA de esas verificaciones falla — el archivo vigente se
/// conserva sin tocar, y además se guarda una copia de la última TSL reemplazada (<c>.anterior</c>) para
/// rollback manual.
/// </summary>
public sealed class ActualizadorTsl(HttpClient http, VerificadorRevocacionOcsp verificadorOcsp, VerificadorRevocacionCrl verificadorCrl)
{
    public async Task<ResultadoDescargaTsl> ActualizarAsync(
        Uri urlTsl, string rutaArchivoActual, X509Certificate2 raizConfiableFirmaTsl, ListaConfianzaIofe? listaActual, CancellationToken ct = default)
    {
        if (urlTsl.Scheme != Uri.UriSchemeHttps)
            return new(ResultadoActualizacionTsl.RechazadaError, $"La URL de descarga debe ser HTTPS (era '{urlTsl.Scheme}') — se rechaza sin descargar nada.", null);

        string directorio = Path.GetDirectoryName(Path.GetFullPath(rutaArchivoActual))
            ?? throw new ArgumentException("No se pudo determinar el directorio del archivo TSL vigente.", nameof(rutaArchivoActual));
        string rutaTemporal = Path.Combine(directorio, $".tsl-descarga-{Guid.NewGuid():N}.xml");

        try
        {
            byte[] bytes;
            try { bytes = await http.GetByteArrayAsync(urlTsl, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return new(ResultadoActualizacionTsl.RechazadaError, $"No se pudo descargar la TSL desde {urlTsl}: {ex.Message}", null);
            }

            await File.WriteAllBytesAsync(rutaTemporal, bytes, ct);

            ListaConfianzaIofe candidata;
            try { candidata = ListaConfianzaIofe.CargarDesdeArchivoFirmado(rutaTemporal, raizConfiableFirmaTsl); }
            catch (InvalidOperationException ex)
            {
                return new(ResultadoActualizacionTsl.RechazadaFirmaInvalida, $"La TSL descargada no verifica — se descarta sin tocar la TSL vigente: {ex.Message}", null);
            }

            if (listaActual?.EmitidaEn is { } emitidaActual && candidata.EmitidaEn is { } emitidaNueva && emitidaNueva <= emitidaActual)
                return new(ResultadoActualizacionTsl.SinCambios,
                    $"La TSL descargada (emitida {emitidaNueva:o}) no es más reciente que la vigente (emitida {emitidaActual:o}) — se conserva la vigente.", null);

            if (candidata.CertificadoFirmante is null || candidata.RaizConfiableFirmaTsl is null)
                return new(ResultadoActualizacionTsl.RechazadaError, "La TSL descargada no expuso su certificado firmante tras verificar — no se puede comprobar su revocación, se descarta.", null);

            var revocacion = await VerificadorRevocacionFirmanteTsl.VerificarAsync(
                candidata.CertificadoFirmante, candidata.RaizConfiableFirmaTsl, verificadorOcsp, verificadorCrl, ct);
            if (revocacion.Combinado == EstadoRevocacion.Revoked)
                return new(ResultadoActualizacionTsl.RechazadaRevocada,
                    "El certificado que firma la TSL descargada está revocado — se descarta sin tocar la TSL vigente. " + string.Join(" | ", revocacion.Evidencia), null);

            if (File.Exists(rutaArchivoActual))
                File.Copy(rutaArchivoActual, rutaArchivoActual + ".anterior", overwrite: true);
            File.Move(rutaTemporal, rutaArchivoActual, overwrite: true);

            return new(ResultadoActualizacionTsl.Actualizada,
                $"TSL actualizada: emitida {candidata.EmitidaEn:o}, próxima actualización {candidata.ProximaActualizacion:o}.", candidata);
        }
        finally
        {
            try { if (File.Exists(rutaTemporal)) File.Delete(rutaTemporal); } catch { /* mejor esfuerzo — el archivo ya pudo haberse movido */ }
        }
    }
}
