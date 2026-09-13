using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Options;
using Net.Pkcs11Interop.Common;
using Net.Pkcs11Interop.HighLevelAPI;
using SecureSign.Crypto.Domain;

namespace SecureSign.Crypto.Infrastructure.Pkcs11;

/// <summary>
/// Adaptador PKCS#11 real. Probado end-to-end contra un DNIe peruano físico
/// (middleware IDEMIA idplug-pkcs11.dll) en esta sesión de desarrollo: la
/// firma generada se verificó criptográficamente contra el certificado de
/// firma emitido por "ECEP-RENIEC CA Class 2".
///
/// LIMITACIONES DELIBERADAS de este adaptador (léase antes de asumir que
/// sirve para cualquier despliegue):
///
/// 1. Asume UN solo token conectado a ESTA máquina, correspondiente a UNA
///    sola persona (el firmante que físicamente tiene el equipo enfrente).
///    <see cref="GenerarParClavesAsync"/> ignora <c>usuarioId</c> — no hay
///    forma de que un servicio centralizado con un solo token conectado
///    sirva a múltiples firmantes simultáneamente. Esto refleja una
///    realidad física, no una limitación arbitraria: la llave privada vive
///    en LA tarjeta que está en EL lector de ESA máquina.
///
/// 2. Por lo anterior, este proveedor solo tiene sentido cuando
///    SecureSign.Crypto.Api corre NATIVO en la máquina Windows del propio
///    firmante (no en un contenedor Linux — un contenedor Linux no puede
///    cargar una DLL nativa de Windows). Ver src/backend/RUNBOOK.md.
///
/// 3. Selecciona el certificado personal (no-CA) cuya etiqueta contiene
///    "FIR" (configurable, ver Pkcs11Options) — el DNIe peruano expone dos
///    aplicaciones PKI en slots separados, una de autenticación ("AUT") y
///    otra de firma ("FIR"), cada una con su propio PIN. Firmar documentos
///    con validez legal requiere la de FIRMA, nunca la de autenticación.
///
/// 4. Los certificados en la tarjeta se distinguen de la cadena de CA
///    (que también vive ahí) mediante la extensión X.509 BasicConstraints
///    — un certificado con CA=true nunca es candidato, tenga o no llave
///    privada asociada.
/// </summary>
public sealed class ProveedorCriptograficoPkcs11(IOptions<Pkcs11Options> opciones) : IProveedorCriptografico
{
    // Prefijo ASN.1 DER estándar (RFC 3447/8017, PKCS#1 v1.5) para un
    // DigestInfo de SHA-256. CKM_RSA_PKCS firma el buffer tal cual (padding
    // PKCS#1 + cifrado RSA) sin volver a hashear — por eso hay que envolver
    // el hash ya calculado en esta estructura antes de pasarlo a la tarjeta.
    private static readonly byte[] DigestInfoPrefijoSha256 =
        Convert.FromHexString("3031300D060960864801650304020105000420");

    private readonly Pkcs11Options _opciones = opciones.Value;

    public Task<string> GenerarParClavesAsync(Guid usuarioId, AlgoritmoFirma algoritmo, CancellationToken ct = default)
    {
        if (algoritmo != AlgoritmoFirma.RsaSha256)
            throw new NotSupportedException("El DNIe peruano (y la mayoría de tokens PKCS#11 de identidad) usan RSA — solicita AlgoritmoFirma.RsaSha256.");

        var factory = new Pkcs11InteropFactories();
        using IPkcs11Library pkcs11 = factory.Pkcs11LibraryFactory.LoadPkcs11Library(factory, _opciones.RutaLibreria, AppType.SingleThreaded);

        var candidato = BuscarCertificadoDeFirma(factory, pkcs11)
            ?? throw new InvalidOperationException(
                "No se encontró un certificado de firma (no-CA) en ningún token conectado. " +
                "Verifica que la tarjeta esté insertada y que el lector funcione.");

        // La referencia codifica todo lo necesario para volver a localizar
        // exactamente esta llave más adelante, sin tener que buscar de nuevo
        // por etiqueta (que podría ser ambigua entre tokens).
        var referencia = $"pkcs11:slot={candidato.SlotId}:id={Convert.ToHexString(candidato.CkaId)}";
        return Task.FromResult(referencia);
    }

    public Task<ResultadoFirmaCriptografica> FirmarAsync(
        string referenciaLlave, byte[] hashDocumento, AlgoritmoFirma algoritmo, string? credencial = null, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(credencial))
            throw new ArgumentException("Este proveedor requiere el PIN de la tarjeta en cada operación de firma.", nameof(credencial));

