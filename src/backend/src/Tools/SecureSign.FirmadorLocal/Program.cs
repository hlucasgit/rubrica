using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Win32;
using Net.Pkcs11Interop.Common;
using Net.Pkcs11Interop.HighLevelAPI;

namespace SecureSign.FirmadorLocal;

/// <summary>
/// Firmador Local de SecureSign Perú — el equivalente a "FirmadorClienteWeb"
/// de Firma Perú, pero como un único ejecutable .NET autocontenido (sin
/// Java, sin ClickOnce, sin plugins de navegador) invocado vía el protocolo
/// de URL <c>securesign://</c>.
///
/// Diseño clave: el PIN de la tarjeta NUNCA viaja por la red — ni siquiera
/// hacia el propio Servicio Criptográfico de SecureSign. Este proceso:
/// 1. Descarga el documento original directamente desde el Gateway.
/// 2. Calcula su hash SHA-256 EN ESTA MÁQUINA (nunca confía en un hash que
///    le pasen — así nunca firma "a ciegas" un hash de origen desconocido).
/// 3. Firma ese hash con la tarjeta/token PKCS#11 conectado localmente,
///    mostrando una ventana (ver <see cref="VentanaFirma"/>) para elegir el
///    certificado e ingresar el PIN — no un prompt de consola.
/// 4. Envía de vuelta SOLO la firma resultante y el certificado público —
///    ver SecureSign.Signature.Application.FirmarLocal.VerificadorFirmaExterna,
///    que verifica esa firma con la llave PÚBLICA del certificado (una
///    operación que no requiere PIN ni PKCS#11).
///
/// Ver docs/RUNBOOK.md sección 12 para el flujo de instalación e integración
/// completo, y limitaciones deliberadas (solo Windows, un token a la vez,
/// selección de certificado por índice en consola).
/// </summary>
internal static class Program
{
    private static readonly byte[] DigestInfoPrefijoSha256 =
        Convert.FromHexString("3031300D060960864801650304020105000420");

    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        Console.Title = "SecureSign Perú — Firmador Local";
        Console.WriteLine("=== SecureSign Perú — Firmador Local ===");
        Console.WriteLine();

        if (args.Length == 0 || args[0] == "--registrar")
        {
            RegistrarProtocolo();
            return 0;
        }

