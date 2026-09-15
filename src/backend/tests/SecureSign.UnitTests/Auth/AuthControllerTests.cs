using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SecureSign.Gateway;
using SecureSign.Gateway.Controllers;
using SecureSign.Shared.Auth;

namespace SecureSign.UnitTests.Auth;

/// <summary>
/// POST /api/auth/token contra el catálogo ClientesDemo — de punta a punta
/// con Argon2idSecretHasher real (RUNBOOK.md 12.23), no con un doble de
/// prueba, para que un bug como el de <see cref="Argon2idSecretHasherTests"/>
/// (el secreto correcto rechazado siempre) se detecte también a este nivel.
/// </summary>
public sealed class AuthControllerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"securesign-llaves-{Guid.NewGuid():N}");
    private const string ClientId = "cliente-de-prueba";
    private const string Secreto = "secreto-de-prueba-correcto";

    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { /* mejor esfuerzo */ } }

    private AuthController Construir()
    {
        var jwtOpciones = Options.Create(new JwtOptions
        {
            Issuer = "https://api.securesign.pe",
            Audience = "securesign-platform",
            DirectorioLlaves = _dir,
            SecretoClienteInterno = "secreto-interno-de-prueba",
        });
        var llaves = new RsaKeyStore(jwtOpciones);
        var tokenService = new JwtTokenService(llaves, jwtOpciones);

        var clientesDemo = Options.Create(new ClientesDemoOptions
        {
            Clientes =
            [
                new ClienteDemo
                {
                    ClientId = ClientId,
                    SecretosHash = [Argon2idSecretHasher.Hashear(Secreto)],
                    TenantId = Guid.NewGuid(),
                    ClienteIntegradorId = Guid.NewGuid(),
                    Scopes = "documentos.crear documentos.leer",
                },
            ],
        });

        return new AuthController(tokenService, clientesDemo, jwtOpciones);
    }

    [Fact]
    public void Client_credentials_con_secreto_correcto_emite_token()
    {
        var controlador = Construir();

        var resultado = controlador.ObtenerToken("client_credentials", ClientId, Secreto, scope: null);

        var ok = Assert.IsType<OkObjectResult>(resultado);
        Assert.NotNull(ok.Value);
    }

    [Fact]
    public void Client_credentials_con_secreto_incorrecto_rechaza()
    {
        var controlador = Construir();

        var resultado = controlador.ObtenerToken("client_credentials", ClientId, "secreto-equivocado", scope: null);

        Assert.IsType<UnauthorizedObjectResult>(resultado);
    }

    [Fact]
    public void Client_credentials_con_client_id_desconocido_rechaza()
    {
        var controlador = Construir();

        var resultado = controlador.ObtenerToken("client_credentials", "cliente-que-no-existe", Secreto, scope: null);

        Assert.IsType<UnauthorizedObjectResult>(resultado);
    }

    [Fact]
    public void Grant_type_distinto_de_client_credentials_rechaza()
    {
        var controlador = Construir();

        var resultado = controlador.ObtenerToken("authorization_code", ClientId, Secreto, scope: null);

        Assert.IsType<BadRequestObjectResult>(resultado);
    }
}