        var (slotId, ckaId) = ParsearReferencia(referenciaLlave);

        var factory = new Pkcs11InteropFactories();
        using IPkcs11Library pkcs11 = factory.Pkcs11LibraryFactory.LoadPkcs11Library(factory, _opciones.RutaLibreria, AppType.SingleThreaded);

        var slot = pkcs11.GetSlotList(SlotsType.WithTokenPresent).FirstOrDefault(s => s.SlotId == slotId)
            ?? throw new InvalidOperationException($"El slot {slotId} de la referencia ya no está disponible (¿se retiró la tarjeta?).");

        using ISession session = slot.OpenSession(SessionType.ReadOnly);
        session.Login(CKU.CKU_USER, credencial);
        try
        {
            var plantillaLlave = new List<IObjectAttribute>
            {
                factory.ObjectAttributeFactory.Create(CKA.CKA_CLASS, CKO.CKO_PRIVATE_KEY),
                factory.ObjectAttributeFactory.Create(CKA.CKA_SIGN, true),
                factory.ObjectAttributeFactory.Create(CKA.CKA_ID, ckaId)
            };
            var llaves = session.FindAllObjects(plantillaLlave);
            if (llaves.Count == 0)
                throw new InvalidOperationException("No se encontró la llave privada correspondiente a esta referencia (¿PIN incorrecto o tarjeta distinta?).");

            // El sistema ya nos entrega el hash SHA-256 del documento (no el
            // documento crudo) — CKM_RSA_PKCS firma el buffer sin re-hashear,
            // así que hay que envolverlo en la estructura DigestInfo estándar
            // antes de pasarlo a la tarjeta (ver comentario del prefijo arriba).
            var digestInfo = DigestInfoPrefijoSha256.Concat(hashDocumento).ToArray();

            var mecanismo = factory.MechanismFactory.Create(CKM.CKM_RSA_PKCS);
            byte[] firma = session.Sign(mecanismo, llaves[0], digestInfo);

            return Task.FromResult(new ResultadoFirmaCriptografica(firma, algoritmo, referenciaLlave));
        }
        finally
        {
            session.Logout();
        }
    }

    public Task<IReadOnlyList<ResultadoFirmaCriptografica>> FirmarLoteAsync(
        IReadOnlyList<OperacionFirmaLote> operaciones, string? credencial = null, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(credencial))
            throw new ArgumentException("Este proveedor requiere el PIN de la tarjeta en cada operación de firma.", nameof(credencial));
        if (operaciones.Count == 0)
            return Task.FromResult<IReadOnlyList<ResultadoFirmaCriptografica>>(Array.Empty<ResultadoFirmaCriptografica>());

        // El punto entero de firmar "en lote" es evitar que el firmante
        // ingrese su PIN una vez por documento — eso exige que todas las
        // operaciones del lote usen la MISMA referencia de llave (mismo
        // firmante, misma tarjeta física en el mismo lector).
        var referencias = operaciones.Select(o => o.ReferenciaLlave).Distinct().ToList();
        if (referencias.Count != 1)
            throw new ArgumentException("Todas las operaciones de un lote deben usar la misma referencia de llave (mismo firmante).", nameof(operaciones));

        var (slotId, ckaId) = ParsearReferencia(referencias[0]);

        var factory = new Pkcs11InteropFactories();
        using IPkcs11Library pkcs11 = factory.Pkcs11LibraryFactory.LoadPkcs11Library(factory, _opciones.RutaLibreria, AppType.SingleThreaded);

        var slot = pkcs11.GetSlotList(SlotsType.WithTokenPresent).FirstOrDefault(s => s.SlotId == slotId)
            ?? throw new InvalidOperationException($"El slot {slotId} de la referencia ya no está disponible (¿se retiró la tarjeta?).");

        using ISession session = slot.OpenSession(SessionType.ReadOnly);
        session.Login(CKU.CKU_USER, credencial);
        try
        {
            var plantillaLlave = new List<IObjectAttribute>
            {
                factory.ObjectAttributeFactory.Create(CKA.CKA_CLASS, CKO.CKO_PRIVATE_KEY),
                factory.ObjectAttributeFactory.Create(CKA.CKA_SIGN, true),
                factory.ObjectAttributeFactory.Create(CKA.CKA_ID, ckaId)
            };
            var llaves = session.FindAllObjects(plantillaLlave);
            if (llaves.Count == 0)
                throw new InvalidOperationException("No se encontró la llave privada correspondiente a esta referencia (¿PIN incorrecto o tarjeta distinta?).");

            var resultados = new List<ResultadoFirmaCriptografica>(operaciones.Count);
            foreach (var operacion in operaciones)
            {
                var digestInfo = DigestInfoPrefijoSha256.Concat(operacion.HashDocumento).ToArray();
                var mecanismo = factory.MechanismFactory.Create(CKM.CKM_RSA_PKCS);
                byte[] firma = session.Sign(mecanismo, llaves[0], digestInfo);
                resultados.Add(new ResultadoFirmaCriptografica(firma, operacion.Algoritmo, operacion.ReferenciaLlave));
            }

            return Task.FromResult<IReadOnlyList<ResultadoFirmaCriptografica>>(resultados);
        }
        finally
        {
            session.Logout();
        }
    }

    public Task<bool> VerificarFirmaAsync(string referenciaLlave, byte[] hashDocumento, byte[] firma, AlgoritmoFirma algoritmo, CancellationToken ct = default)
    {
        // Verificar es una operación pública — no requiere PIN ni volver a
        // tocar la tarjeta: basta la llave pública del certificado.
        var (slotId, ckaId) = ParsearReferencia(referenciaLlave);

        var factory = new Pkcs11InteropFactories();
        using IPkcs11Library pkcs11 = factory.Pkcs11LibraryFactory.LoadPkcs11Library(factory, _opciones.RutaLibreria, AppType.SingleThreaded);

        var slot = pkcs11.GetSlotList(SlotsType.WithTokenPresent).FirstOrDefault(s => s.SlotId == slotId);
        if (slot is null) return Task.FromResult(false);

        using ISession session = slot.OpenSession(SessionType.ReadOnly);
        var plantillaCert = new List<IObjectAttribute>
        {
            factory.ObjectAttributeFactory.Create(CKA.CKA_CLASS, CKO.CKO_CERTIFICATE),
            factory.ObjectAttributeFactory.Create(CKA.CKA_ID, ckaId)
        };
        var certs = session.FindAllObjects(plantillaCert);
        if (certs.Count == 0) return Task.FromResult(false);

        var valorCert = session.GetAttributeValue(certs[0], new List<CKA> { CKA.CKA_VALUE })[0].GetValueAsByteArray();
        using var x509 = new X509Certificate2(valorCert);
        using var rsa = x509.GetRSAPublicKey();
        if (rsa is null) return Task.FromResult(false);

        var valido = rsa.VerifyHash(hashDocumento, firma, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return Task.FromResult(valido);
    }

    private (ulong SlotId, byte[] CkaId) ParsearReferencia(string referencia)
    {
        // Formato: "pkcs11:slot={id}:id={hex}"
        var partes = referencia.Split(':');
        if (partes.Length != 3 || partes[0] != "pkcs11")
            throw new ArgumentException($"Referencia de llave con formato inesperado: {referencia}", nameof(referencia));

        var slotId = ulong.Parse(partes[1]["slot=".Length..]);
        var ckaId = Convert.FromHexString(partes[2]["id=".Length..]);
        return (slotId, ckaId);
    }

    private (ulong SlotId, byte[] CkaId)? BuscarCertificadoDeFirma(Pkcs11InteropFactories factory, IPkcs11Library pkcs11)
    {
        foreach (var slot in pkcs11.GetSlotList(SlotsType.WithTokenPresent))
        {
            using ISession session = slot.OpenSession(SessionType.ReadOnly);
            var plantillaCert = new List<IObjectAttribute>
            {
                factory.ObjectAttributeFactory.Create(CKA.CKA_CLASS, CKO.CKO_CERTIFICATE)
            };

            foreach (var cert in session.FindAllObjects(plantillaCert))
            {
                var attrs = session.GetAttributeValue(cert, new List<CKA> { CKA.CKA_LABEL, CKA.CKA_VALUE, CKA.CKA_ID });
                var label = attrs[0].GetValueAsString();
                var value = attrs[1].GetValueAsByteArray();
                var ckaId = attrs[2].GetValueAsByteArray();

                X509Certificate2? x509;
                try { x509 = new X509Certificate2(value); } catch { continue; }

                var esCA = x509.Extensions.OfType<X509BasicConstraintsExtension>().FirstOrDefault()?.CertificateAuthority ?? false;
                if (esCA) continue;

                if (label.Contains(_opciones.EtiquetaCertificadoFirma, StringComparison.OrdinalIgnoreCase))
                    return (slot.SlotId, ckaId);
            }
        }

        return null;
    }
}
