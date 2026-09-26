using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SecureSign.Gateway;
using SecureSign.Gateway.Controllers;
using SecureSign.Shared.Auth;

namespace SecureSign.UnitTests.Auth;

/// <summary>
/// <c>POST /api/auth/interno/emitir</c>: autenticación por servicio (nombre +
/// secreto propio, rotación sin corte), política de emisión por tipo de token
/// y regla de arranque contra secretos de desarrollo — RUNBOOK.md 12.27/12.28.
/// </summary>
public sealed class SecretoInternoTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"securesign-llaves-{Guid.NewGuid():N}");

    private const string Signature = "securesign-signature-api";
    private const string Documents = "securesign-documents-api";
    private const string SecretoSignature = "secreto-signature-de-prueba-0123456789abcdef";
    private const string SecretoDocuments = "secreto-documents-de-prueba-0123456789abcdef";
    private const string SecretoNuevo = "secreto-signature-nuevo-de-prueba-0123456789";

    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { /* mejor esfuerzo */ } }

    private JwtOptions Opciones(params string[] secretosSignatureExtra) => new()
    {
        DirectorioLlaves = _dir,
        ServiciosEmisores =
        [
            new ServicioEmisorOpciones { Nombre = Signature, Secretos = [SecretoSignature, .. secretosSignatureExtra], PuedeEmitirTickets = true },
            new ServicioEmisorOpciones { Nombre = Documents, Secretos = [SecretoDocuments] },
        ],
    };

    private AuthController Construir(JwtOptions? opciones = null)
    {
        var jwt = Options.Create(opciones ?? Opciones());
        var tokenService = new JwtTokenService(new RsaKeyStore(jwt), jwt);
        return new AuthController(tokenService, Options.Create(new ClientesDemoOptions()), jwt)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }

    private static Dictionary<string, string> ClaimsIntercambio(string actor) => new()
    {
        [ClaimsSecureSign.TenantId] = Guid.NewGuid().ToString(),
        [ClaimsSecureSign.Scope] = "internal-service",
        ["act"] = actor,
    };

    private static Dictionary<string, string> ClaimsTicket() => new()
    {
        [ClaimsSecureSign.TenantId] = Guid.NewGuid().ToString(),
        [ClaimsSecureSign.UsuarioId] = Guid.NewGuid().ToString(),
        [ClaimsSecureSign.Scope] = "firmar:local",
        ["jti"] = Guid.NewGuid().ToString("N"),
        [EmisorTicketFirmaLocal.ClaimTipo] = EmisorTicketFirmaLocal.ValorTipoTicketFirmaLocal,
        [EmisorTicketFirmaLocal.ClaimSolicitudFirmaId] = Guid.NewGuid().ToString(),
        [EmisorTicketFirmaLocal.ClaimFlujoFirmaId] = Guid.NewGuid().ToString(),
        [EmisorTicketFirmaLocal.ClaimDocumentoId] = Guid.NewGuid().ToString(),
        [EmisorTicketFirmaLocal.ClaimDocumentoHash] = new string('a', 64),
    };

    private static IActionResult Emitir(
        AuthController c, string? servicio, string? secreto,
        Dictionary<string, string> claims, string audiencia = "securesign-internal-services", int minutos = 2)
    {
        if (servicio is not null) c.Request.Headers["X-Internal-Service"] = servicio;
        if (secreto is not null) c.Request.Headers["X-Internal-Client-Secret"] = secreto;
        return c.EmitirInterno(new AuthController.EmitirTokenInternoRequest(claims, audiencia, minutos));
    }

    private static void AssertPolitica(IActionResult r) =>
        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(r).StatusCode);

    // --- Autenticación por servicio ---

    [Fact]
    public void Servicio_con_su_secreto_obtiene_un_token_de_intercambio() =>
        Assert.IsType<OkObjectResult>(Emitir(Construir(), Signature, SecretoSignature, ClaimsIntercambio(Signature)));

    [Fact]
    public void El_secreto_de_otro_servicio_no_sirve() =>
        Assert.IsType<UnauthorizedObjectResult>(Emitir(Construir(), Signature, SecretoDocuments, ClaimsIntercambio(Signature)));

    [Fact]
    public void Servicio_desconocido_se_rechaza() =>
        Assert.IsType<UnauthorizedObjectResult>(Emitir(Construir(), "otro-servicio", SecretoSignature, ClaimsIntercambio("otro-servicio")));

    [Fact]
    public void Sin_encabezados_rechaza()
    {
        Assert.IsType<UnauthorizedObjectResult>(Emitir(Construir(), servicio: null, secreto: null, ClaimsIntercambio(Signature)));
        Assert.IsType<UnauthorizedObjectResult>(Emitir(Construir(), Signature, secreto: null, ClaimsIntercambio(Signature)));
        Assert.IsType<UnauthorizedObjectResult>(Emitir(Construir(), servicio: null, SecretoSignature, ClaimsIntercambio(Signature)));
    }

    [Fact]
    public void Durante_la_rotacion_de_un_servicio_acepta_el_viejo_y_el_nuevo()
    {
        var opciones = Opciones(SecretoNuevo);
        Assert.IsType<OkObjectResult>(Emitir(Construir(opciones), Signature, SecretoSignature, ClaimsIntercambio(Signature)));
        Assert.IsType<OkObjectResult>(Emitir(Construir(opciones), Signature, SecretoNuevo, ClaimsIntercambio(Signature)));
    }

    [Fact]
    public void Un_secreto_retirado_deja_de_valer() =>
        Assert.IsType<UnauthorizedObjectResult>(Emitir(Construir(), Signature, SecretoNuevo, ClaimsIntercambio(Signature)));

    [Fact]
    public void Un_secreto_vacio_en_la_lista_no_autoriza_un_encabezado_vacio() =>
        Assert.IsType<UnauthorizedObjectResult>(Emitir(Construir(Opciones("")), Signature, "", ClaimsIntercambio(Signature)));

    // --- Política de emisión: intercambio ---

    [Fact]
    public void Intercambio_no_puede_hacerse_pasar_por_otro_servicio() =>
        AssertPolitica(Emitir(Construir(), Documents, SecretoDocuments, ClaimsIntercambio(Signature)));

    [Fact]
    public void Intercambio_no_puede_pedir_audiencia_externa() =>
        AssertPolitica(Emitir(Construir(), Documents, SecretoDocuments, ClaimsIntercambio(Documents), audiencia: "securesign-platform"));

    [Fact]
    public void Intercambio_no_puede_pedir_otro_scope()
    {
        var claims = ClaimsIntercambio(Documents);
        claims[ClaimsSecureSign.Scope] = "documentos.crear documentos.leer firmas.crear evidencias.leer";
        AssertPolitica(Emitir(Construir(), Documents, SecretoDocuments, claims));
    }

    [Fact]
    public void Intercambio_no_puede_llevar_claims_fuera_de_la_lista()
    {
        var claims = ClaimsIntercambio(Documents);
        claims[ClaimsSecureSign.ClienteIntegradorId] = Guid.NewGuid().ToString();
        AssertPolitica(Emitir(Construir(), Documents, SecretoDocuments, claims));
    }

    [Fact]
    public void Intercambio_no_puede_durar_mas_que_la_vida_interna() =>
        AssertPolitica(Emitir(Construir(), Documents, SecretoDocuments, ClaimsIntercambio(Documents), minutos: 60));

    [Fact]
    public void Sin_claims_o_sin_expiracion_es_rechazado_por_la_politica()
    {
        AssertPolitica(Emitir(Construir(), Documents, SecretoDocuments, new Dictionary<string, string>()));
        AssertPolitica(Emitir(Construir(), Documents, SecretoDocuments, ClaimsIntercambio(Documents), minutos: 0));
    }

    // --- Política de emisión: ticket del Firmador Local ---

    [Fact]
    public void Signature_puede_emitir_un_ticket_del_firmador_local() =>
        Assert.IsType<OkObjectResult>(Emitir(Construir(), Signature, SecretoSignature, ClaimsTicket(), audiencia: "securesign-platform"));

    [Fact]
    public void Un_servicio_sin_permiso_de_tickets_no_puede_emitirlos() =>
        AssertPolitica(Emitir(Construir(), Documents, SecretoDocuments, ClaimsTicket(), audiencia: "securesign-platform"));

    [Fact]
    public void Ticket_con_audiencia_interna_es_rechazado() =>
        AssertPolitica(Emitir(Construir(), Signature, SecretoSignature, ClaimsTicket(), audiencia: "securesign-internal-services"));

    [Fact]
    public void Ticket_con_scope_ampliado_es_rechazado()
    {
        var claims = ClaimsTicket();
        claims[ClaimsSecureSign.Scope] = "firmar:local documentos.leer";
        AssertPolitica(Emitir(Construir(), Signature, SecretoSignature, claims, audiencia: "securesign-platform"));
    }

    [Fact]
    public void Ticket_con_claim_extra_es_rechazado()
    {
        var claims = ClaimsTicket();
        claims[ClaimsSecureSign.ClienteIntegradorId] = Guid.NewGuid().ToString();
        AssertPolitica(Emitir(Construir(), Signature, SecretoSignature, claims, audiencia: "securesign-platform"));
    }

    [Fact]
    public void Ticket_no_puede_durar_mas_de_cinco_minutos() =>
        AssertPolitica(Emitir(Construir(), Signature, SecretoSignature, ClaimsTicket(), audiencia: "securesign-platform", minutos: 60));

    // --- La política no debe divergir de los emisores reales ---

    private sealed class CapturaHandler : HttpMessageHandler
    {
        public string? Cuerpo { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Cuerpo = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"access_token":"jwt-de-prueba","expires_in":120}""", Encoding.UTF8, "application/json"),
            };
        }
    }

    private static (Dictionary<string, string> Claims, string Audiencia, int Minutos) LeerSolicitud(string cuerpo)
    {
        using var doc = JsonDocument.Parse(cuerpo);
        var raiz = doc.RootElement;
        var claims = raiz.GetProperty("claims").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!);
        return (claims, raiz.GetProperty("audiencia").GetString()!, raiz.GetProperty("minutosExpiracion").GetInt32());
    }

    [Fact]
    public async Task La_politica_acepta_lo_que_TokenExchangeService_realmente_pide()
    {
        var opciones = Opciones();
        opciones.Authority = "http://gateway:8080";
        var handler = new CapturaHandler();
        var servicio = new TokenExchangeService(new EmisorTokenInterno(new HttpClient(handler) { BaseAddress = new Uri("http://gateway:8080") }), Options.Create(opciones));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimsSecureSign.TenantId, Guid.NewGuid().ToString()), new Claim(ClaimsSecureSign.UsuarioId, Guid.NewGuid().ToString())], "prueba"));

        await servicio.Exchange(principal, Documents);
        var (claims, audiencia, minutos) = LeerSolicitud(handler.Cuerpo!);
        Assert.Null(PoliticaEmisionInterna.Evaluar(opciones.ServiciosEmisores[1], claims, audiencia, minutos, opciones));

        await servicio.EmitirTokenDeSistema(Documents);
        (claims, audiencia, minutos) = LeerSolicitud(handler.Cuerpo!);
        Assert.Null(PoliticaEmisionInterna.Evaluar(opciones.ServiciosEmisores[1], claims, audiencia, minutos, opciones));
    }

    [Fact]
    public async Task La_politica_acepta_lo_que_EmisorTicketFirmaLocal_realmente_pide()
    {
        var opciones = Opciones();
        var handler = new CapturaHandler();
        var emisor = new EmisorTicketFirmaLocal(new EmisorTokenInterno(new HttpClient(handler) { BaseAddress = new Uri("http://gateway:8080") }), Options.Create(opciones));

        await emisor.Emitir(new DatosTicketFirmaLocal(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new string('b', 64), Origen: "https://firma.securesign.pe"));

        var (claims, audiencia, minutos) = LeerSolicitud(handler.Cuerpo!);
        Assert.Null(PoliticaEmisionInterna.Evaluar(opciones.ServiciosEmisores[0], claims, audiencia, minutos, opciones));
    }

    // --- Regla de arranque contra secretos de desarrollo ---

    private const string SecretoDev = "dev-only-secret-signature-api-do-not-use-in-production";

    [Fact]
    public void En_desarrollo_permite_los_secretos_de_ejemplo() =>
        Assert.Empty(ValidadorOpcionesJwt.Validar(new JwtOptions { SecretoClienteInterno = SecretoDev }, esDesarrollo: true));

    [Fact]
    public void Fuera_de_desarrollo_rechaza_el_secreto_de_ejemplo() =>
        Assert.Single(ValidadorOpcionesJwt.Validar(new JwtOptions { SecretoClienteInterno = SecretoDev }, esDesarrollo: false));

    [Fact]
    public void Fuera_de_desarrollo_rechaza_un_secreto_corto() =>
        Assert.Single(ValidadorOpcionesJwt.Validar(new JwtOptions { SecretoClienteInterno = "corto" }, esDesarrollo: false));

    [Fact]
    public void Fuera_de_desarrollo_revisa_los_secretos_de_cada_servicio_emisor()
    {
        var opciones = Opciones();
        opciones.PuertoInterno = 8081;
        opciones.ServiciosEmisores[1].Secretos = [SecretoDev];
        Assert.Single(ValidadorOpcionesJwt.Validar(opciones, esDesarrollo: false));
    }

    [Fact]
    public void Fuera_de_desarrollo_acepta_secretos_largos_reales()
    {
        var opciones = Opciones(SecretoNuevo);
        opciones.PuertoInterno = 8081;
        Assert.Empty(ValidadorOpcionesJwt.Validar(opciones, esDesarrollo: false));
    }

    [Fact]
    public void Fuera_de_desarrollo_exige_el_puerto_interno_si_hay_servicios_emisores() =>
        Assert.Contains("PuertoInterno", Assert.Single(ValidadorOpcionesJwt.Validar(Opciones(), esDesarrollo: false)));

    [Fact]
    public void En_desarrollo_el_puerto_interno_es_opcional() =>
        Assert.Empty(ValidadorOpcionesJwt.Validar(Opciones(), esDesarrollo: true));

    // --- Listener interno ---

    private AuthController EnPuerto(int puertoLocal, int? puertoInterno)
    {
        var opciones = Opciones();
        opciones.PuertoInterno = puertoInterno;
        var controlador = Construir(opciones);
        controlador.HttpContext.Connection.LocalPort = puertoLocal;
        return controlador;
    }

    [Fact]
    public void En_el_puerto_publico_el_endpoint_interno_no_existe() =>
        Assert.IsType<NotFoundResult>(Emitir(EnPuerto(puertoLocal: 8080, puertoInterno: 8081), Signature, SecretoSignature, ClaimsIntercambio(Signature)));

    [Fact]
    public void En_el_puerto_interno_el_endpoint_funciona() =>
        Assert.IsType<OkObjectResult>(Emitir(EnPuerto(puertoLocal: 8081, puertoInterno: 8081), Signature, SecretoSignature, ClaimsIntercambio(Signature)));

    [Fact]
    public void En_el_puerto_publico_ni_siquiera_un_secreto_correcto_revela_nada() =>
        // 404 y no 401/403: desde fuera no se distingue "endpoint inexistente" de "secreto malo".
        Assert.IsType<NotFoundResult>(Emitir(EnPuerto(puertoLocal: 8080, puertoInterno: 8081), Signature, "secreto-incorrecto", ClaimsIntercambio(Signature)));

    [Fact]
    public void Sin_puerto_interno_configurado_no_se_restringe_el_puerto() =>
        Assert.IsType<OkObjectResult>(Emitir(EnPuerto(puertoLocal: 8080, puertoInterno: null), Signature, SecretoSignature, ClaimsIntercambio(Signature)));

    [Fact]
    public void Servicio_sin_secreto_configurado_no_falla_la_validacion() =>
        Assert.Empty(ValidadorOpcionesJwt.Validar(new JwtOptions(), esDesarrollo: false));
}
