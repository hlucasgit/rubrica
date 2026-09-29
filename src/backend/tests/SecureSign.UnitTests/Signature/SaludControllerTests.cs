using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SecureSign.Signature.Api.Controllers;
using SecureSign.Trust;
using SecureSign.UnitTests.Trust;

namespace SecureSign.UnitTests.Signature;

/// <summary>RUNBOOK.md 12.54 (informe de preauditoría INDECOPI/IOFE, hallazgo P2-02): un monitor externo debe poder distinguir "saludable" de "no saludable" por el código HTTP, no solo por el cuerpo.</summary>
public sealed class SaludControllerTests
{
    [Fact]
    public void Sin_ninguna_TSL_cargada_responde_503()
    {
        var controller = new SaludController(new EstadoSaludTsl());

        var resultado = controller.Tsl();

        var objeto = Assert.IsType<ObjectResult>(resultado.Result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, objeto.StatusCode);
    }

    [Fact]
    public void Con_TSL_vigente_y_firmante_no_revocado_responde_200()
    {
        var estado = new EstadoSaludTsl();
        var ruta = TslDePruebaHelper.EscribirArchivoTemporal([], emitidaEn: DateTimeOffset.UtcNow.AddDays(-1), proximaActualizacion: DateTimeOffset.UtcNow.AddDays(180));
        try
        {
            var lista = ListaConfianzaIofe.CargarDesdeArchivo(ruta);
            estado.RegistrarVigencia(lista, EstadoVigenciaTsl.Vigente, DateTimeOffset.UtcNow);
            estado.RegistrarRevocacionDelFirmante(EstadoRevocacion.Good, DateTimeOffset.UtcNow);

            var controller = new SaludController(estado);
            var resultado = controller.Tsl();

            var ok = Assert.IsType<OkObjectResult>(resultado.Result);
            var cuerpo = Assert.IsType<RespuestaSaludTsl>(ok.Value);
            Assert.Equal(EstadoVigenciaTsl.Vigente, cuerpo.Vigencia);
            Assert.Equal(EstadoRevocacion.Good, cuerpo.RevocacionDelFirmante);
        }
        finally { File.Delete(ruta); }
    }

    [Fact]
    public void Con_TSL_vencida_responde_503()
    {
        var estado = new EstadoSaludTsl();
        var ruta = TslDePruebaHelper.EscribirArchivoTemporal([], emitidaEn: DateTimeOffset.UtcNow.AddDays(-400), proximaActualizacion: DateTimeOffset.UtcNow.AddDays(-1));
        try
        {
            var lista = ListaConfianzaIofe.CargarDesdeArchivo(ruta);
            estado.RegistrarVigencia(lista, EstadoVigenciaTsl.Vencida, DateTimeOffset.UtcNow);

            var controller = new SaludController(estado);
            var resultado = controller.Tsl();

            var objeto = Assert.IsType<ObjectResult>(resultado.Result);
            Assert.Equal(StatusCodes.Status503ServiceUnavailable, objeto.StatusCode);
        }
        finally { File.Delete(ruta); }
    }

    [Fact]
    public void Con_firmante_de_la_TSL_revocado_responde_503_aunque_la_TSL_este_vigente()
    {
        var estado = new EstadoSaludTsl();
        var ruta = TslDePruebaHelper.EscribirArchivoTemporal([], emitidaEn: DateTimeOffset.UtcNow.AddDays(-1), proximaActualizacion: DateTimeOffset.UtcNow.AddDays(180));
        try
        {
            var lista = ListaConfianzaIofe.CargarDesdeArchivo(ruta);
            estado.RegistrarVigencia(lista, EstadoVigenciaTsl.Vigente, DateTimeOffset.UtcNow);
            estado.RegistrarRevocacionDelFirmante(EstadoRevocacion.Revoked, DateTimeOffset.UtcNow);

            var controller = new SaludController(estado);
            var resultado = controller.Tsl();

            var objeto = Assert.IsType<ObjectResult>(resultado.Result);
            Assert.Equal(StatusCodes.Status503ServiceUnavailable, objeto.StatusCode);
        }
        finally { File.Delete(ruta); }
    }
}
