using System.Net;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace SecureSign.FirmadorLocal;

/// <summary>
/// El Firmador Local corriendo como servicio: un ícono en la bandeja del
/// sistema + un <see cref="HttpListener"/> en <c>http://127.0.0.1:PUERTO/</c>
/// (loopback únicamente — nunca escucha en la red). El visor (o cualquier
/// integrador) simplemente le hace un <c>fetch()</c> normal desde
/// JavaScript, exactamente como hace la Plataforma FIRMA PERÚ con su
/// <c>startSignature(port, param)</c> — ver comentario de <see cref="Program"/>.
///
/// Rutas:
/// - GET  /ping    → { "status": "ok" } — para que el visor detecte si el
///   servicio ya está corriendo antes de intentar firmar.
/// - POST /firmar  → recibe los mismos parámetros que antes viajaban en la
///   URI (gatewayUrl, solicitudId, flujoId, accessToken) como cuerpo JSON,
///   muestra <see cref="VentanaFirma"/>, firma, y devuelve el resultado.
///
/// La ventana de firma se muestra en el hilo de UI de este
/// <see cref="ApplicationContext"/> (vía <see cref="Control.Invoke(Delegate)"/>)
/// aunque la petición HTTP llegue en un hilo del pool — WinForms exige que
/// toda UI se cree y manipule desde un único hilo STA.
/// </summary>
internal sealed class ServicioLocal : ApplicationContext
{
    private readonly int _puerto;
    private readonly Form _hiloUi;
    private readonly NotifyIcon _icono;
    private readonly HttpListener _listener = new();
    private static readonly JsonSerializerOptions OpcionesJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <param name="puerto">
    /// Puerto donde escuchar — ver <see cref="Program.ResolverPuerto"/> para
    /// cómo se elige (por defecto <see cref="Program.PuertoPorDefecto"/>,
    /// configurable si ya está en uso por otra aplicación).
    /// </param>
    public ServicioLocal(int puerto)
    {
        _puerto = puerto;

        // Formulario invisible: solo existe para tener un Handle de Win32 al
        // que hacerle Invoke desde el hilo del HttpListener.
        _hiloUi = new Form { ShowInTaskbar = false, Opacity = 0, FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual, Location = new System.Drawing.Point(-2000, -2000) };
        _hiloUi.Load += (_, _) => _hiloUi.Hide();
        _hiloUi.Show();

        _icono = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Shield,
            Visible = true,
            Text = $"SecureSign Perú — Firmador Local (puerto {_puerto})",
        };
        var menu = new ContextMenuStrip();
        menu.Items.Add($"Escuchando en http://127.0.0.1:{_puerto}/", null, (_, _) => { }).Enabled = false;
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Salir", null, (_, _) => Salir());
        _icono.ContextMenuStrip = menu;
        _icono.DoubleClick += (_, _) =>
            MessageBox.Show(
                $"El Firmador Local está activo, escuchando en http://127.0.0.1:{_puerto}/.\n\nPuedes cerrar esta app desde el menú del ícono (clic derecho → Salir).",
                "SecureSign Perú — Firmador Local", MessageBoxButtons.OK, MessageBoxIcon.Information);

        try
        {
            _listener.Prefixes.Add($"http://127.0.0.1:{_puerto}/");
            _listener.Start();
        }
        catch (HttpListenerException ex)
        {
            MessageBox.Show(
                $"No se pudo iniciar el Firmador Local en el puerto {_puerto} — ¿ya hay una instancia corriendo, o el puerto está ocupado por otra app?\n\n" +
                $"Prueba con otro puerto: SecureSignFirmadorLocal.exe --puerto <número>\n\n{ex.Message}",
                "SecureSign Perú — Firmador Local", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Salir();
            return;
        }

        Console.WriteLine($"Firmador Local activo — escuchando en http://127.0.0.1:{_puerto}/");
        _ = EscucharAsync();
    }

