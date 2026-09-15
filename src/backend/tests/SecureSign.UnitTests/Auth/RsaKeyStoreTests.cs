using Microsoft.Extensions.Options;
using SecureSign.Shared.Auth;

namespace SecureSign.UnitTests.Auth;

/// <summary>
/// RsaKeyStore es la pieza que cierra "secretos externalizados" (informe de
/// preauditoría INDECOPI/IOFE, hallazgo P1) — ver RUNBOOK.md 12.21. Estas
/// pruebas confirman el contrato real que el resto del sistema asume: si el
/// directorio está vacío genera una llave sola, la llave activa es siempre
/// la más reciente, y recargar el mismo directorio reproduce exactamente
/// las mismas llaves (no las regenera).
/// </summary>
public sealed class RsaKeyStoreTests : IDisposable
{
    private readonly List<string> _directorios = [];

    public void Dispose()
    {
        foreach (var dir in _directorios) { try { Directory.Delete(dir, recursive: true); } catch { /* mejor esfuerzo */ } }
    }

    private string NuevoDirectorio()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"securesign-llaves-{Guid.NewGuid():N}");
        _directorios.Add(dir);
        return dir;
    }

    private static RsaKeyStore Construir(string directorio) =>
        new(Options.Create(new JwtOptions { DirectorioLlaves = directorio }));

    [Fact]
    public void Directorio_vacio_genera_una_llave_sola_al_arrancar()
    {
        string dir = NuevoDirectorio();
        var almacen = Construir(dir);

        Assert.Single(almacen.LlavesVigentes);
        Assert.Equal(almacen.LlavesVigentes[0].Kid, almacen.LlaveDeFirmaActiva.Kid);
        Assert.Single(Directory.GetFiles(dir, "*.pem"));
    }

    [Fact]
    public void Recargar_el_mismo_directorio_reproduce_las_mismas_llaves_sin_regenerar()
    {
        string dir = NuevoDirectorio();
        var primero = Construir(dir);
        string kidOriginal = primero.LlaveDeFirmaActiva.Kid;

        var segundo = Construir(dir);

        Assert.Single(Directory.GetFiles(dir, "*.pem"));
        Assert.Equal(kidOriginal, segundo.LlaveDeFirmaActiva.Kid);
    }

    [Fact]
    public void Con_varias_llaves_la_activa_es_siempre_la_mas_reciente()
    {
        string dir = NuevoDirectorio();
        Directory.CreateDirectory(dir);

        // Simula una rotación: dos llaves con timestamps de nombre distintos
        // (mismo formato que RsaKeyStore genera) — la más nueva por nombre
        // debe ganar, sin depender de metadatos de filesystem.
        using var rsaVieja = System.Security.Cryptography.RSA.Create(2048);
        using var rsaNueva = System.Security.Cryptography.RSA.Create(2048);
        File.WriteAllText(Path.Combine(dir, "20200101000000_vieja123.pem"), rsaVieja.ExportPkcs8PrivateKeyPem());
        File.WriteAllText(Path.Combine(dir, "20990101000000_nueva456.pem"), rsaNueva.ExportPkcs8PrivateKeyPem());

        var almacen = Construir(dir);

        Assert.Equal("nueva456", almacen.LlaveDeFirmaActiva.Kid);
        Assert.Equal(2, almacen.LlavesVigentes.Count);
    }

    [Fact]
    public void ComoSecurityKey_lleva_el_kid_como_KeyId()
    {
        string dir = NuevoDirectorio();
        var almacen = Construir(dir);

        var securityKey = almacen.LlaveDeFirmaActiva.ComoSecurityKey();

        Assert.Equal(almacen.LlaveDeFirmaActiva.Kid, securityKey.KeyId);
    }
}
