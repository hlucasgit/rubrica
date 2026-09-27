using SecureSign.Trust;

namespace SecureSign.Signature.Api;

/// <summary>
/// Vuelve a evaluar la vigencia de la TSL mientras el servicio está en marcha (RUNBOOK.md 12.40): un servicio que
/// lleva meses corriendo no se entera de otro modo de que la lista venció a mitad de camino. Solo avisa (registro);
/// abortar el arranque es decisión de <c>Program.cs</c>, no de un servicio ya en marcha.
/// </summary>
public sealed class VigilanteVigenciaTsl(ListaConfianzaIofe lista, ILogger<VigilanteVigenciaTsl> registro, TimeProvider? reloj = null)
    : BackgroundService
{
    public static readonly TimeSpan Intervalo = TimeSpan.FromHours(6);

    public static void Registrar(ILogger registro, EvaluacionVigenciaTsl e)
    {
        switch (e.Nivel)
        {
            case NivelAvisoTsl.Error: registro.LogError("{Mensaje}", e.Mensaje); break;
            case NivelAvisoTsl.Advertencia: registro.LogWarning("{Mensaje}", e.Mensaje); break;
            default: registro.LogInformation("{Mensaje}", e.Mensaje); break;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken detener)
    {
        var tiempo = reloj ?? TimeProvider.System;
        using var temporizador = new PeriodicTimer(Intervalo, tiempo);
        try
        {
            // La primera evaluación ya la hizo el arranque; aquí solo las periódicas.
            while (await temporizador.WaitForNextTickAsync(detener))
                Registrar(registro, EvaluadorVigenciaTsl.Evaluar(lista, tiempo.GetUtcNow(), fallarSiVencida: false));
        }
        catch (OperationCanceledException) { /* apagado normal */ }
    }
}
