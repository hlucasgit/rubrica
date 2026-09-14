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
using SecureSign.Pades;

namespace SecureSign.FirmadorLocal;

/// <summary>
/// Firmador Local de SecureSign Perú — el equivalente al "FirmadorClienteWeb"
/// de Firma Perú, pero como un único ejecutable .NET autocontenido (sin
/// Java, sin ClickOnce, sin plugins de navegador).
///
/// MODO PRINCIPAL — servicio local: al ejecutarse sin argumentos, queda
/// corriendo en segundo plano (ícono en la bandeja) escuchando peticiones
/// HTTP en <c>http://127.0.0.1:{puerto}</c> (por defecto <see cref="PuertoPorDefecto"/>,
/// configurable con <c>--puerto &lt;n&gt;</c> o <c>SECURESIGN_FIRMADOR_PUERTO</c>
/// si ese puerto ya está en uso — ver <see cref="ResolverPuerto"/>). El
/// navegador simplemente hace un <c>fetch()</c> normal a ese puerto — igual
/// que hace la propia Plataforma FIRMA PERÚ con su
/// <c>startSignature(port, param)</c> — sin depender en absoluto de que
/// Windows resuelva ningún protocolo de URL personalizado (ver
/// ServicioLocal.cs y la nota de troubleshooting en RUNBOOK.md sección 12,
/// donde esa resolución resultó ser poco confiable).
///
/// MODO HEREDADO — invocación por <c>securesign://</c>: se conserva como
/// alternativa/respaldo (ver RUNBOOK.md), útil por ejemplo con
/// <c>rundll32.exe url.dll,FileProtocolHandler</c>.
///
/// Diseño clave, igual en ambos modos: el PIN de la tarjeta NUNCA viaja por
/// la red — ni siquiera hacia el propio Servicio Criptográfico de
/// SecureSign. Este proceso:
/// 1. Descarga el documento original directamente desde el Gateway.
/// 2. Calcula su hash SHA-256 EN ESTA MÁQUINA (nunca confía en un hash que
///    le pasen — así nunca firma "a ciegas" un hash de origen desconocido).
/// 3. Firma ese hash con la tarjeta/token PKCS#11 conectado localmente,
///    mostrando una ventana (ver <see cref="VentanaFirma"/>) para elegir el
///    certificado e ingresar el PIN.
/// 4. Envía de vuelta SOLO la firma resultante y el certificado público —
///    ver SecureSign.Signature.Application.FirmarLocal.VerificadorFirmaExterna,
///    que verifica esa firma con la llave PÚBLICA del certificado (una
///    operación que no requiere PIN ni PKCS#11).
/// </summary>
internal static class Program
{
    /// <summary>
    /// Puerto por defecto — configurable si ya está en uso por otra
    /// aplicación, vía argumento <c>--puerto &lt;n&gt;</c> o la variable de
    /// entorno <c>SECURESIGN_FIRMADOR_PUERTO</c> (el argumento tiene
    /// prioridad). Ver <see cref="ResolverPuerto"/>.
    /// </summary>
    internal const int PuertoPorDefecto = 48596;

    private static readonly byte[] DigestInfoPrefijoSha256 =
        Convert.FromHexString("3031300D060960864801650304020105000420");

    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--registrar")
        {
            RegistrarProtocolo();
            return 0;
        }

