using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SecureSign.Gateway;
using SecureSign.Gateway.Controllers;
using SecureSign.Shared.Auth;

namespace SecureSign.UnitTests.Auth;

/// <summary>
/// Secreto interno de <c>POST /api/auth/interno/emitir</c>: rotación sin corte
/// (lista de secretos aceptados) y regla de arranque contra secretos de
/// desarrollo fuera de Development — RUNBOOK.md 12.27.
/// </summary>
public sealed class SecretoInternoTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"securesign-llaves-{Guid.NewGuid():N}");
    private const string SecretoActual = "secreto-interno-actual-de-prueba-0123456789";
    private const string SecretoNuevo = "secreto-interno-nuevo-de-prueba-0123456789";

    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { /* mejor esfuerzo */ } }

    private AuthController Construir(params string[] adicionales)
    {
        var jwt = Options.Create(new JwtOptions
        {
            DirectorioLlaves = _dir,
            SecretoClienteInterno = SecretoActual,
            SecretosClienteInternoAdicionales = adicionales,
        });
        var tokenService = new JwtTokenService(new RsaKeyStore(jwt), jwt);
        return new AuthController(tokenService, Options.Create(new ClientesDemoOptions()), jwt)
        {
            ControllerContext = new ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() },
        };
    }

    private static AuthController.EmitirTokenInternoRequest Solicitud() =>
        new(new Dictionary<string, string> { ["sub"] = "servicio-de-prueba" }, "securesign-internal-services", 2);

    private static IActionResult Emitir(AuthController controlador, string? secreto)
    {
        if (secreto is not null) controlador.Request.Headers["X-Internal-Client-Secret"] = secreto;
        return controlador.EmitirInterno(Solicitud());
    }

    [Fact]
    public void Acepta_el_secreto_actual() =>
        Assert.IsType<OkObjectResult>(Emitir(Construir(), SecretoActual));

    [Fact]
    public void Durante_la_rotacion_acepta_actual_y_nuevo()
    {
        Assert.IsType<OkObjectResult>(Emitir(Construir(SecretoNuevo), SecretoActual));
        Assert.IsType<OkObjectResult>(Emitir(Construir(SecretoNuevo), SecretoNuevo));
    }

    [Fact]
    public void Un_secreto_retirado_de_la_lista_deja_de_valer() =>
        Assert.IsType<UnauthorizedObjectResult>(Emitir(Construir(), SecretoNuevo));

    [Fact]
    public void Sin_encabezado_rechaza() =>
        Assert.IsType<UnauthorizedObjectResult>(Emitir(Construir(SecretoNuevo), secreto: null));

    [Fact]
    public void Secreto_vacio_en_la_lista_de_adicionales_no_autoriza_un_encabezado_vacio() =>
        Assert.IsType<UnauthorizedObjectResult>(Emitir(Construir(""), ""));

    [Fact]
    public void En_desarrollo_permite_el_secreto_de_ejemplo()
    {
        var opciones = new JwtOptions { SecretoClienteInterno = "dev-only-internal-client-secret-do-not-use-in-production" };
        Assert.Empty(ValidadorOpcionesJwt.Validar(opciones, esDesarrollo: true));
    }

    [Fact]
    public void Fuera_de_desarrollo_rechaza_el_secreto_de_ejemplo()
    {
        var opciones = new JwtOptions { SecretoClienteInterno = "dev-only-internal-client-secret-do-not-use-in-production" };
        Assert.Single(ValidadorOpcionesJwt.Validar(opciones, esDesarrollo: false));
    }

    [Fact]
    public void Fuera_de_desarrollo_rechaza_un_secreto_corto()
    {
        var opciones = new JwtOptions { SecretoClienteInterno = "corto" };
        Assert.Single(ValidadorOpcionesJwt.Validar(opciones, esDesarrollo: false));
    }

    [Fact]
    public void Fuera_de_desarrollo_revisa_tambien_los_secretos_adicionales()
    {
        var opciones = new JwtOptions { SecretoClienteInterno = SecretoActual, SecretosClienteInternoAdicionales = ["dev-only-otro"] };
        Assert.Single(ValidadorOpcionesJwt.Validar(opciones, esDesarrollo: false));
    }

    [Fact]
    public void Fuera_de_desarrollo_acepta_secretos_largos_reales()
    {
        var opciones = new JwtOptions { SecretoClienteInterno = SecretoActual, SecretosClienteInternoAdicionales = [SecretoNuevo] };
        Assert.Empty(ValidadorOpcionesJwt.Validar(opciones, esDesarrollo: false));
    }

    [Fact]
    public void Servicio_sin_secreto_configurado_no_falla_la_validacion()
    {
        // Identity/Evidence/Audit/Crypto no piden tokens internos: sin secreto, nada que validar.
        Assert.Empty(ValidadorOpcionesJwt.Validar(new JwtOptions(), esDesarrollo: false));
    }
}
