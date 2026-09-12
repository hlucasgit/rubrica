using SecureSign.Signature.Domain;
using Xunit;

namespace SecureSign.UnitTests.Signature;

public class SolicitudFirmaTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid DocumentoId = Guid.NewGuid();

    [Fact]
    public void Con_orden_secuencial_el_segundo_firmante_no_puede_firmar_antes_que_el_primero()
    {
        var firmante1 = Guid.NewGuid();
        var firmante2 = Guid.NewGuid();

        var solicitud = SolicitudFirma.Crear(
            TenantId, DocumentoId, TipoFirma.Avanzada, requiereOrdenSecuencial: true,
            firmantes: new[] { (firmante1, 1), (firmante2, 2) },
            creadoPor: Guid.NewGuid()).Valor;

        var flujoSegundo = solicitud.Flujos.Single(f => f.FirmanteUsuarioId == firmante2);

        var resultado = solicitud.IniciarValidacionIdentidad(flujoSegundo.Id);

        Assert.False(resultado.EsExitoso);
    }

    [Fact]
    public void Flujo_completo_simple_termina_en_estado_Firmado()
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
        var resultadoFirma = solicitud.ConfirmarFirma(flujo.Id);

        Assert.True(resultadoFirma.EsExitoso);
        Assert.Equal(EstadoSolicitudFirma.Firmado, solicitud.Estado);
    }

    [Fact]
    public void No_se_puede_firmar_sin_haber_visualizado_el_documento()
    {
        var firmante = Guid.NewGuid();
        var solicitud = SolicitudFirma.Crear(
            TenantId, DocumentoId, TipoFirma.Simple, requiereOrdenSecuencial: false,
            firmantes: new[] { (firmante, 1) },
            creadoPor: Guid.NewGuid()).Valor;

        var flujo = solicitud.Flujos.Single();

        var resultado = solicitud.ConfirmarFirma(flujo.Id);

        Assert.False(resultado.EsExitoso);
    }

    [Fact]
    public void Rechazar_un_flujo_marca_toda_la_solicitud_como_rechazada()
    {
        var firmante = Guid.NewGuid();
        var solicitud = SolicitudFirma.Crear(
            TenantId, DocumentoId, TipoFirma.Simple, requiereOrdenSecuencial: false,
            firmantes: new[] { (firmante, 1) },
            creadoPor: Guid.NewGuid()).Valor;

        var flujo = solicitud.Flujos.Single();
        solicitud.RechazarFirma(flujo.Id, "El firmante no está de acuerdo con el contenido.");

        Assert.Equal(EstadoSolicitudFirma.Rechazado, solicitud.Estado);
    }

    [Fact]
    public void Crear_sin_firmantes_falla()
    {
        var resultado = SolicitudFirma.Crear(
            TenantId, DocumentoId, TipoFirma.Simple, requiereOrdenSecuencial: false,
            firmantes: Array.Empty<(Guid, int)>(),
            creadoPor: Guid.NewGuid());

        Assert.False(resultado.EsExitoso);
    }
}
