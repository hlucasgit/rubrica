using System.Net;
using System.Net.Sockets;
using SecureSign.Trust;

namespace SecureSign.UnitTests.Trust;

/// <summary>
/// RUNBOOK.md 12.63: <see cref="ExtensionesX509"/> extrae URLs DECLARADAS DENTRO de un certificado (AIA/CRL) sin
/// validarlas — <see cref="DescargadorCertificadosIntermedios"/>/<see cref="VerificadorRevocacionCrl"/>/
/// <see cref="VerificadorRevocacionOcsp"/> las siguen ANTES de saber si el certificado es confiable, y
/// <c>POST /api/validador/pdf</c> es público sin token (RUNBOOK.md 12.12) — un atacante anónimo puede hacer que
/// el servidor golpee una IP interna o metadata de nube con solo subir un PDF con un certificado fabricado a
/// mano. <see cref="GuardiaSsrf"/> cierra eso.
/// </summary>
public sealed class GuardiaSsrfTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.5.5.5")]
    [InlineData("10.0.0.5")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")] // metadata de nube (AWS/Azure/GCP)
    [InlineData("0.0.0.0")]
    [InlineData("::1")]
    [InlineData("fc00::1")]
    [InlineData("fe80::1")]
    public void Direcciones_privadas_o_internas_se_rechazan(string ip)
    {
        Assert.True(GuardiaSsrf.EsPrivadaOInterna(IPAddress.Parse(ip)));
        Assert.False(GuardiaSsrf.EsPublica(IPAddress.Parse(ip)));
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("172.15.255.255")] // justo AFUERA de 172.16.0.0/12
    [InlineData("172.32.0.0")]     // justo AFUERA de 172.16.0.0/12 por el otro lado
    [InlineData("2606:4700:4700::1111")] // Cloudflare DNS, IPv6 pública real
    public void Direcciones_publicas_se_aceptan(string ip)
    {
        Assert.False(GuardiaSsrf.EsPrivadaOInterna(IPAddress.Parse(ip)));
        Assert.True(GuardiaSsrf.EsPublica(IPAddress.Parse(ip)));
    }

    [Fact]
    public async Task El_manejador_real_rechaza_una_conexion_a_localhost_con_un_servidor_escuchando_de_verdad()
    {
        // Servidor TCP real en loopback — si el guard fallara, esta prueba se conectaría exitosamente.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            int puerto = ((IPEndPoint)listener.LocalEndpoint).Port;
            using var http = new HttpClient(GuardiaSsrf.CrearManejador());

            var ex = await Assert.ThrowsAsync<HttpRequestException>(
                () => http.GetByteArrayAsync($"http://127.0.0.1:{puerto}/cualquier-cosa"));

            Assert.Contains("SSRF", ex.Message);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task El_manejador_real_permite_conectar_a_una_direccion_publica()
    {
        // No golpea la red real de verdad: falla al conectar (puerto cerrado en una IP pública de prueba,
        // TEST-NET-1 RFC 5737, reservada para documentación — nunca enrutable de verdad) con un error de RED
        // (tiempo agotado/rechazo), NUNCA con el mensaje de SSRF — confirma que el guard dejó pasar el intento.
        using var http = new HttpClient(GuardiaSsrf.CrearManejador()) { Timeout = TimeSpan.FromSeconds(3) };

        var ex = await Record.ExceptionAsync(() => http.GetByteArrayAsync("http://203.0.113.1:81/"));

        Assert.NotNull(ex);
        Assert.DoesNotContain("SSRF", ex!.ToString());
    }
}
