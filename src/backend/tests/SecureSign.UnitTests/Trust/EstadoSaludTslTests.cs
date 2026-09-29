using SecureSign.Trust;

namespace SecureSign.UnitTests.Trust;

/// <summary>RUNBOOK.md 12.54 (informe de preauditoría INDECOPI/IOFE, hallazgo P2-02): el health check de la TSL solo lee lo que este estado ya registró — nunca vuelve a evaluar nada.</summary>
public sealed class EstadoSaludTslTests
{
    [Fact]
    public void Antes_de_registrar_nada_no_esta_cargada_y_todo_es_null()
    {
        var estado = new EstadoSaludTsl();

        var f = estado.Leer();

        Assert.False(f.Cargada);
        Assert.Null(f.EmitidaEn);
        Assert.Null(f.ProximaActualizacion);
        Assert.Null(f.Vigencia);
        Assert.Null(f.UltimaComprobacion);
        Assert.Null(f.RevocacionDelFirmante);
        Assert.Null(f.UltimaComprobacionDeRevocacion);
    }

    [Fact]
    public void RegistrarVigencia_actualiza_los_campos_de_vigencia_sin_tocar_los_de_revocacion()
    {
        var estado = new EstadoSaludTsl();
        var ruta = TslDePruebaHelper.EscribirArchivoTemporal([],
            emitidaEn: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            proximaActualizacion: new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero));
        try
        {
            var lista = ListaConfianzaIofe.CargarDesdeArchivo(ruta);
            var ahora = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

            estado.RegistrarVigencia(lista, EstadoVigenciaTsl.Vigente, ahora);
            var f = estado.Leer();

            Assert.True(f.Cargada);
            Assert.Equal(lista.EmitidaEn, f.EmitidaEn);
            Assert.Equal(lista.ProximaActualizacion, f.ProximaActualizacion);
            Assert.Equal(EstadoVigenciaTsl.Vigente, f.Vigencia);
            Assert.Equal(ahora, f.UltimaComprobacion);
            Assert.Null(f.RevocacionDelFirmante);
        }
        finally { File.Delete(ruta); }
    }

    [Fact]
    public void RegistrarRevocacionDelFirmante_actualiza_solo_los_campos_de_revocacion()
    {
        var estado = new EstadoSaludTsl();
        var ahora = DateTimeOffset.UtcNow;

        estado.RegistrarRevocacionDelFirmante(EstadoRevocacion.Good, ahora);
        var f = estado.Leer();

        Assert.Equal(EstadoRevocacion.Good, f.RevocacionDelFirmante);
        Assert.Equal(ahora, f.UltimaComprobacionDeRevocacion);
        Assert.False(f.Cargada); // RegistrarVigencia nunca se llamó
    }

    [Fact]
    public void Registrar_dos_veces_conserva_solo_la_ultima_fotografia()
    {
        var estado = new EstadoSaludTsl();
        var ruta = TslDePruebaHelper.EscribirArchivoTemporal([], emitidaEn: DateTimeOffset.UtcNow, proximaActualizacion: DateTimeOffset.UtcNow.AddDays(1));
        try
        {
            var lista = ListaConfianzaIofe.CargarDesdeArchivo(ruta);
            estado.RegistrarVigencia(lista, EstadoVigenciaTsl.Vigente, DateTimeOffset.UtcNow.AddHours(-6));
            estado.RegistrarVigencia(lista, EstadoVigenciaTsl.PorVencer, DateTimeOffset.UtcNow);

            Assert.Equal(EstadoVigenciaTsl.PorVencer, estado.Leer().Vigencia);
        }
        finally { File.Delete(ruta); }
    }
}
