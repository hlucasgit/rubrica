using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace SecureSign.Trust;

/// <summary>
/// Protección SSRF para todo cliente HTTP que siga una URL declarada DENTRO de un certificado (AIA "CA Issuers",
/// AIA OCSP, CRL Distribution Points — <see cref="ExtensionesX509"/>). Esas URLs las elige quien emitió (o
/// quien fabricó) el certificado, no el operador de SecureSign: <see cref="ValidadorCertificados.ValidarAsync"/>
/// las sigue ANTES de saber si el certificado es siquiera confiable — <c>POST /api/validador/pdf</c> es público
/// y sin token (RUNBOOK.md 12.12), así que cualquiera puede subir un PDF con un certificado autofirmado cuyo
/// AIA/CRL apunte a <c>http://169.254.169.254/...</c> (metadata de nube) o a un servicio interno de Docker.
///
/// El filtro va en <see cref="SocketsHttpHandler.ConnectCallback"/>, no antes: valida la IP YA RESUELTA justo
/// antes de abrir el socket, así una respuesta DNS que cambie entre la validación y la conexión (DNS rebinding)
/// no sirve para saltarse el filtro — se conecta exactamente a la IP que se validó, nunca se vuelve a resolver
/// el nombre. No se restringe por esquema (`http://` puro es normal en OCSP real de CAs reales — RUNBOOK.md
/// documenta más de una que lo usa) — el riesgo real es la IP de destino, no el protocolo.
/// </summary>
public static class GuardiaSsrf
{
    public static SocketsHttpHandler CrearManejador() => new()
    {
        ConnectCallback = async (contexto, ct) =>
        {
            IPAddress[] direcciones;
            try { direcciones = await Dns.GetHostAddressesAsync(contexto.DnsEndPoint.Host, ct); }
            catch (Exception ex) { throw new HttpRequestException($"No se pudo resolver '{contexto.DnsEndPoint.Host}': {ex.Message}", ex); }

            var direccion = Array.Find(direcciones, EsPublica)
                ?? throw new HttpRequestException(
                    $"Conexión rechazada (protección SSRF): todas las direcciones resueltas para '{contexto.DnsEndPoint.Host}' son privadas, de loopback o de enlace local.");

            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(direccion, contexto.DnsEndPoint.Port), ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch { socket.Dispose(); throw; }
        },
    };

    public static bool EsPublica(IPAddress direccion) => !EsPrivadaOInterna(direccion);

    public static bool EsPrivadaOInterna(IPAddress direccion)
    {
        if (IPAddress.IsLoopback(direccion)) return true;

        var ip = direccion.IsIPv4MappedToIPv6 ? direccion.MapToIPv4() : direccion;

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            byte[] b = ip.GetAddressBytes();
            return b[0] switch
            {
                0 => true,                                  // 0.0.0.0/8
                10 => true,                                  // 10.0.0.0/8
                127 => true,                                 // 127.0.0.0/8
                169 when b[1] == 254 => true,                // 169.254.0.0/16 — link-local, incluye metadata de nube
                172 when b[1] is >= 16 and <= 31 => true,     // 172.16.0.0/12 — incluye redes por defecto de Docker
                192 when b[1] == 168 => true,                // 192.168.0.0/16
                _ => false,
            };
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal) return true;
            byte[] b = ip.GetAddressBytes();
            return (b[0] & 0xFE) == 0xFC; // fc00::/7 — unique local address (equivalente IPv6 de RFC 1918)
        }

        return true; // familia de direcciones no reconocida: rechazar por defecto, nunca confiar a ciegas.
    }
}
