using SecureSign.Identity.Domain;
using SecureSign.Signature.Domain;
using Xunit;

namespace SecureSign.UnitTests.Identity;

public class IndiceConfianzaDigitalTests
{
    [Fact]
    public void Usuario_nuevo_sin_validaciones_obtiene_30()
    {
        var indice = IndiceConfianzaDigital.Calcular(
            historialValidaciones: Array.Empty<SenalValidacion>(),
            tieneCertificadoVigente: false, esCuentaInstitucional: false, rechazosPorSospechaFraude: 0);

        Assert.Equal(30, indice);
    }

    [Fact]
    public void Usuario_con_certificado_vigente_obtiene_95()
    {
        var indice = IndiceConfianzaDigital.Calcular(
            historialValidaciones: Array.Empty<SenalValidacion>(),
            tieneCertificadoVigente: true, esCuentaInstitucional: false, rechazosPorSospechaFraude: 0);

        Assert.Equal(95, indice);
    }

    [Fact]
    public void Cuenta_institucional_siempre_obtiene_99_aunque_no_tenga_certificado()
    {
        var indice = IndiceConfianzaDigital.Calcular(
            historialValidaciones: Array.Empty<SenalValidacion>(),
            tieneCertificadoVigente: false, esCuentaInstitucional: true, rechazosPorSospechaFraude: 0);

        Assert.Equal(99, indice);
    }

    [Fact]
    public void Historial_de_fraude_reduce_el_indice_de_un_usuario_validado()
    {
        var validaciones = new[] { new SenalValidacion(MetodoValidacionIdentidad.OtpSms, true, DateTimeOffset.UtcNow) };

        var indice = IndiceConfianzaDigital.Calcular(
            historialValidaciones: validaciones,
            tieneCertificadoVigente: false, esCuentaInstitucional: false, rechazosPorSospechaFraude: 2);

        Assert.Equal(40, indice); // 70 - (2 * 15)
    }

    [Theory]
    [InlineData(30, TipoFirma.Simple, true)]
    [InlineData(30, TipoFirma.Avanzada, false)]
    [InlineData(70, TipoFirma.Avanzada, true)]
    [InlineData(70, TipoFirma.Digital, false)]
    [InlineData(95, TipoFirma.Digital, true)]
    public void PuedeEjecutar_respeta_los_umbrales_por_tipo_de_firma(int indice, TipoFirma tipo, bool esperado)
    {
        Assert.Equal(esperado, IndiceConfianzaDigital.PuedeEjecutar(indice, tipo));
    }
}
