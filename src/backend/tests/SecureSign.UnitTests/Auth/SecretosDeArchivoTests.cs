using Microsoft.Extensions.Configuration;
using SecureSign.Shared.Auth;

namespace SecureSign.UnitTests.Auth;

/// <summary>Secretos entregados como archivos (Docker/Kubernetes secrets) — RUNBOOK.md 12.30.</summary>
public sealed class SecretosDeArchivoTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"securesign-secrets-{Guid.NewGuid():N}");

    public SecretosDeArchivoTests() => Directory.CreateDirectory(_dir);

    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { /* mejor esfuerzo */ } }

    private void Secreto(string nombre, string contenido) => File.WriteAllText(Path.Combine(_dir, nombre), contenido);

    private static IConfigurationRoot Construir(IDictionary<string, string?> base_, string directorio) =>
        new ConfigurationBuilder().AddInMemoryCollection(base_).AddSecureSignSecretosDeArchivo(directorio).Build();

    [Fact]
    public void El_nombre_del_archivo_con_doble_guion_bajo_es_la_clave_de_configuracion()
    {
        Secreto("Jwt__SecretoClienteInterno", "valor-del-archivo-0123456789abcdef0123");
        Secreto("Jwt__ServiciosEmisores__0__Secretos__0", "otro-valor-del-archivo-0123456789abcdef");

        var config = Construir(new Dictionary<string, string?>(), _dir);
        var jwt = config.GetSection("Jwt").Get<JwtOptions>()!;

        Assert.Equal("valor-del-archivo-0123456789abcdef0123", jwt.SecretoClienteInterno);
        Assert.Equal("otro-valor-del-archivo-0123456789abcdef", jwt.ServiciosEmisores.Single().Secretos.Single());
    }

    [Fact]
    public void El_salto_de_linea_final_del_archivo_no_forma_parte_del_secreto()
    {
        Secreto("Jwt__SecretoClienteInterno", "valor-con-salto-0123456789abcdef0123\n");
        Secreto("Jwt__NombreServicio", "securesign-signature-api\r\n");

        var config = Construir(new Dictionary<string, string?>(), _dir);

        Assert.Equal("valor-con-salto-0123456789abcdef0123", config["Jwt:SecretoClienteInterno"]);
        Assert.Equal("securesign-signature-api", config["Jwt:NombreServicio"]);
    }

    [Fact]
    public void El_archivo_tiene_precedencia_sobre_el_valor_de_appsettings()
    {
        Secreto("Jwt__SecretoClienteInterno", "valor-real-del-archivo-0123456789abcdef");

        var config = Construir(new Dictionary<string, string?>
        {
            ["Jwt:SecretoClienteInterno"] = "dev-only-valor-de-ejemplo-del-repositorio",
            ["Jwt:Authority"] = "http://gateway:8080",
        }, _dir);

        Assert.Equal("valor-real-del-archivo-0123456789abcdef", config["Jwt:SecretoClienteInterno"]);
        Assert.Equal("http://gateway:8080", config["Jwt:Authority"]); // lo que no está en archivo se conserva
    }

    [Fact]
    public void Un_directorio_inexistente_se_ignora_sin_lanzar()
    {
        var config = Construir(new Dictionary<string, string?> { ["Jwt:Authority"] = "http://gateway:8080" },
            Path.Combine(_dir, "no-existe"));

        Assert.Equal("http://gateway:8080", config["Jwt:Authority"]);
    }

    [Fact]
    public void Un_secreto_en_archivo_satisface_la_regla_de_arranque_fuera_de_Development()
    {
        // appsettings trae el valor dev-only-...; el archivo lo reemplaza y el servicio puede arrancar en producción.
        Secreto("Jwt__SecretoClienteInterno", "secreto-productivo-largo-0123456789abcdef");
        var config = Construir(new Dictionary<string, string?>
        {
            ["Jwt:SecretoClienteInterno"] = "dev-only-secret-signature-api-do-not-use-in-production",
        }, _dir);

        var jwt = config.GetSection("Jwt").Get<JwtOptions>()!;

        Assert.Empty(ValidadorOpcionesJwt.Validar(jwt, esDesarrollo: false));
    }

    [Fact]
    public void La_variable_de_entorno_redefine_el_directorio_por_defecto()
    {
        Secreto("Jwt__SecretoClienteInterno", "valor-por-variable-0123456789abcdef0123");
        var anterior = Environment.GetEnvironmentVariable(SecretosDeArchivo.VariableDirectorio);
        try
        {
            Environment.SetEnvironmentVariable(SecretosDeArchivo.VariableDirectorio, _dir);
            var config = new ConfigurationBuilder().AddSecureSignSecretosDeArchivo().Build();
            Assert.Equal("valor-por-variable-0123456789abcdef0123", config["Jwt:SecretoClienteInterno"]);
        }
        finally { Environment.SetEnvironmentVariable(SecretosDeArchivo.VariableDirectorio, anterior); }
    }
}
