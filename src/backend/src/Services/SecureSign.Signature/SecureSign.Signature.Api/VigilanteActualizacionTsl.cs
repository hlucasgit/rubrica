using System.Security.Cryptography.X509Certificates;
using SecureSign.Trust;

namespace SecureSign.Signature.Api;

/// <summary>
/// Recarga en caliente de la TSL de INDECOPI dentro de un servicio YA corriendo (RUNBOOK.md 12.57, informe de
/// preauditoría INDECOPI/IOFE, hallazgo P2-01 — cierra el límite documentado en RUNBOOK.md 12.52: el actualizador
/// de línea de comandos solo tocaba el archivo, y un servicio en marcha seguía con la TSL que cargó al arrancar
/// hasta el próximo reinicio).
///
/// Deshabilitado por defecto (<c>ConfianzaIofe:ActualizacionAutomatica:Habilitada</c>) — salir a buscar una URL
/// externa periódicamente es una decisión operativa que el operador debe habilitar a propósito, igual que
/// <c>ConfianzaIofe:FallarSiTslVencida</c>. Reutiliza <see cref="ActualizadorTsl"/> tal cual (misma descarga a
/// archivo temporal + verificación completa + intercambio atómico que ya usa la herramienta de línea de comandos)
/// — la única diferencia es que, si la descarga reemplaza el archivo, también publica la nueva lista en
/// <see cref="IProveedorListaConfianzaIofe"/> para que <see cref="ValidadorCertificados"/> (Scoped) y
/// <see cref="VigilanteVigenciaTsl"/> la vean sin reiniciar el proceso.
/// </summary>
public sealed class VigilanteActualizacionTsl(
    IProveedorListaConfianzaIofe proveedor,
    ActualizadorTsl actualizador,
    ILogger<VigilanteActualizacionTsl> registro,
    Uri urlDescarga,
    string rutaArchivoTsl,
    X509Certificate2 raizConfiableFirmaTsl,
    bool habilitada,
    TimeProvider? reloj = null)
    : BackgroundService
{
    public static readonly TimeSpan Intervalo = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken detener)
    {
        if (!habilitada)
        {
            registro.LogInformation("Actualización automática de la TSL deshabilitada (ConfianzaIofe:ActualizacionAutomatica:Habilitada=false) — se usará solo la TSL cargada al arrancar hasta un reinicio o una actualización manual del archivo.");
            return;
        }

        var tiempo = reloj ?? TimeProvider.System;
        try
        {
            // Primer chequeo inmediato al arrancar (no esperar Intervalo completo) — luego uno cada Intervalo.
            await EjecutarUnaVezAsync(detener);
            using var temporizador = new PeriodicTimer(Intervalo, tiempo);
            while (await temporizador.WaitForNextTickAsync(detener))
                await EjecutarUnaVezAsync(detener);
        }
        catch (OperationCanceledException) { /* apagado normal */ }
    }

    private async Task EjecutarUnaVezAsync(CancellationToken ct)
    {
        ResultadoDescargaTsl resultado;
        try
        {
            resultado = await actualizador.ActualizarAsync(urlDescarga, rutaArchivoTsl, raizConfiableFirmaTsl, proveedor.Actual, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            registro.LogWarning(ex, "Actualización automática de la TSL — error inesperado, se conserva la TSL vigente.");
            return;
        }

        switch (resultado.Resultado)
        {
            case ResultadoActualizacionTsl.Actualizada:
                registro.LogInformation("Actualización automática de la TSL — {Detalle}", resultado.Detalle);
                if (resultado.NuevaLista is not null) proveedor.Reemplazar(resultado.NuevaLista);
                break;
            case ResultadoActualizacionTsl.SinCambios:
                registro.LogInformation("Actualización automática de la TSL — {Detalle}", resultado.Detalle);
                break;
            default:
                registro.LogWarning("Actualización automática de la TSL — no aplicada ({Resultado}): {Detalle}", resultado.Resultado, resultado.Detalle);
                break;
        }
    }
}
