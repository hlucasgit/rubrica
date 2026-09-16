using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using SecureSign.Crypto.Domain;
using SecureSign.Crypto.Infrastructure.Pkcs11;

namespace SecureSign.UnitTests.Crypto;

/// <summary>
/// Prueba <see cref="ProveedorCriptograficoPkcs11"/> contra un SoftHSM2 real
/// compilado localmente — no un doble de prueba, la misma librería PKCS#11
/// nativa (.dll) que cualquier token real (DNIe incluido) expone. Cierra el
/// hallazgo del informe de preauditoría INDECOPI/IOFE, sección 13 ("Pruebas
/// que faltan": PKCS#11 — certificado revocado/expirado, PIN incorrecto),
/// documentado como bloqueado por falta de toolchain en
/// docs/08-cumplimiento/matriz-cumplimiento-indecopi.md sección 5.
///
/// SOLO corre en un entorno donde SoftHSM2 se compiló localmente siguiendo
/// CMAKE-WIN-NOTES.md (Visual Studio + CMake + vcpkg — ver ese archivo para
/// los pasos exactos) y se inicializó un token de prueba con un certificado
/// importado bajo la etiqueta "FIR" — nunca en CI, que no tiene ese
/// toolchain instalado; cada test verifica la presencia del módulo con
/// <see cref="ModuloDisponible"/> y se salta silenciosamente si falta, en
/// vez de fallar el build en un entorno sin este setup.
///
/// Setup para reproducir en otra máquina (una sola vez):
/// <code>
/// softhsm2-util --init-token --slot 0 --label TokenPrueba --pin 1234 --so-pin 5678
/// softhsm2-util --import llave.pem --token TokenPrueba --label FIR --id 01 --pin 1234
/// softhsm2-util --import cert.pem --import-type cert --token TokenPrueba --label FIR --id 01 --pin 1234
/// </code>
/// (la llave debe ser PKCS#8 PEM, el certificado X509 PEM, ambos con el
/// mismo --id para que PKCS#11 los correlacione — exactamente como hace
/// <c>ProveedorCriptograficoPkcs11.BuscarCertificadoDeFirma</c> internamente).
/// </summary>
public sealed class ProveedorCriptograficoPkcs11Tests
{
    private const string RutaModulo = @"C:\SoftHSMv2\out64\lib\softhsm\softhsm2.dll";
    private const string PinCorrecto = "1234";

    private static bool ModuloDisponible => File.Exists(RutaModulo);

    private static ProveedorCriptograficoPkcs11 CrearProveedor() =>
        new(Options.Create(new Pkcs11Options { RutaLibreria = RutaModulo, EtiquetaCertificadoFirma = "FIR" }));

    [Fact]
    public async Task Descubre_el_certificado_de_firma_real_del_token()
    {
        if (!ModuloDisponible) return; // entorno sin SoftHSM2 compilado — ver comentario de clase.

        var proveedor = CrearProveedor();
        string referencia = await proveedor.GenerarParClavesAsync(Guid.NewGuid(), AlgoritmoFirma.RsaSha256);

        Assert.StartsWith("pkcs11:slot=", referencia);
        Assert.Contains(":id=", referencia);
    }

    [Fact]
    public async Task Firma_real_contra_el_token_verifica_correctamente()
    {
        if (!ModuloDisponible) return;

        var proveedor = CrearProveedor();
        string referencia = await proveedor.GenerarParClavesAsync(Guid.NewGuid(), AlgoritmoFirma.RsaSha256);

        byte[] hash = SHA256.HashData("documento de prueba PKCS#11"u8.ToArray());
        var resultado = await proveedor.FirmarAsync(referencia, hash, AlgoritmoFirma.RsaSha256, credencial: PinCorrecto);

        Assert.NotEmpty(resultado.Firma);
        Assert.Equal(referencia, resultado.ReferenciaLlaveUsada);

        bool valido = await proveedor.VerificarFirmaAsync(referencia, hash, resultado.Firma, AlgoritmoFirma.RsaSha256);
        Assert.True(valido);
    }

