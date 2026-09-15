using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SecureSign.Shared.Auth;

namespace SecureSign.UnitTests.Auth;

/// <summary>
/// Prueba real de firma RS256 + validación — no contra mocks, contra el
/// mismo par JwtTokenService/RsaKeyStore que corre en el Gateway (ver
/// RUNBOOK.md 12.21). Confirma lo que todo el resto del sistema asume:
/// firmar con la llave activa, validar con su llave pública, valida; con
/// cualquier otra llave, o el texto alterado, rechaza.
/// </summary>
public sealed class JwtRoundTripTests : IDisposable
{
    private readonly List<string> _directorios = [];

    public void Dispose()
    {
        foreach (var dir in _directorios) { try { Directory.Delete(dir, recursive: true); } catch { /* mejor esfuerzo */ } }
    }

    /// <summary>Cada llamada usa un directorio NUEVO — así dos llaveros construidos en la misma prueba son, de verdad, llaves RSA distintas (no la misma releída).</summary>
    private (JwtTokenService Servicio, RsaKeyStore Llaves) Construir()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"securesign-llaves-{Guid.NewGuid():N}");
        _directorios.Add(dir);
        var opciones = Options.Create(new JwtOptions { Issuer = "https://api.securesign.pe", DirectorioLlaves = dir });
        var llaves = new RsaKeyStore(opciones);
        return (new JwtTokenService(llaves, opciones), llaves);
    }

    private static TokenValidationParameters ParametrosValidos(string issuer, string audience, SecurityKey llavePublica) => new()
    {
        ValidateIssuer = true,
        ValidIssuer = issuer,
        ValidateAudience = true,
        ValidAudience = audience,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = llavePublica,
    };

    [Fact]
    public void Token_firmado_valida_correctamente_con_la_llave_publica_de_la_misma_llave_activa()
    {
        var (servicio, llaves) = Construir();
        var token = servicio.EmitirConClaims(new Dictionary<string, string> { ["scope"] = "internal-service" }, "securesign-internal-services", 5);

        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var principal = handler.ValidateToken(token.AccessToken,
            ParametrosValidos("https://api.securesign.pe", "securesign-internal-services", llaves.LlaveDeFirmaActiva.ComoSecurityKey()),
            out var validado);

        Assert.Equal("internal-service", principal.FindFirst("scope")?.Value);
        Assert.Equal(SecurityAlgorithms.RsaSha256, ((System.IdentityModel.Tokens.Jwt.JwtSecurityToken)validado).SignatureAlgorithm);
        Assert.Equal(llaves.LlaveDeFirmaActiva.Kid, ((System.IdentityModel.Tokens.Jwt.JwtSecurityToken)validado).Header.Kid);
    }

    [Fact]
    public void Token_firmado_NO_valida_con_la_llave_publica_de_otro_llavero()
    {
        var (servicio, _) = Construir();
        var token = servicio.EmitirConClaims(new Dictionary<string, string> { ["scope"] = "internal-service" }, "securesign-internal-services", 5);

        var (_, llavesImpostoras) = Construir(); // otro directorio, otra llave RSA

        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        Assert.ThrowsAny<SecurityTokenException>(() => handler.ValidateToken(token.AccessToken,
            ParametrosValidos("https://api.securesign.pe", "securesign-internal-services", llavesImpostoras.LlaveDeFirmaActiva.ComoSecurityKey()),
            out _));
    }

    [Fact]
    public void EmitirConClaims_incluye_todos_los_claims_pedidos()
    {
        var (servicio, llaves) = Construir();
        var token = servicio.EmitirConClaims(
            new Dictionary<string, string> { ["tenant_id"] = "t1", ["act"] = "securesign-signature-api" },
            "securesign-internal-services", 5);

        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var principal = handler.ValidateToken(token.AccessToken,
            ParametrosValidos("https://api.securesign.pe", "securesign-internal-services", llaves.LlaveDeFirmaActiva.ComoSecurityKey()),
            out _);

        Assert.Equal("t1", principal.FindFirst("tenant_id")?.Value);
        Assert.Equal("securesign-signature-api", principal.FindFirst("act")?.Value);
    }
}
