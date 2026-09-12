using SecureSign.Identity.Domain;
using SecureSign.Signature.Domain;
using Xunit;

namespace SecureSign.UnitTests.Identity;

public class UsuarioIdentidadTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UsuarioId = Guid.NewGuid();

    [Fact]
    public void Usuario_recien_registrado_solo_puede_firma_simple()
    {
        var usuario = UsuarioIdentidad.Registrar(TenantId, UsuarioId);

        Assert.Equal(IndiceConfianzaDigital.UsuarioNuevo, usuario.CalcularIndiceConfianza());
        Assert.True(IndiceConfianzaDigital.PuedeEjecutar(usuario.CalcularIndiceConfianza(), TipoFirma.Simple));
        Assert.False(IndiceConfianzaDigital.PuedeEjecutar(usuario.CalcularIndiceConfianza(), TipoFirma.Avanzada));
    }

    [Fact]
    public void Tras_una_validacion_exitosa_habilita_firma_avanzada_pero_no_digital()
    {
        var usuario = UsuarioIdentidad.Registrar(TenantId, UsuarioId);
        usuario.RegistrarValidacionExitosa();

        var indice = usuario.CalcularIndiceConfianza();

        Assert.Equal(IndiceConfianzaDigital.UsuarioValidado, indice);
        Assert.True(IndiceConfianzaDigital.PuedeEjecutar(indice, TipoFirma.Avanzada));
        Assert.False(IndiceConfianzaDigital.PuedeEjecutar(indice, TipoFirma.Digital));
    }

    [Fact]
    public void Certificado_vigente_habilita_firma_digital()
    {
        var usuario = UsuarioIdentidad.Registrar(TenantId, UsuarioId);
        usuario.MarcarCertificadoVigente();

        var indice = usuario.CalcularIndiceConfianza();

        Assert.True(IndiceConfianzaDigital.PuedeEjecutar(indice, TipoFirma.Digital));
    }

    [Fact]
    public void Revocar_certificado_retira_la_habilitacion_de_firma_digital()
    {
        var usuario = UsuarioIdentidad.Registrar(TenantId, UsuarioId);
        usuario.MarcarCertificadoVigente();
        usuario.RevocarCertificado();

        var indice = usuario.CalcularIndiceConfianza();

        Assert.False(IndiceConfianzaDigital.PuedeEjecutar(indice, TipoFirma.Digital));
    }

    [Fact]
    public void Rechazos_por_fraude_degradan_el_indice_aunque_haya_validacion_exitosa_previa()
    {
        var usuario = UsuarioIdentidad.Registrar(TenantId, UsuarioId);
        usuario.RegistrarValidacionExitosa();
        usuario.RegistrarRechazoPorSospechaFraude();
        usuario.RegistrarRechazoPorSospechaFraude();

        var indice = usuario.CalcularIndiceConfianza();

        Assert.False(IndiceConfianzaDigital.PuedeEjecutar(indice, TipoFirma.Avanzada));
    }
}