        if (args[0] == "--desinstalar")
        {
            DesinstalarProtocolo();
            return 0;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        try
        {
            await ProcesarInvocacionAsync(args[0]);
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine($"ERROR: {ex.Message}");
            MessageBox.Show(ex.Message, "SecureSign Perú — Error al firmar", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    // ---------- registro del protocolo securesign:// ----------

    private static void RegistrarProtocolo()
    {
        var rutaExe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName;

        using var claveProtocolo = Registry.CurrentUser.CreateSubKey(@"Software\Classes\securesign");
        claveProtocolo.SetValue(string.Empty, "URL:Protocolo de firma SecureSign Perú");
        claveProtocolo.SetValue("URL Protocol", string.Empty);

        using var claveIcono = claveProtocolo.CreateSubKey("DefaultIcon");
        claveIcono.SetValue(string.Empty, $"\"{rutaExe}\",0");

        using var claveComando = claveProtocolo.CreateSubKey(@"shell\open\command");
        claveComando.SetValue(string.Empty, $"\"{rutaExe}\" \"%1\"");

        Console.WriteLine("Firmador Local registrado correctamente.");
        Console.WriteLine($"  Protocolo: securesign://");
        Console.WriteLine($"  Ejecutable: {rutaExe}");
        Console.WriteLine();
        Console.WriteLine("Ya puedes cerrar esta ventana. Si tenías el navegador abierto, reinícialo");
        Console.WriteLine("para que reconozca el nuevo protocolo.");
    }

    private static void DesinstalarProtocolo()
    {
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\securesign", throwOnMissingSubKey: false);
        Console.WriteLine("Protocolo securesign:// eliminado del registro de este usuario.");
    }

    // ---------- flujo de firma ----------

    private sealed record ParametrosFirma(string GatewayUrl, string SolicitudId, string FlujoId, string AccessToken);

    private static async Task ProcesarInvocacionAsync(string uriCompleta)
    {
        var parametros = ParsearParametros(uriCompleta);

        using var http = new HttpClient { BaseAddress = new Uri(parametros.GatewayUrl) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", parametros.AccessToken);

        Console.WriteLine($"Solicitud de firma: {parametros.SolicitudId}");
        Console.WriteLine("Consultando la solicitud...");
        var estado = await http.GetFromJsonAsync<JsonElement>($"/api/firmas/{parametros.SolicitudId}/estado");
        var documentoId = estado.GetProperty("documentoId").GetGuid();

        Console.WriteLine("Descargando el documento a firmar...");
        var respuestaDocumento = await http.GetAsync($"/api/documentos/{documentoId}/contenido");
        respuestaDocumento.EnsureSuccessStatusCode();
        var contenido = await respuestaDocumento.Content.ReadAsByteArrayAsync();
        var nombreArchivo = respuestaDocumento.Content.Headers.ContentDisposition?.FileNameStar
            ?? respuestaDocumento.Content.Headers.ContentDisposition?.FileName
            ?? documentoId.ToString();
        var hash = SHA256.HashData(contenido);
        Console.WriteLine($"  {contenido.Length} bytes — SHA-256: {Convert.ToHexString(hash)}");

        var candidatos = EnumerarCertificados();

        Console.WriteLine("Mostrando ventana de firma — elige el certificado e ingresa tu PIN ahí...");
        using var ventana = new VentanaFirma(nombreArchivo, contenido.Length, Convert.ToHexString(hash), candidatos.Select(c => c.Descripcion).ToList());
        if (ventana.ShowDialog() != DialogResult.OK || ventana.IndiceCertificadoElegido < 0)
        {
            Console.WriteLine("Operación cancelada por el usuario.");
            return;
        }

        var elegido = candidatos[ventana.IndiceCertificadoElegido];
        var pin = ventana.Pin;

        Console.WriteLine("Firmando en esta máquina (el PIN no sale de aquí)...");
        var firma = FirmarConTarjeta(elegido.SlotId, elegido.CkaId, hash, pin);
        pin = string.Empty; // no persistir el PIN en memoria más de lo necesario

        Console.WriteLine("Enviando el resultado a SecureSign...");
        var cuerpo = new
        {
            firmaBase64 = Convert.ToBase64String(firma),
            certificadoBase64 = Convert.ToBase64String(elegido.CertDer),
            algoritmo = "RsaSha256",
        };
        var respuesta = await http.PostAsJsonAsync(
            $"/api/firmas/{parametros.SolicitudId}/flujos/{parametros.FlujoId}/completar-firma-local", cuerpo);
        var textoRespuesta = await respuesta.Content.ReadAsStringAsync();

        if (!respuesta.IsSuccessStatusCode)
            throw new InvalidOperationException($"SecureSign rechazó el resultado (HTTP {(int)respuesta.StatusCode}): {textoRespuesta}");

        Console.WriteLine();
        Console.WriteLine("ÉXITO — documento firmado correctamente.");
        Console.WriteLine(textoRespuesta);
        MessageBox.Show($"El documento \"{nombreArchivo}\" se firmó correctamente.", "SecureSign Perú", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static ParametrosFirma ParsearParametros(string uriCompleta)
    {
        var uri = new Uri(uriCompleta);
        var query = uri.Query.TrimStart('?');
        var valorParam = query
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .FirstOrDefault(kv => kv[0] == "param")
            ?.ElementAtOrDefault(1)
            ?? throw new ArgumentException("La URI de invocación no trae el parámetro 'param'.");

        var json = Convert.FromBase64String(Uri.UnescapeDataString(valorParam));
        var doc = JsonSerializer.Deserialize<JsonElement>(json);

        return new ParametrosFirma(
            doc.GetProperty("gatewayUrl").GetString()!,
            doc.GetProperty("solicitudId").GetString()!,
            doc.GetProperty("flujoId").GetString()!,
            doc.GetProperty("accessToken").GetString()!);
    }

    // ---------- PKCS#11 (misma lógica probada en ProveedorCriptograficoPkcs11 / DnieProbe) ----------

    private static string RutaLibreriaPkcs11 =>
        Environment.GetEnvironmentVariable("SECURESIGN_PKCS11_LIB")
        ?? @"C:\Program Files\IDEMIA\IDPlugClassic\DLLs\idplug-pkcs11.dll";

    private sealed record CandidatoCertificado(ulong SlotId, byte[] CkaId, string Descripcion, byte[] CertDer);

    /// <summary>
    /// Enumera los certificados de firma (no-CA) de los tokens conectados,
    /// sin ninguna interacción — la elección la hace el usuario en
    /// <see cref="VentanaFirma"/>, no aquí.
    /// </summary>
    private static List<CandidatoCertificado> EnumerarCertificados()
    {
        var factory = new Pkcs11InteropFactories();
        using IPkcs11Library pkcs11 = factory.Pkcs11LibraryFactory.LoadPkcs11Library(factory, RutaLibreriaPkcs11, AppType.SingleThreaded);

        var candidatos = new List<CandidatoCertificado>();
        foreach (var slot in pkcs11.GetSlotList(SlotsType.WithTokenPresent))
        {
            using ISession session = slot.OpenSession(SessionType.ReadOnly);
            var plantilla = new List<IObjectAttribute> { factory.ObjectAttributeFactory.Create(CKA.CKA_CLASS, CKO.CKO_CERTIFICATE) };

            foreach (var cert in session.FindAllObjects(plantilla))
            {
                var atributos = session.GetAttributeValue(cert, new List<CKA> { CKA.CKA_LABEL, CKA.CKA_VALUE, CKA.CKA_ID });
                var etiqueta = atributos[0].GetValueAsString();
                var valor = atributos[1].GetValueAsByteArray();
                var ckaId = atributos[2].GetValueAsByteArray();

                X509Certificate2 x509;
                try { x509 = new X509Certificate2(valor); }
                catch { continue; }

                var esCA = x509.Extensions.OfType<X509BasicConstraintsExtension>().FirstOrDefault()?.CertificateAuthority ?? false;
                if (esCA) continue;

                candidatos.Add(new CandidatoCertificado(slot.SlotId, ckaId, $"{etiqueta} — {x509.Subject}", valor));
            }
        }

        if (candidatos.Count == 0)
            throw new InvalidOperationException(
                "No se encontró ningún certificado de firma en los tokens conectados. Verifica que la tarjeta esté insertada.");

        return candidatos;
    }

    private static byte[] FirmarConTarjeta(ulong slotId, byte[] ckaId, byte[] hashDocumento, string pin)
    {
        var factory = new Pkcs11InteropFactories();
        using IPkcs11Library pkcs11 = factory.Pkcs11LibraryFactory.LoadPkcs11Library(factory, RutaLibreriaPkcs11, AppType.SingleThreaded);

        var slot = pkcs11.GetSlotList(SlotsType.WithTokenPresent).FirstOrDefault(s => s.SlotId == slotId)
            ?? throw new InvalidOperationException("El slot de la tarjeta ya no está disponible (¿se retiró?).");

        using ISession session = slot.OpenSession(SessionType.ReadOnly);
        session.Login(CKU.CKU_USER, pin);
        try
        {
            var plantillaLlave = new List<IObjectAttribute>
            {
                factory.ObjectAttributeFactory.Create(CKA.CKA_CLASS, CKO.CKO_PRIVATE_KEY),
                factory.ObjectAttributeFactory.Create(CKA.CKA_SIGN, true),
                factory.ObjectAttributeFactory.Create(CKA.CKA_ID, ckaId),
            };
            var llaves = session.FindAllObjects(plantillaLlave);
            if (llaves.Count == 0)
                throw new InvalidOperationException("No se encontró la llave privada correspondiente (¿PIN incorrecto?).");

            var digestInfo = DigestInfoPrefijoSha256.Concat(hashDocumento).ToArray();
            var mecanismo = factory.MechanismFactory.Create(CKM.CKM_RSA_PKCS);
            return session.Sign(mecanismo, llaves[0], digestInfo);
        }
        finally
        {
            session.Logout();
        }
    }
}
