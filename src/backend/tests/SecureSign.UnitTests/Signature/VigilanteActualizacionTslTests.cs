using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using SecureSign.Signature.Api;
using SecureSign.Trust;
using SecureSign.UnitTests.Trust;

namespace SecureSign.UnitTests.Signature;

/// <summary>
/// RUNBOOK.md 12.57 (informe de preauditoría INDECOPI/IOFE, hallazgo P2-01, recarga en caliente): el bucle
/// periódico de <see cref="VigilanteActualizacionTsl"/> — deshabilitado por defecto, chequeo inmediato al
/// arrancar cuando está habilitado, reemplaza <see cref="IProveedorListaConfianzaIofe"/> solo cuando
/// <see cref="ActualizadorTsl"/> devuelve <see cref="ResultadoActualizacionTsl.Actualizada"/>.
/// </summary>
public sealed class VigilanteActualizacionTslTests : IDisposable
{
    private readonly List<string> _archivos = [];
    private readonly List<string> _directorios = [];

    public void Dispose()
    {
        foreach (var a in _archivos) { try { File.Delete(a); } catch { } }
        foreach (var d in _directorios) { try { Directory.Delete(d, recursive: true); } catch { } }
    }

    private sealed class RegistroDePrueba : ILogger<VigilanteActualizacionTsl>
    {
        public List<string> Mensajes { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Mensajes) Mensajes.Add(formatter(state, exception));
        }
    }

    private static X509Certificate2 GenerarAutofirmado(string cn, RSA llave)
    {
        var req = new CertificateRequest($"CN={cn}", llave, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return new X509Certificate2(req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1)).Export(X509ContentType.Cert));
    }

    private sealed record Escenario(X509Certificate2 Raiz, RSA Llave, string RutaTslVigente, HandlerHttpFalso Handler, string UrlDescarga);

    private Escenario Construir()
    {
        string sufijo = Guid.NewGuid().ToString("N");
        var llave = RSA.Create(2048);
        var raiz = GenerarAutofirmado($"Raiz Vigilante Actualizacion TSL De Prueba {sufijo}", llave);

        string dir = Path.Combine(Path.GetTempPath(), $"vigilante-actualizacion-tsl-{sufijo}");
        Directory.CreateDirectory(dir);
        _directorios.Add(dir);

        return new Escenario(raiz, llave, Path.Combine(dir, "tsl-pe.xml"), new HandlerHttpFalso(), $"https://vigilante-actualizacion-tsl.prueba.local/tsl-{sufijo}.xml");
    }

    private string EscribirTslCandidata(Escenario e, DateTimeOffset emitidaEn)
    {
        string contenidoFechas =
            $"<tsl:ListIssueDateTime>{emitidaEn.UtcDateTime:o}</tsl:ListIssueDateTime>" +
            $"<tsl:NextUpdate><tsl:dateTime>{emitidaEn.AddDays(180).UtcDateTime:o}</tsl:dateTime></tsl:NextUpdate>";
        string ruta = TslFirmadaDePruebaHelper.Escribir(e.Llave, e.Raiz, contenidoSchemeInformation: contenidoFechas);
        _archivos.Add(ruta);
        return ruta;
    }

    private static ActualizadorTsl ConstruirActualizador(Escenario e)
    {
        var http = new HttpClient(e.Handler);
        return new ActualizadorTsl(http, new VerificadorRevocacionOcsp(http), new VerificadorRevocacionCrl(http));
    }

    [Fact]
    public async Task Deshabilitada_no_toca_el_proveedor_ni_llama_a_la_red()
    {
        var e = Construir();
        // Deliberadamente SIN registrar ninguna ruta en e.Handler — si el servicio llamara a la red, obtendría
        // 404 y (si no estuviera bien deshabilitado) se registraría como RechazadaError.
        var proveedor = new ProveedorListaConfianzaIofe(null!);
        var registro = new RegistroDePrueba();
        using var vigilante = new VigilanteActualizacionTsl(
            proveedor, ConstruirActualizador(e), registro, new Uri(e.UrlDescarga), e.RutaTslVigente, e.Raiz, habilitada: false);

        await vigilante.StartAsync(CancellationToken.None);
        try
        {
            await EsperarHasta(() => registro.Mensajes.Count, esperado: 1); // el único mensaje es "deshabilitada"
            Assert.Contains(registro.Mensajes, m => m.Contains("deshabilitada", StringComparison.OrdinalIgnoreCase));
        }
        finally { await vigilante.StopAsync(CancellationToken.None); }
    }

    [Fact]
    public async Task Habilitada_hace_un_chequeo_inmediato_al_arrancar_y_actualiza_el_proveedor()
    {
        var e = Construir();
        File.WriteAllText(e.RutaTslVigente, "contenido-vigente-de-prueba");
        var candidataRuta = EscribirTslCandidata(e, DateTimeOffset.UtcNow);
        e.Handler.ResponderBytes(e.UrlDescarga, File.ReadAllBytes(candidataRuta), "application/xml");

        var proveedor = new ProveedorListaConfianzaIofe(null!);
        var registro = new RegistroDePrueba();
        var reloj = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var vigilante = new VigilanteActualizacionTsl(
            proveedor, ConstruirActualizador(e), registro, new Uri(e.UrlDescarga), e.RutaTslVigente, e.Raiz, habilitada: true, reloj);

        await vigilante.StartAsync(CancellationToken.None);
        try
        {
            await EsperarHasta(() => proveedor.Actual is not null, esperado: true);
            Assert.NotNull(proveedor.Actual);
            Assert.Contains(registro.Mensajes, m => m.Contains("Actualizada", StringComparison.OrdinalIgnoreCase) || m.Contains("TSL actualizada"));
        }
        finally { await vigilante.StopAsync(CancellationToken.None); }
    }

    [Fact]
    public async Task Vuelve_a_consultar_la_red_en_cada_Intervalo()
    {
        var e = Construir();
        File.WriteAllText(e.RutaTslVigente, "contenido-vigente-de-prueba");
        int llamadas = 0;
        e.Handler.Responder(e.UrlDescarga, _ =>
        {
            Interlocked.Increment(ref llamadas);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        });

        var proveedor = new ProveedorListaConfianzaIofe(null!);
        var registro = new RegistroDePrueba();
        var reloj = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var vigilante = new VigilanteActualizacionTsl(
            proveedor, ConstruirActualizador(e), registro, new Uri(e.UrlDescarga), e.RutaTslVigente, e.Raiz, habilitada: true, reloj);

        await vigilante.StartAsync(CancellationToken.None);
        try
        {
            await EsperarHasta(() => llamadas, esperado: 1);

            reloj.Advance(VigilanteActualizacionTsl.Intervalo);
            await EsperarHasta(() => llamadas, esperado: 2);

            reloj.Advance(VigilanteActualizacionTsl.Intervalo);
            await EsperarHasta(() => llamadas, esperado: 3);
        }
        finally { await vigilante.StopAsync(CancellationToken.None); }
    }

    [Fact]
    public async Task Cuando_la_descarga_no_verifica_no_reemplaza_el_proveedor()
    {
        var e = Construir();
        File.WriteAllText(e.RutaTslVigente, "contenido-vigente-de-prueba");

        var otraLlave = RSA.Create(2048);
        var otraRaiz = GenerarAutofirmado("Otra Raiz No Relacionada", otraLlave);
        string ruta = TslFirmadaDePruebaHelper.Escribir(otraLlave, otraRaiz); // firmada por OTRA raíz, no por e.Raiz
        _archivos.Add(ruta);
        e.Handler.ResponderBytes(e.UrlDescarga, File.ReadAllBytes(ruta), "application/xml");

        string vigenteRuta = EscribirTslCandidata(e, DateTimeOffset.UtcNow.AddDays(-10));
        File.Copy(vigenteRuta, e.RutaTslVigente, overwrite: true);
        var listaInicial = ListaConfianzaIofe.CargarDesdeArchivoFirmado(e.RutaTslVigente, e.Raiz);
        var proveedor = new ProveedorListaConfianzaIofe(listaInicial);
        var registro = new RegistroDePrueba();
        var reloj = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var vigilante = new VigilanteActualizacionTsl(
            proveedor, ConstruirActualizador(e), registro, new Uri(e.UrlDescarga), e.RutaTslVigente, e.Raiz, habilitada: true, reloj);

        await vigilante.StartAsync(CancellationToken.None);
        try
        {
            await EsperarHasta(() => registro.Mensajes.Any(m => m.Contains("no aplicada", StringComparison.OrdinalIgnoreCase)), esperado: true);
            Assert.Same(listaInicial, proveedor.Actual);
        }
        finally { await vigilante.StopAsync(CancellationToken.None); }
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