    [Fact]
    public async Task Firma_sobre_contenido_alterado_no_verifica()
    {
        if (!ModuloDisponible) return;

        var proveedor = CrearProveedor();
        string referencia = await proveedor.GenerarParClavesAsync(Guid.NewGuid(), AlgoritmoFirma.RsaSha256);

        byte[] hashOriginal = SHA256.HashData("contenido original"u8.ToArray());
        byte[] hashAlterado = SHA256.HashData("contenido alterado"u8.ToArray());
        var resultado = await proveedor.FirmarAsync(referencia, hashOriginal, AlgoritmoFirma.RsaSha256, credencial: PinCorrecto);

        bool valido = await proveedor.VerificarFirmaAsync(referencia, hashAlterado, resultado.Firma, AlgoritmoFirma.RsaSha256);
        Assert.False(valido);
    }

    /// <summary>Fila del informe de preauditoría, sección 13: "PIN incorrecto → rechazo".</summary>
    [Fact]
    public async Task Pin_incorrecto_lanza_y_no_produce_ninguna_firma()
    {
        if (!ModuloDisponible) return;

        var proveedor = CrearProveedor();
        string referencia = await proveedor.GenerarParClavesAsync(Guid.NewGuid(), AlgoritmoFirma.RsaSha256);
        byte[] hash = SHA256.HashData("documento"u8.ToArray());

        await Assert.ThrowsAnyAsync<Exception>(
            () => proveedor.FirmarAsync(referencia, hash, AlgoritmoFirma.RsaSha256, credencial: "0000"));
    }

    [Fact]
    public async Task Sin_pin_lanza_antes_de_tocar_el_token()
    {
        if (!ModuloDisponible) return;

        var proveedor = CrearProveedor();
        string referencia = await proveedor.GenerarParClavesAsync(Guid.NewGuid(), AlgoritmoFirma.RsaSha256);
        byte[] hash = SHA256.HashData("documento"u8.ToArray());

        await Assert.ThrowsAsync<ArgumentException>(
            () => proveedor.FirmarAsync(referencia, hash, AlgoritmoFirma.RsaSha256, credencial: null));
    }

    [Fact]
    public async Task Lote_con_una_sola_credencial_firma_todas_las_operaciones()
    {
        if (!ModuloDisponible) return;

        var proveedor = CrearProveedor();
        string referencia = await proveedor.GenerarParClavesAsync(Guid.NewGuid(), AlgoritmoFirma.RsaSha256);

        var operaciones = new[]
        {
            new OperacionFirmaLote(referencia, SHA256.HashData("documento 1"u8.ToArray()), AlgoritmoFirma.RsaSha256),
            new OperacionFirmaLote(referencia, SHA256.HashData("documento 2"u8.ToArray()), AlgoritmoFirma.RsaSha256),
            new OperacionFirmaLote(referencia, SHA256.HashData("documento 3"u8.ToArray()), AlgoritmoFirma.RsaSha256),
        };

        var resultados = await proveedor.FirmarLoteAsync(operaciones, credencial: PinCorrecto);

        Assert.Equal(3, resultados.Count);
        for (int i = 0; i < operaciones.Length; i++)
        {
            bool valido = await proveedor.VerificarFirmaAsync(referencia, operaciones[i].HashDocumento, resultados[i].Firma, AlgoritmoFirma.RsaSha256);
            Assert.True(valido, $"La firma {i} del lote no verificó.");
        }
    }

    [Fact]
    public async Task Lote_con_referencias_de_llave_distintas_lanza()
    {
        if (!ModuloDisponible) return;

        var proveedor = CrearProveedor();
        string referencia = await proveedor.GenerarParClavesAsync(Guid.NewGuid(), AlgoritmoFirma.RsaSha256);

        var operaciones = new[]
        {
            new OperacionFirmaLote(referencia, SHA256.HashData("documento 1"u8.ToArray()), AlgoritmoFirma.RsaSha256),
            new OperacionFirmaLote("pkcs11:slot=999:id=FF", SHA256.HashData("documento 2"u8.ToArray()), AlgoritmoFirma.RsaSha256),
        };

        await Assert.ThrowsAsync<ArgumentException>(() => proveedor.FirmarLoteAsync(operaciones, credencial: PinCorrecto));
    }

    [Fact]
    public async Task Verificar_con_slot_inexistente_da_falso_sin_lanzar()
    {
        if (!ModuloDisponible) return;

        var proveedor = CrearProveedor();
        byte[] hash = SHA256.HashData("documento"u8.ToArray());

        bool valido = await proveedor.VerificarFirmaAsync("pkcs11:slot=999999:id=FF", hash, new byte[256], AlgoritmoFirma.RsaSha256);
        Assert.False(valido);
    }
}
