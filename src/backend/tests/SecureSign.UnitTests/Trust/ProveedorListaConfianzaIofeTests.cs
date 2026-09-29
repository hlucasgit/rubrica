using SecureSign.Trust;

namespace SecureSign.UnitTests.Trust;

/// <summary>RUNBOOK.md 12.57 (informe de preauditoría INDECOPI/IOFE, hallazgo P2-01, recarga en caliente).</summary>
public sealed class ProveedorListaConfianzaIofeTests
{
    private static ListaConfianzaIofe CrearLista(DateTimeOffset emitidaEn)
    {
        var ruta = TslDePruebaHelper.EscribirArchivoTemporal([], emitidaEn: emitidaEn, proximaActualizacion: emitidaEn.AddDays(180));
        try { return ListaConfianzaIofe.CargarDesdeArchivo(ruta); }
        finally { File.Delete(ruta); }
    }

    [Fact]
    public void Actual_devuelve_la_lista_con_la_que_se_construyo()
    {
        var inicial = CrearLista(DateTimeOffset.UtcNow);
        var proveedor = new ProveedorListaConfianzaIofe(inicial);

        Assert.Same(inicial, proveedor.Actual);
    }

    [Fact]
    public void Reemplazar_hace_que_Actual_devuelva_la_lista_nueva()
    {
        var inicial = CrearLista(DateTimeOffset.UtcNow.AddDays(-1));
        var nueva = CrearLista(DateTimeOffset.UtcNow);
        var proveedor = new ProveedorListaConfianzaIofe(inicial);

        proveedor.Reemplazar(nueva);

        Assert.Same(nueva, proveedor.Actual);
    }

    [Fact]
    public void Reemplazar_no_afecta_una_referencia_ya_leida_antes_del_reemplazo()
    {
        var inicial = CrearLista(DateTimeOffset.UtcNow.AddDays(-1));
        var nueva = CrearLista(DateTimeOffset.UtcNow);
        var proveedor = new ProveedorListaConfianzaIofe(inicial);

        var referenciaLeidaAntes = proveedor.Actual;
        proveedor.Reemplazar(nueva);

        Assert.Same(inicial, referenciaLeidaAntes); // la ListaConfianzaIofe en sí sigue siendo inmutable
        Assert.Same(nueva, proveedor.Actual);
    }
}
