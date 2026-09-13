using SecureSign.Signature.Domain;
using Xunit;

namespace SecureSign.UnitTests.Signature;

public class PosicionFirmaTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid DocumentoId = Guid.NewGuid();

    [Theory]
    [InlineData(0, 0.1, 0.1, 0.2, 0.05)] // página 0 no existe (1-based)
    [InlineData(1, -0.1, 0.1, 0.2, 0.05)] // X negativo
    [InlineData(1, 0.1, 1.1, 0.2, 0.05)] // Y fuera de rango
    [InlineData(1, 0.1, 0.1, 0, 0.05)] // ancho cero
    [InlineData(1, 0.9, 0.1, 0.2, 0.05)] // se sale del borde derecho
    public void Rechaza_coordenadas_invalidas(int pagina, double x, double y, double ancho, double alto)
    {
        var resultado = PosicionFirma.Crear(pagina, x, y, ancho, alto);
        Assert.False(resultado.EsExitoso);
    }

    [Fact]
    public void Acepta_un_recuadro_dentro_de_los_limites_de_la_pagina()
    {
        var resultado = PosicionFirma.Crear(1, 0.6, 0.85, 0.35, 0.1);
        Assert.True(resultado.EsExitoso);
        Assert.Equal(1, resultado.Valor.NumeroPagina);
    }

    [Fact]
    public void Establecer_posicion_antes_de_firmar_funciona()
    {
        var firmante = Guid.NewGuid();
        var solicitud = SolicitudFirma.Crear(
            TenantId, DocumentoId, TipoFirma.Simple, requiereOrdenSecuencial: false,
            firmantes: new[] { (firmante, 1) },
            creadoPor: Guid.NewGuid()).Valor;
        var flujo = solicitud.Flujos.Single();

        var posicion = PosicionFirma.Crear(1, 0.6, 0.85, 0.35, 0.1).Valor;
        var resultado = solicitud.EstablecerPosicionFirma(flujo.Id, posicion);

        Assert.True(resultado.EsExitoso);
        Assert.Equal(posicion, flujo.Posicion);
    }

    [Fact]
    public void No_se_puede_cambiar_la_posicion_despues_de_firmado()
    {
        var firmante = Guid.NewGuid();
        var solicitud = SolicitudFirma.Crear(
            TenantId, DocumentoId, TipoFirma.Simple, requiereOrdenSecuencial: false,
            firmantes: new[] { (firmante, 1) },
            creadoPor: Guid.NewGuid()).Valor;
        var flujo = solicitud.Flujos.Single();

        solicitud.NotificarFirmante(flujo.Id);
        solicitud.RegistrarVisualizacion(flujo.Id);
        solicitud.IniciarValidacionIdentidad(flujo.Id);
        solicitud.ConfirmarFirma(flujo.Id);

        var posicion = PosicionFirma.Crear(1, 0.6, 0.85, 0.35, 0.1).Valor;
        var resultado = solicitud.EstablecerPosicionFirma(flujo.Id, posicion);

        Assert.False(resultado.EsExitoso);
        Assert.NotNull(flujo.FirmadoEn);
    }
}
