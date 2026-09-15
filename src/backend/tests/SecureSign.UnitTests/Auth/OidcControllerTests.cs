using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SecureSign.Gateway.Controllers;
using SecureSign.Shared.Auth;

namespace SecureSign.UnitTests.Auth;

/// <summary>
/// Forma real de /.well-known/openid-configuration y /.well-known/jwks.json
/// — ver RUNBOOK.md 12.21. Cada servicio de la plataforma depende de que
/// jwks.json traiga exactamente los campos que JwksLlaveResolver espera
/// (kid/kty/n/e); esta prueba es la que rompería primero si alguno de los
/// dos lados de ese contrato cambiara sin el otro.
/// </summary>
public sealed class OidcControllerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"securesign-llaves-{Guid.NewGuid():N}");

    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { /* mejor esfuerzo */ } }

    private OidcController Construir(out RsaKeyStore llaves)
    {
        var opciones = Options.Create(new JwtOptions { Issuer = "https://api.securesign.pe", DirectorioLlaves = _dir });
        llaves = new RsaKeyStore(opciones);
        var controlador = new OidcController(llaves, opciones)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { Request = { Scheme = "http", Host = new HostString("gateway", 8080) } },
            },
        };
        return controlador;
    }

    private static JsonDocument ComoJson(IActionResult resultado)
    {
        var valor = Assert.IsType<OkObjectResult>(resultado).Value!;
        return JsonDocument.Parse(JsonSerializer.Serialize(valor));
    }

    [Fact]
    public void Discovery_publica_issuer_fijo_y_jwks_uri_relativa_a_la_peticion()
    {
        var controlador = Construir(out _);
        using var doc = ComoJson(controlador.Discovery());

        Assert.Equal("https://api.securesign.pe", doc.RootElement.GetProperty("issuer").GetString());
        Assert.Equal("http://gateway:8080/.well-known/jwks.json", doc.RootElement.GetProperty("jwks_uri").GetString());
        Assert.Equal("http://gateway:8080/api/auth/token", doc.RootElement.GetProperty("token_endpoint").GetString());
    }

    [Fact]
    public void Jwks_publica_la_llave_activa_con_los_campos_que_JwksLlaveResolver_necesita()
    {
        var controlador = Construir(out var llaves);
        using var doc = ComoJson(controlador.Jwks());

        var primera = doc.RootElement.GetProperty("keys")[0];
        Assert.Equal("RSA", primera.GetProperty("kty").GetString());
        Assert.Equal(llaves.LlaveDeFirmaActiva.Kid, primera.GetProperty("kid").GetString());
        Assert.False(string.IsNullOrEmpty(primera.GetProperty("n").GetString()));
        Assert.False(string.IsNullOrEmpty(primera.GetProperty("e").GetString()));
    }

    [Fact]
    public void Jwks_nunca_publica_la_llave_privada()
    {
        var controlador = Construir(out _);
        using var doc = ComoJson(controlador.Jwks());

        string json = doc.RootElement.ToString();
        Assert.DoesNotContain("PRIVATE KEY", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"d\":", json); // exponente privado RSA en JWK — nunca debe aparecer
    }
}
