using SecureSign.Evidence.Domain;
using Xunit;

namespace SecureSign.UnitTests.Evidence;

public class CadenaEvidenciaTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid DocumentoId = Guid.NewGuid();

    private static DatosContextuales DatosDePrueba() => new("190.12.34.56", "Mozilla/5.0", "Windows 11", null);

    [Fact]
    public void Cadena_de_tres_eventos_es_valida_cuando_no_hay_alteracion()
    {
        var evento1 = EventoEvidencia.Crear(TenantId, DocumentoId, TipoEvidencia.Carga, DatosDePrueba(), null);
        var evento2 = EventoEvidencia.Crear(TenantId, DocumentoId, TipoEvidencia.Visualizacion, DatosDePrueba(), evento1.HashEvento);
        var evento3 = EventoEvidencia.Crear(TenantId, DocumentoId, TipoEvidencia.Firma, DatosDePrueba(), evento2.HashEvento);

        var resultado = VerificadorCadenaEvidencia.Verificar(new[] { evento1, evento2, evento3 });

        Assert.True(resultado.CadenaValida);
        Assert.Equal(3, resultado.TotalEventos);
        Assert.Null(resultado.PrimerEventoRotoId);
    }

    [Fact]
    public void Cadena_detecta_rotura_cuando_un_eslabon_intermedio_es_reemplazado()
    {
        var evento1 = EventoEvidencia.Crear(TenantId, DocumentoId, TipoEvidencia.Carga, DatosDePrueba(), null);
        var evento2 = EventoEvidencia.Crear(TenantId, DocumentoId, TipoEvidencia.Visualizacion, DatosDePrueba(), evento1.HashEvento);
        var evento3 = EventoEvidencia.Crear(TenantId, DocumentoId, TipoEvidencia.Firma, DatosDePrueba(), evento2.HashEvento);

        // Simula un intento de alterar retroactivamente el evento intermedio:
        // se crea un evento2 "falsificado" con los mismos datos pero registrado
        // más tarde (RegistradoEn distinto), lo que cambia su HashEvento.
        var evento2Falsificado = EventoEvidencia.Crear(TenantId, DocumentoId, TipoEvidencia.Visualizacion, DatosDePrueba(), evento1.HashEvento);

        var resultado = VerificadorCadenaEvidencia.Verificar(new[] { evento1, evento2Falsificado, evento3 });

        Assert.False(resultado.CadenaValida);
        Assert.Equal(evento3.Id, resultado.PrimerEventoRotoId); // evento3 referencia el hash del evento2 original, no del falsificado
    }

    [Fact]
    public void Primer_evento_de_la_cadena_no_tiene_hash_anterior()
    {
        var evento1 = EventoEvidencia.Crear(TenantId, DocumentoId, TipoEvidencia.Carga, DatosDePrueba(), null);

        Assert.Null(evento1.HashEventoAnterior);
        Assert.True(evento1.EsConsistenteCon(null));
    }

    /// <summary>
    /// Regresión: PostgreSQL ("timestamp with time zone") solo conserva
    /// precisión de microsegundo. Si RegistradoEn se usara con precisión de
    /// tick completa (100ns) al calcular el hash, recargar el evento desde
    /// la base de datos truncaría el timestamp y EsConsistenteCon()
    /// reportaría una manipulación inexistente. Se detectó probando contra
    /// una instancia real de PostgreSQL, no con el repositorio en memoria.
    /// </summary>
    [Fact]
    public void RegistradoEn_no_conserva_precision_mas_fina_que_microsegundos()
    {
        var evento = EventoEvidencia.Crear(TenantId, DocumentoId, TipoEvidencia.Carga, DatosDePrueba(), null);

        Assert.Equal(0, evento.RegistradoEn.Ticks % 10);
    }
}
