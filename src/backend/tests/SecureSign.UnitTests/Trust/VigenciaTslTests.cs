using SecureSign.Trust;

namespace SecureSign.UnitTests.Trust;

/// <summary>Fecha de emisión y próxima actualización de la TSL, y el aviso de vigencia (RUNBOOK.md 12.40).</summary>
public sealed class VigenciaTslTests : IDisposable
{
    private readonly List<string> _archivos = [];
    private static readonly DateTimeOffset Emitida = new(2026, 8, 10, 16, 10, 31, TimeSpan.Zero);
    private static readonly DateTimeOffset Proxima = new(2027, 2, 10, 11, 10, 31, TimeSpan.Zero);
    private static readonly TimeSpan Aviso = TimeSpan.FromDays(14);

    public void Dispose() { foreach (var a in _archivos) { try { File.Delete(a); } catch { /* mejor esfuerzo */ } } }

    private ListaConfianzaIofe Lista(DateTimeOffset? emitida, DateTimeOffset? proxima)
    {
        string ruta = TslDePruebaHelper.EscribirArchivoTemporal([], emitida, proxima);
        _archivos.Add(ruta);
        return ListaConfianzaIofe.CargarDesdeArchivo(ruta);
    }

    [Fact]
    public void Lee_la_emision_y_la_proxima_actualizacion_declaradas()
    {
        var lista = Lista(Emitida, Proxima);

        Assert.Equal(Emitida, lista.EmitidaEn);
        Assert.Equal(Proxima, lista.ProximaActualizacion);
    }

    [Fact]
    public void Una_TSL_sin_fechas_las_devuelve_nulas_y_la_vigencia_es_desconocida()
    {
        var lista = Lista(null, null);

        Assert.Null(lista.EmitidaEn);
        Assert.Null(lista.ProximaActualizacion);
        Assert.Equal(EstadoVigenciaTsl.Desconocida, lista.Vigencia(DateTimeOffset.UtcNow, Aviso));
    }

    [Fact]
    public void Lejos_de_la_proxima_actualizacion_esta_vigente() =>
        Assert.Equal(EstadoVigenciaTsl.Vigente, Lista(Emitida, Proxima).Vigencia(Proxima.AddDays(-60), Aviso));

    [Fact]
    public void Dentro_del_plazo_de_aviso_esta_por_vencer() =>
        Assert.Equal(EstadoVigenciaTsl.PorVencer, Lista(Emitida, Proxima).Vigencia(Proxima.AddDays(-5), Aviso));

    [Fact]
    public void Justo_en_el_limite_del_aviso_ya_esta_por_vencer() =>
        Assert.Equal(EstadoVigenciaTsl.PorVencer, Lista(Emitida, Proxima).Vigencia(Proxima - Aviso, Aviso));

    [Fact]
    public void Pasada_la_proxima_actualizacion_esta_vencida() =>
        Assert.Equal(EstadoVigenciaTsl.Vencida, Lista(Emitida, Proxima).Vigencia(Proxima.AddSeconds(1), Aviso));

    [Fact]
    public void Justo_en_la_proxima_actualizacion_todavia_no_esta_vencida() =>
        Assert.Equal(EstadoVigenciaTsl.PorVencer, Lista(Emitida, Proxima).Vigencia(Proxima, Aviso));

    [Fact]
    public void La_descripcion_para_el_expediente_incluye_ambas_fechas_y_el_estado()
    {
        var lista = Lista(Emitida, Proxima);

        Assert.Equal("TSL de IOFE: emitida 2026-08-10, próxima actualización 2027-02-10 — VIGENTE.", lista.DescribirVigencia(Proxima.AddDays(-90), Aviso));
        Assert.EndsWith("VENCIDA.", lista.DescribirVigencia(Proxima.AddDays(1), Aviso));
        Assert.Contains("(no declarada)", Lista(null, null).DescribirVigencia(DateTimeOffset.UtcNow, Aviso));
    }

    [Fact]
    public void La_TSL_real_de_INDECOPI_declara_emision_y_proxima_actualizacion()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Services", "SecureSign.Signature", "SecureSign.Signature.Api", "ConfianzaIofe");
        string tsl = Path.Combine(dir, "tsl-pe.xml");
        if (!File.Exists(tsl)) return;