        if (args.Length > 0 && args[0] == "--desinstalar")
        {
            DesinstalarProtocolo();
            return 0;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        if (args.Length > 0 && args[0].StartsWith("securesign://", StringComparison.OrdinalIgnoreCase))
        {
            Console.Title = "SecureSign Perú — Firmador Local";
            Console.WriteLine("=== SecureSign Perú — Firmador Local (modo heredado, securesign://) ===");
            Console.WriteLine();
            try
            {
                var parametros = ParsearParametrosDesdeUri(args[0]);
                var ticket = DecodificarTicket(parametros.Ticket);
                var resultado = await EjecutarFirmaAsync(parametros, ticket, hiloUi: null);
                Console.WriteLine(resultado.Mensaje);
                return resultado.Ok ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: {ex.Message}");
                MessageBox.Show(ex.Message, "SecureSign Perú — Error al firmar", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        int puerto;
        try
        {
            puerto = ResolverPuerto(args);
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(ex.Message, "SecureSign Perú — Firmador Local", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }

        // Modo principal: servicio local persistente (ver ServicioLocal.cs).
        using var servicio = new ServicioLocal(puerto);
        Application.Run(servicio);
        return 0;
    }

    /// <summary>
    /// Resuelve el puerto a usar: <c>--puerto &lt;n&gt;</c> gana sobre la
    /// variable de entorno <c>SECURESIGN_FIRMADOR_PUERTO</c>, que a su vez
    /// gana sobre <see cref="PuertoPorDefecto"/>. Se puede necesitar
    /// cambiarlo si el puerto por defecto ya está en uso por otra app.
    /// </summary>
    private static int ResolverPuerto(string[] args)
    {
        var indiceFlag = Array.IndexOf(args, "--puerto");
        if (indiceFlag >= 0)
        {
            if (indiceFlag + 1 >= args.Length || !ushort.TryParse(args[indiceFlag + 1], out var puertoArgumento) || puertoArgumento == 0)
                throw new ArgumentException("Uso: SecureSignFirmadorLocal.exe --puerto <número entre 1 y 65535>");
            return puertoArgumento;
        }

        var variableEntorno = Environment.GetEnvironmentVariable("SECURESIGN_FIRMADOR_PUERTO");
        if (!string.IsNullOrWhiteSpace(variableEntorno))
        {
            if (!ushort.TryParse(variableEntorno, out var puertoEntorno) || puertoEntorno == 0)
                throw new ArgumentException($"SECURESIGN_FIRMADOR_PUERTO tiene un valor inválido: \"{variableEntorno}\".");
            return puertoEntorno;
        }

        return PuertoPorDefecto;
    }

    // ---------- registro del protocolo securesign:// (modo heredado) ----------

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

    // ---------- flujo de firma (compartido entre el servicio local y el modo heredado) ----------

    /// <param name="Ticket">
    /// El ticket de firma de un solo uso (ver EmisorTicketFirmaLocal y
    /// RUNBOOK.md 12.13) — SUSTITUYE al accessToken reusable que este mismo
    /// campo llevaba antes: se usa tal cual como credencial Bearer para las
    /// 3 llamadas al backend (este proceso nunca verifica su firma — eso lo
    /// hace el backend, que es quien realmente la conoce; ver ClaimsTicket).
    /// </param>
    internal sealed record ParametrosFirma(string GatewayUrl, string Ticket);

    internal sealed record ResultadoFirma(bool Ok, string Mensaje, JsonElement? Datos);

    /// <summary>
    /// Los claims del ticket, leídos por simple decodificación Base64Url del
    /// payload del JWT — NUNCA se verifica la firma aquí (este proceso no
    /// conoce ni debe conocer la llave del backend). Esto es deliberado y
    /// seguro (ver comentario extenso en EmisorTicketFirmaLocal y RUNBOOK.md
    /// 12.13): un ticket alterado simplemente será rechazado por el backend
    /// cuando este proceso lo use como Bearer — los usos de estos claims
    /// aquí son solo comprobaciones locales de salida rápida (no repetir un
    /// ticket vencido, no aceptar una petición de un origen distinto al
    /// autorizado, no firmar un documento con un hash distinto al esperado)
    /// para fallar ANTES de pedirle el PIN al usuario, nunca como sustituto
    /// de la validación real.
    /// </summary>
    internal sealed record ClaimsTicket(string SolicitudId, string FlujoId, string? DocumentoHashEsperado, string? Origen, DateTimeOffset? ExpiraEn);

    internal static ParametrosFirma ParsearParametrosDesdeUri(string uriCompleta)
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
        return ParametrosDesdeJson(JsonSerializer.Deserialize<JsonElement>(json));
    }

    internal static ParametrosFirma ParsearParametrosDesdeCuerpoJson(string json) =>
        ParametrosDesdeJson(JsonSerializer.Deserialize<JsonElement>(json));

    private static ParametrosFirma ParametrosDesdeJson(JsonElement doc) => new(
        doc.GetProperty("gatewayUrl").GetString()!,
        doc.GetProperty("ticket").GetString()!);

    /// <summary>Decodifica (sin verificar firma — ver ClaimsTicket) el payload de un JWT.</summary>
    internal static ClaimsTicket DecodificarTicket(string jwt)
    {
        var partes = jwt.Split('.');
        if (partes.Length != 3)
            throw new ArgumentException("El ticket no tiene el formato JWT esperado (header.payload.signature).");

        var payload = JsonSerializer.Deserialize<JsonElement>(Base64UrlDecodificar(partes[1]));

        string? Obtener(string clave) => payload.TryGetProperty(clave, out var v) ? v.GetString() : null;

        var solicitudId = Obtener("ssg_solicitud_id") ?? throw new ArgumentException("El ticket no trae ssg_solicitud_id.");
        var flujoId = Obtener("ssg_flujo_id") ?? throw new ArgumentException("El ticket no trae ssg_flujo_id.");

        DateTimeOffset? expira = payload.TryGetProperty("exp", out var expEl) && expEl.TryGetInt64(out var expUnix)
            ? DateTimeOffset.FromUnixTimeSeconds(expUnix)
            : null;

        return new ClaimsTicket(solicitudId, flujoId, Obtener("ssg_documento_hash"), Obtener("ssg_origen"), expira);
    }

    private static byte[] Base64UrlDecodificar(string valor)
    {
        string s = valor.Replace('-', '+').Replace('_', '/');
        s = (s.Length % 4) switch { 2 => s + "==", 3 => s + "=", _ => s };
        return Convert.FromBase64String(s);
    }

    /// <param name="hiloUi">
    /// Formulario cuyo Handle se usa para <c>Invoke</c> la ventana de firma
    /// en el hilo de UI correcto — necesario cuando esta operación se lanza
    /// desde el hilo en segundo plano del servicio HTTP (ver ServicioLocal).
    /// Si es <c>null</c>, se asume que ya se está en el hilo de UI (modo
    /// heredado por consola, de un solo uso).
    /// </param>
    internal static async Task<ResultadoFirma> EjecutarFirmaAsync(ParametrosFirma parametros, ClaimsTicket ticket, Form? hiloUi)
    {
        if (ticket.ExpiraEn is { } expira && DateTimeOffset.UtcNow >= expira)
            return new ResultadoFirma(false, "El ticket de firma ya expiró — vuelve a intentarlo desde el navegador (se emite uno nuevo cada vez).", null);

        using var http = new HttpClient { BaseAddress = new Uri(parametros.GatewayUrl) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", parametros.Ticket);

        var estado = await http.GetFromJsonAsync<JsonElement>($"/api/firmas/{ticket.SolicitudId}/estado");
        var documentoId = estado.GetProperty("documentoId").GetGuid();

        var respuestaDocumento = await http.GetAsync($"/api/documentos/{documentoId}/contenido");
        respuestaDocumento.EnsureSuccessStatusCode();
        var contenido = await respuestaDocumento.Content.ReadAsByteArrayAsync();
        var tipoContenido = respuestaDocumento.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        var nombreArchivo = respuestaDocumento.Content.Headers.ContentDisposition?.FileNameStar
            ?? respuestaDocumento.Content.Headers.ContentDisposition?.FileName
            ?? documentoId.ToString();
        var hash = SHA256.HashData(contenido);

        // Ver informe de preauditoría INDECOPI/IOFE, sección 12/13 ("Documento
        // distinto al hash autorizado → Bloqueado"): el ticket declara el hash
        // que el usuario autorizó a firmar EN EL MOMENTO en que el navegador lo
        // pidió — si el documento cambió desde entonces (o el ticket viene de
        // otro documento), se rechaza ANTES de pedir el PIN. El backend
        // (FirmarLocalHandler) vuelve a comprobar esto de forma independiente,
        // así que esta comprobación aquí es solo para fallar rápido y sin
        // gastar una operación con la tarjeta.
        if (ticket.DocumentoHashEsperado is { Length: > 0 } hashEsperado &&
            !string.Equals(Convert.ToHexString(hash), hashEsperado, StringComparison.OrdinalIgnoreCase))
        {
            return new ResultadoFirma(false,
                "El documento descargado no coincide con el hash que autorizó el ticket de firma — operación rechazada por seguridad.", null);
        }

        var candidatos = EnumerarCertificados();

        int indiceElegido = -1;
        string pin = string.Empty;
        void MostrarVentana()
        {
            using var ventana = new VentanaFirma(nombreArchivo, contenido.Length, Convert.ToHexString(hash), candidatos.Select(c => c.Descripcion).ToList());
            if (ventana.ShowDialog() == DialogResult.OK)
            {
                indiceElegido = ventana.IndiceCertificadoElegido;
                pin = ventana.Pin;
            }
        }

        if (hiloUi is not null) hiloUi.Invoke(MostrarVentana);
        else MostrarVentana();

        if (indiceElegido < 0 || indiceElegido >= candidatos.Count)
            return new ResultadoFirma(false, "Operación cancelada por el usuario.", null);

        var elegido = candidatos[indiceElegido];

        // El diccionario /Sig se prepara ANTES de firmar nada — así, si algo
        // falla (documento corrupto, PdfSharpCore no puede reescribirlo,
        // etc.), se descubre sin haber gastado ninguna operación con la
        // tarjeta. Para PDF, PAdES ya NO es "mejor esfuerzo": si el flujo es
        // sobre un PDF, el Firmador Local DEBE producir un PAdES real o
        // abortar por completo (fail closed) — ver informe de preauditoría
        // INDECOPI/IOFE, hallazgo P0-03. Degradar en silencio a la firma
        // desacoplada dejaría un flujo marcado "Firmado" sin que exista
        // realmente el PAdES que el propio Firmador Local promete generar.
        bool esPdf = tipoContenido.Contains("pdf", StringComparison.OrdinalIgnoreCase);
        ResultadoPreparacionPades? preparadoPades = null;
        string? nombreFirmante = null;
        if (esPdf)
        {
            using var certificadoParaNombre = new X509Certificate2(elegido.CertDer);
            nombreFirmante = certificadoParaNombre.GetNameInfo(X509NameType.SimpleName, false);
            try
            {
                preparadoPades = PdfSignaturePlaceholder.Preparar(contenido, nombreFirmante, "Firma electrónica", DateTimeOffset.UtcNow);
            }
            catch (Exception ex)
            {
                return new ResultadoFirma(false,
                    $"No se pudo preparar la firma PAdES para este PDF — se abortó la operación sin firmar nada (ver RUNBOOK.md 12.8). Detalle: {ex.Message}", null);
            }
        }

        var firma = FirmarConTarjeta(elegido.SlotId, elegido.CkaId, hash, pin);

        string? documentoPadesBase64 = null;
        if (esPdf && preparadoPades is { } preparado)
        {
            try
            {
                using var certificado = new X509Certificate2(elegido.CertDer);
                byte[] cms = CmsBuilder.Firmar(
                    preparado.ContenidoCubierto, certificado, cadenaCertificacion: null,
                    datos => FirmarConTarjeta(elegido.SlotId, elegido.CkaId, SHA256.HashData(datos), pin));
                documentoPadesBase64 = Convert.ToBase64String(PdfSignaturePlaceholder.Inyectar(preparado, cms));
            }
            catch (Exception ex)
            {
                pin = string.Empty;
                return new ResultadoFirma(false,
                    $"La tarjeta ya firmó el hash del documento, pero no se pudo completar la firma PAdES — se abortó SIN enviar nada a SecureSign, para no dejar un flujo marcado como firmado sin PAdES real. Detalle: {ex.Message}", null);
            }
        }
        pin = string.Empty; // no persistir el PIN en memoria más de lo necesario

        var cuerpo = new
        {
            firmaBase64 = Convert.ToBase64String(firma),
            certificadoBase64 = Convert.ToBase64String(elegido.CertDer),
            algoritmo = "RsaSha256",
            documentoPadesBase64,
        };
        var respuesta = await http.PostAsJsonAsync(
            $"/api/firmas/{ticket.SolicitudId}/flujos/{ticket.FlujoId}/completar-firma-local", cuerpo);
        var textoRespuesta = await respuesta.Content.ReadAsStringAsync();

        if (!respuesta.IsSuccessStatusCode)
            return new ResultadoFirma(false, $"SecureSign rechazó el resultado (HTTP {(int)respuesta.StatusCode}): {textoRespuesta}", null);

        void MostrarExito() =>
            MessageBox.Show($"El documento \"{nombreArchivo}\" se firmó correctamente.", "SecureSign Perú", MessageBoxButtons.OK, MessageBoxIcon.Information);

        if (hiloUi is not null) hiloUi.Invoke(MostrarExito);
        else MostrarExito();

        return new ResultadoFirma(true, "Documento firmado correctamente.", JsonSerializer.Deserialize<JsonElement>(textoRespuesta));
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
