using System.Net;

namespace SecureSign.UnitTests.Trust;

/// <summary>
/// Sustituye la red real (RENIEC/INDECOPI) para los tests de SecureSign.Trust:
/// se inyecta directamente como el <see cref="HttpMessageHandler"/> de un
/// <see cref="HttpClient"/> normal — DescargadorCertificadosIntermedios,
/// VerificadorRevocacionCrl y VerificadorRevocacionOcsp ya reciben su
/// HttpClient por constructor, así que no hace falta ningún cambio de
/// producción para poder probarlos offline.
/// </summary>
internal sealed class HandlerHttpFalso : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpRequestMessage, Task<HttpResponseMessage>>> _rutas = new();

    public void ResponderBytes(string url, byte[] cuerpo, string contentType) =>
        _rutas[url] = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(cuerpo) { Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType) } },
        });

    public void Responder(string url, Func<HttpRequestMessage, Task<HttpResponseMessage>> respuesta) => _rutas[url] = respuesta;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string url = request.RequestUri!.ToString();
        if (_rutas.TryGetValue(url, out var handler)) return await handler(request);
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }
}