        var lista = ListaConfianzaIofe.CargarDesdeArchivo(tsl);

        Assert.NotNull(lista.EmitidaEn);
        Assert.NotNull(lista.ProximaActualizacion);
        Assert.True(lista.ProximaActualizacion > lista.EmitidaEn, "NextUpdate debe ser posterior a la emisión.");
    }
}

/// <summary>Decisión de qué hacer con la vigencia de la TSL — cada rama, incluida "vencida" (RUNBOOK.md 12.40).</summary>
public sealed class EvaluadorVigenciaTslTests : IDisposable
{
    private readonly List<string> _archivos = [];
    private static readonly DateTimeOffset Proxima = new(2027, 2, 10, 11, 10, 31, TimeSpan.Zero);

    public void Dispose() { foreach (var a in _archivos) { try { File.Delete(a); } catch { /* mejor esfuerzo */ } } }

    private ListaConfianzaIofe Lista(DateTimeOffset? proxima)
    {
        string ruta = TslDePruebaHelper.EscribirArchivoTemporal([], proxima?.AddMonths(-6), proxima);
        _archivos.Add(ruta);
        return ListaConfianzaIofe.CargarDesdeArchivo(ruta);
    }

    [Fact]
    public void Vigente_solo_informa()
    {
        var e = EvaluadorVigenciaTsl.Evaluar(Lista(Proxima), Proxima.AddDays(-90), fallarSiVencida: true);

        Assert.Equal(NivelAvisoTsl.Informacion, e.Nivel);
        Assert.False(e.DebeAbortarElArranque);
        Assert.EndsWith("VIGENTE.", e.Mensaje);
    }

    [Fact]
    public void Por_vencer_advierte_y_nunca_aborta_aunque_se_pida_fallar()
    {
        var e = EvaluadorVigenciaTsl.Evaluar(Lista(Proxima), Proxima.AddDays(-3), fallarSiVencida: true);

        Assert.Equal(NivelAvisoTsl.Advertencia, e.Nivel);
        Assert.False(e.DebeAbortarElArranque);
        Assert.Contains("Descarga pronto", e.Mensaje);
    }

    [Fact]
    public void Vencida_es_un_error_pero_por_defecto_no_aborta()
    {
        var e = EvaluadorVigenciaTsl.Evaluar(Lista(Proxima), Proxima.AddDays(1), fallarSiVencida: false);

        Assert.Equal(EstadoVigenciaTsl.Vencida, e.Estado);
        Assert.Equal(NivelAvisoTsl.Error, e.Nivel);
        Assert.False(e.DebeAbortarElArranque);
        Assert.Contains("reemplaza ConfianzaIofe/tsl-pe.xml", e.Mensaje);
    }

    [Fact]
    public void Vencida_con_fallar_si_vencida_aborta_el_arranque()
    {
        var e = EvaluadorVigenciaTsl.Evaluar(Lista(Proxima), Proxima.AddDays(1), fallarSiVencida: true);

        Assert.True(e.DebeAbortarElArranque);
        Assert.Equal(NivelAvisoTsl.Error, e.Nivel);
    }

    [Fact]
    public void Sin_fecha_declarada_advierte_y_no_aborta()
    {
        var e = EvaluadorVigenciaTsl.Evaluar(Lista(null), DateTimeOffset.UtcNow, fallarSiVencida: true);

        Assert.Equal(EstadoVigenciaTsl.Desconocida, e.Estado);
        Assert.Equal(NivelAvisoTsl.Advertencia, e.Nivel);
        Assert.False(e.DebeAbortarElArranque);
    }

    [Fact]
    public void El_plazo_de_aviso_es_configurable()
    {
        var lista = Lista(Proxima);
        var ahora = Proxima.AddDays(-30);

        Assert.Equal(EstadoVigenciaTsl.Vigente, EvaluadorVigenciaTsl.Evaluar(lista, ahora, false).Estado);                         // 14 días por defecto
        Assert.Equal(EstadoVigenciaTsl.PorVencer, EvaluadorVigenciaTsl.Evaluar(lista, ahora, false, TimeSpan.FromDays(45)).Estado); // aviso de 45 días
    }
}
