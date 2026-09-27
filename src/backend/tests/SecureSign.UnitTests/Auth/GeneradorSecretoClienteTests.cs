using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SecureSign.Gateway;
using SecureSign.Gateway.Controllers;
using SecureSign.Shared.Auth;

namespace SecureSign.UnitTests.Auth;

/// <summary>Generación del client_secret y su hash Argon2id (RUNBOOK.md 12.39).</summary>
public sealed class GeneradorSecretoClienteTests
{
    [Fact]
    public void El_hash_generado_verifica_con_el_secreto_generado()
    {
        var (secreto, hash) = GeneradorSecretoCliente.Generar();

        Assert.True(Argon2idSecretHasher.Verificar(secreto, hash));
        Assert.False(Argon2idSecretHasher.Verificar(secreto + "x", hash));
    }

    [Fact]
    public void Cada_secreto_es_distinto_y_el_hash_lleva_su_propia_sal()
    {
        var a = GeneradorSecretoCliente.Generar();
        var b = GeneradorSecretoCliente.Generar();

        Assert.NotEqual(a.Secreto, b.Secreto);
        Assert.NotEqual(a.Hash, b.Hash);
    }

    [Fact]
    public void El_secreto_es_base64url_sin_relleno_y_de_la_longitud_de_32_bytes()
    {
        var (secreto, _) = GeneradorSecretoCliente.Generar();

        Assert.Matches(new Regex("^[A-Za-z0-9_-]+$"), secreto); // sin +, / ni =
        Assert.Equal(43, secreto.Length);                       // 32 bytes en base64url sin relleno
    }

    [Fact]
    public void El_secreto_no_aparece_dentro_del_hash()
    {
        var (secreto, hash) = GeneradorSecretoCliente.Generar();
        Assert.DoesNotContain(secreto, hash);
        Assert.StartsWith("$argon2id$", hash);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    [InlineData(23)]
    public void Menos_de_24_bytes_de_entropia_se_rechaza(int bytes) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => GeneradorSecretoCliente.Generar(bytes));

    [Fact]
    public void Con_mas_bytes_el_secreto_es_mas_largo()
    {
        var (corto, _) = GeneradorSecretoCliente.Generar(24);
        var (largo, _) = GeneradorSecretoCliente.Generar(48);
        Assert.True(largo.Length > corto.Length);
    }

    [Fact]
    public void El_hash_generado_sirve_para_autenticarse_contra_el_AuthController_real()
    {
        // El formato que produce la herramienta es exactamente el que consume ClientesDemo:SecretosHash.
        var (secreto, hash) = GeneradorSecretoCliente.Generar();
        string dir = Path.Combine(Path.GetTempPath(), $"securesign-llaves-{Guid.NewGuid():N}");
        try
        {
            var jwt = Options.Create(new JwtOptions { DirectorioLlaves = dir });
            var controlador = new AuthController(
                new JwtTokenService(new RsaKeyStore(jwt), jwt),
                Options.Create(new ClientesDemoOptions
                {
                    Clientes = [new ClienteDemo { ClientId = "cli", SecretosHash = [hash], TenantId = Guid.NewGuid(), ClienteIntegradorId = Guid.NewGuid(), Scopes = "documentos.leer" }],
                }),
                jwt);

            Assert.IsType<OkObjectResult>(controlador.ObtenerToken("client_credentials", "cli", secreto, scope: null));
            Assert.IsType<UnauthorizedObjectResult>(controlador.ObtenerToken("client_credentials", "cli", secreto + "x", scope: null));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* mejor esfuerzo */ } }
    }

    [Fact]
    public void Durante_una_rotacion_el_secreto_viejo_y_el_nuevo_autentican_a_la_vez_y_al_retirar_el_viejo_deja_de_valer()
    {
        var viejo = GeneradorSecretoCliente.Generar();
        var nuevo = GeneradorSecretoCliente.Generar();
        string dir = Path.Combine(Path.GetTempPath(), $"securesign-llaves-{Guid.NewGuid():N}");
        try
        {
            var jwt = Options.Create(new JwtOptions { DirectorioLlaves = dir });
            AuthController Con(params string[] hashes) => new(
                new JwtTokenService(new RsaKeyStore(jwt), jwt),
                Options.Create(new ClientesDemoOptions
                {
                    Clientes = [new ClienteDemo { ClientId = "cli", SecretosHash = hashes.ToList(), TenantId = Guid.NewGuid(), ClienteIntegradorId = Guid.NewGuid(), Scopes = "documentos.leer" }],
                }),
                jwt);

            var durante = Con(viejo.Hash, nuevo.Hash);
            Assert.IsType<OkObjectResult>(durante.ObtenerToken("client_credentials", "cli", viejo.Secreto, null));
            Assert.IsType<OkObjectResult>(durante.ObtenerToken("client_credentials", "cli", nuevo.Secreto, null));

            var despues = Con(nuevo.Hash); // el viejo se retiró de la lista
            Assert.IsType<OkObjectResult>(despues.ObtenerToken("client_credentials", "cli", nuevo.Secreto, null));
            Assert.IsType<UnauthorizedObjectResult>(despues.ObtenerToken("client_credentials", "cli", viejo.Secreto, null));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* mejor esfuerzo */ } }
    }
}