    private async Task EscucharAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext contexto;
            try
            {
                contexto = await _listener.GetContextAsync();
            }
            catch (Exception)
            {
                break; // el listener se detuvo (Salir()) — fin del ciclo.
            }

            _ = Task.Run(() => AtenderPeticionAsync(contexto));
        }
    }

    private async Task AtenderPeticionAsync(HttpListenerContext contexto)
    {
        var respuesta = contexto.Response;
        // CORS abierto: es un servicio 100% local (loopback) pensado para
        // que CUALQUIER página del integrador pueda llamarlo, igual que
        // Firma Perú no restringe el origen de quien llama a su servicio local.
        respuesta.Headers["Access-Control-Allow-Origin"] = "*";
        respuesta.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
        respuesta.Headers["Access-Control-Allow-Headers"] = "Content-Type";

        try
        {
            if (contexto.Request.HttpMethod == "OPTIONS")
            {
                respuesta.StatusCode = 204;
                respuesta.Close();
                return;
            }

            var ruta = contexto.Request.Url?.AbsolutePath ?? string.Empty;

            if (ruta == "/ping" && contexto.Request.HttpMethod == "GET")
            {
                await ResponderJsonAsync(respuesta, 200, new { status = "ok", puerto = _puerto });
                return;
            }

            if (ruta == "/firmar" && contexto.Request.HttpMethod == "POST")
            {
                using var lector = new StreamReader(contexto.Request.InputStream, Encoding.UTF8);
                var cuerpoJson = await lector.ReadToEndAsync();
                var parametros = Program.ParsearParametrosDesdeCuerpoJson(cuerpoJson);
                var ticket = Program.DecodificarTicket(parametros.Ticket);

                // Ver informe de preauditoría INDECOPI/IOFE, sección 12
                // ("Origen web no autorizado → Bloqueado") y RUNBOOK.md
                // 12.13: el ticket declara el origen exacto (esquema+host+puerto)
                // de la página que lo pidió — si la petición HTTP que de
                // verdad llega aquí trae un Origin distinto (o ninguno,
                // cuando el ticket sí declaró uno), se rechaza sin mostrar
                // ningún diálogo. Esto es lo único que puede detectar, en
                // esta máquina, que OTRA pestaña/sitio abierto en el
                // navegador — no la página legítima que el usuario estaba
                // usando — es quien está llamando a este servicio local.
                var origenPeticion = contexto.Request.Headers["Origin"];
                if (!string.IsNullOrEmpty(ticket.Origen) && !string.Equals(ticket.Origen, origenPeticion, StringComparison.OrdinalIgnoreCase))
                {
                    await ResponderJsonAsync(respuesta, 403, new
                    {
                        ok = false,
                        mensaje = $"El origen de esta petición ({origenPeticion ?? "(ninguno)"}) no coincide con el origen autorizado por el ticket de firma — operación rechazada.",
                    });
                    return;
                }

                var resultado = await Program.EjecutarFirmaAsync(parametros, ticket, _hiloUi);
                await ResponderJsonAsync(respuesta, resultado.Ok ? 200 : 400, resultado);
                return;
            }

            await ResponderJsonAsync(respuesta, 404, new { error = "Ruta no encontrada." });
        }
        catch (Exception ex)
        {
            try { await ResponderJsonAsync(respuesta, 500, new { error = ex.Message }); }
            catch { /* la conexión ya pudo haberse cerrado del otro lado */ }
        }
    }

    private static async Task ResponderJsonAsync(HttpListenerResponse respuesta, int codigoEstado, object cuerpo)
    {
        respuesta.StatusCode = codigoEstado;
        respuesta.ContentType = "application/json; charset=utf-8";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(cuerpo, OpcionesJson);
        respuesta.ContentLength64 = bytes.Length;
        await respuesta.OutputStream.WriteAsync(bytes);
        respuesta.Close();
    }

    private void Salir()
    {
        _icono.Visible = false;
        try { _listener.Stop(); } catch { /* ya pudo estar detenido */ }
        _hiloUi.Close();
        ExitThread();
    }
}
