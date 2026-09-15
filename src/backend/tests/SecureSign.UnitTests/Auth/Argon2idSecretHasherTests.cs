using SecureSign.Shared.Auth;

namespace SecureSign.UnitTests.Auth;

/// <summary>
/// Hash de client_secret (RUNBOOK.md 12.23). Reproduce el bug real
/// encontrado al generar el hash de config para ClientesDemo: un
/// off-by-one en el parseo de "m=19456" (se tomaba <c>AsSpan(1)</c> —
/// "=19456", con el signo igual incluido — en vez de <c>AsSpan(2)</c>),
/// que hacía que <see cref="Argon2idSecretHasher.Verificar"/> devolviera
/// SIEMPRE false, incluso con el secreto correcto — un secreto legítimo
/// jamás autenticaba. El hash en sí (<see cref="Argon2idSecretHasher.Hashear"/>)
/// nunca lanzaba y el string producido se veía perfectamente válido, así
/// que sin esta prueba de round-trip el bug solo aparecía al probar el
/// login real end-to-end.
/// </summary>
public sealed class Argon2idSecretHasherTests
{
    [Fact]
    public void Hashear_y_Verificar_con_el_mismo_secreto_da_verdadero()
    {
        string hash = Argon2idSecretHasher.Hashear("mi-secreto-de-prueba");
        Assert.True(Argon2idSecretHasher.Verificar("mi-secreto-de-prueba", hash));
    }

    [Fact]
    public void Verificar_con_secreto_incorrecto_da_falso()
    {
        string hash = Argon2idSecretHasher.Hashear("mi-secreto-de-prueba");
        Assert.False(Argon2idSecretHasher.Verificar("otro-secreto", hash));
    }

    [Fact]
    public void Hashear_nunca_produce_el_secreto_en_texto_plano()
    {
        string hash = Argon2idSecretHasher.Hashear("mi-secreto-de-prueba");
        Assert.DoesNotContain("mi-secreto-de-prueba", hash);
    }

    [Fact]
    public void Hashear_dos_veces_el_mismo_secreto_da_hashes_distintos()
    {
        // Sal aleatoria por llamada — si dos secretos idénticos dieran el
        // mismo hash, dos clientes con el mismo secreto serían
        // distinguibles comparando su configuración almacenada.
        string hash1 = Argon2idSecretHasher.Hashear("mi-secreto-de-prueba");
        string hash2 = Argon2idSecretHasher.Hashear("mi-secreto-de-prueba");
        Assert.NotEqual(hash1, hash2);
        Assert.True(Argon2idSecretHasher.Verificar("mi-secreto-de-prueba", hash1));
        Assert.True(Argon2idSecretHasher.Verificar("mi-secreto-de-prueba", hash2));
    }

    [Theory]
    [InlineData("")]
    [InlineData("no-es-un-hash-argon2id")]
    [InlineData("$argon2id$v=19$m=19456,t=2,p=1$sal-no-base64$hash-no-base64")]
    [InlineData("$argon2id$v=18$m=19456,t=2,p=1$c2Fs$aGFzaA==")] // versión distinta (18, no 19)
    [InlineData("$argon2id$v=19$m=abc,t=2,p=1$c2Fs$aGFzaA==")] // "m=abc" no es un número
    public void Verificar_con_hash_de_formato_invalido_da_falso_sin_lanzar(string hashInvalido)
    {
        Assert.False(Argon2idSecretHasher.Verificar("cualquier-secreto", hashInvalido));
    }

    [Fact]
    public void Verificar_con_hash_truncado_da_falso()
    {
        string hash = Argon2idSecretHasher.Hashear("mi-secreto-de-prueba");
        Assert.False(Argon2idSecretHasher.Verificar("mi-secreto-de-prueba", hash[..^8]));
    }

    [Fact]
    public void El_hash_real_configurado_para_el_cliente_demo_verifica_con_su_secreto_documentado()
    {
        // Mismo hash que appsettings.json (ClientesDemo:Clientes[0].SecretosHash)
        // — si alguien regenera ese valor sin usar Argon2idSecretHasher.Hashear,
        // o el algoritmo cambia de forma incompatible, esta prueba lo detecta
        // sin necesidad de levantar el Gateway completo.
        const string hashConfigurado = "$argon2id$v=19$m=19456,t=2,p=1$zz+ZC8QFnPEcCCQGbGuQTg==$kR7K+RueLr/NzwIvesNKbOE4o2kx0JBnaLOxQDj+4vU=";
        Assert.True(Argon2idSecretHasher.Verificar("demo-secret-not-for-production", hashConfigurado));
    }
}
