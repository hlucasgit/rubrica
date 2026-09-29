using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using SecureSign.Signature.Api;
using SecureSign.Trust;
using SecureSign.UnitTests.Trust;

namespace SecureSign.UnitTests.Signature;

/// <summary>
/// RUNBOOK.md 12.40 dejaba explícitamente sin probar el bucle periódico real de
/// <see cref="VigilanteVigenciaTsl"/> (el temporizador de 6 horas en sí, no solo la función pura
/// <c>EvaluadorVigenciaTsl.Evaluar</c> que ya llama) — informe de preauditoría INDECOPI/IOFE,
/// hallazgo P2-03. Con un <see cref="FakeTimeProvider"/> inyectado se puede avanzar el reloj sin
/// esperar horas reales, y comprobar que el <see cref="BackgroundService"/> de verdad evalúa más
/// de una vez y respeta la cancelación del host.
/// </summary>
public sealed class VigilanteVigenciaTslTests
{
    private sealed class RegistroDePrueba : ILogger<VigilanteVigenciaTsl>
    {
        public List<string> Mensajes { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Mensajes) Mensajes.Add(formatter(state, exception));
        }
    }

    private static ListaConfianzaIofe CrearListaVigente(DateTimeOffset ahora)
    {
        var ruta = TslDePruebaHelper.EscribirArchivoTemporal(
            [],
            emitidaEn: ahora.AddDays(-1),
            proximaActualizacion: ahora.AddDays(180)); // muy por delante — vigente durante toda la prueba
        try { return ListaConfianzaIofe.CargarDesdeArchivo(ruta); }
        finally { File.Delete(ruta); }
    }

    [Fact]
    public async Task EvaluaMasDeUnaVezMientrasElServicioSigueCorriendo()
    {
        var inicio = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var reloj = new FakeTimeProvider(inicio);
        var lista = CrearListaVigente(inicio);
        var registro = new RegistroDePrueba();
        using var vigilante = new VigilanteVigenciaTsl(new ProveedorListaConfianzaIofe(lista), registro, reloj);

        await vigilante.StartAsync(CancellationToken.None);
        try
        {
            // Antes de la primera tick del PeriodicTimer no debe haber evaluado nada todavía
            // (la primera evaluación la hace el arranque de Program.cs, no este servicio).
            await EsperarHasta(() => registro.Mensajes.Count, esperado: 0, margen: TimeSpan.FromMilliseconds(200));

            reloj.Advance(VigilanteVigenciaTsl.Intervalo);
            await EsperarHasta(() => registro.Mensajes.Count, esperado: 1);

            reloj.Advance(VigilanteVigenciaTsl.Intervalo);
            await EsperarHasta(() => registro.Mensajes.Count, esperado: 2);

            reloj.Advance(VigilanteVigenciaTsl.Intervalo);
            await EsperarHasta(() => registro.Mensajes.Count, esperado: 3);

            Assert.All(registro.Mensajes, m => Assert.Contains("VIGENTE", m));
        }
        finally
        {
            await vigilante.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>RUNBOOK.md 12.54 (informe de preauditoría INDECOPI/IOFE, hallazgo P2-02): el health check lee lo que este ciclo registra, así que el ciclo tiene que registrarlo de verdad.</summary>
    [Fact]
    public async Task CadaEvaluacionQuedaRegistradaEnEstadoSaludTsl()
    {
        var inicio = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var reloj = new FakeTimeProvider(inicio);
        var lista = CrearListaVigente(inicio);
        var registro = new RegistroDePrueba();
        var estadoSalud = new EstadoSaludTsl();
        using var vigilante = new VigilanteVigenciaTsl(new ProveedorListaConfianzaIofe(lista), registro, reloj, estadoSalud);

        await vigilante.StartAsync(CancellationToken.None);
        try
        {
            Assert.False(estadoSalud.Leer().Cargada); // todavía no tocó el reloj

            reloj.Advance(VigilanteVigenciaTsl.Intervalo);
            // Se espera la condición real (Cargada), no un proxy (el contador de mensajes del logger) — ambas
            // escrituras ocurren en la misma iteración sin await entre medias, pero solo una es lo que se prueba.
            await EsperarHasta(() => estadoSalud.Leer().Cargada, esperado: true);

            var f = estadoSalud.Leer();
            Assert.Equal(EstadoVigenciaTsl.Vigente, f.Vigencia);
            Assert.Equal(inicio + VigilanteVigenciaTsl.Intervalo, f.UltimaComprobacion);
        }
        finally
        {
            await vigilante.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task RespetaLaCancelacionDelHostSinLanzar()
    {
        var inicio = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var reloj = new FakeTimeProvider(inicio);
        var lista = CrearListaVigente(inicio);
        var registro = new RegistroDePrueba();
        using var vigilante = new VigilanteVigenciaTsl(new ProveedorListaConfianzaIofe(lista), registro, reloj);

        await vigilante.StartAsync(CancellationToken.None);
        reloj.Advance(VigilanteVigenciaTsl.Intervalo);
        await EsperarHasta(() => registro.Mensajes.Count, esperado: 1);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var detencion = Record.ExceptionAsync(() => vigilante.StopAsync(cts.Token));
        Assert.Null(await detencion);
    }

    private static async Task EsperarHasta<T>(Func<T> valorActual, T esperado, TimeSpan? margen = null)
    {
        var limite = DateTime.UtcNow + (margen ?? TimeSpan.FromSeconds(5));
        while (DateTime.UtcNow < limite)
        {
            if (Equals(valorActual(), esperado)) return;
            await Task.Delay(10);
        }
        Assert.Equal(esperado, valorActual());
    }
}
