using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Ocsp;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Tsp;
using Org.BouncyCastle.Crypto.Operators;
using SecureSign.Domain.Primitives;
using SecureSign.Identity.Domain;
using SecureSign.Pades;
using SecureSign.Signature.Application.Clients;
using SecureSign.Signature.Application.FirmarDocumento;
using SecureSign.Signature.Application.FirmarLocal;
using SecureSign.Signature.Domain;
using SecureSign.Signature.Infrastructure.Confianza;
using SecureSign.Trust;
using SecureSign.Tsa;
using SecureSign.UnitTests.Pades;
using SecureSign.UnitTests.Trust;
using SecureSign.Validator;
using BcCertificate = Org.BouncyCastle.X509.X509Certificate;
using NodoCadena = SecureSign.UnitTests.Trust.CadenaDePruebaHelper.NodoCadena;

namespace SecureSign.UnitTests.Integracion;

/// <summary>
/// De punta a punta, con las piezas REALES y sin red ni base de datos:
/// <see cref="FirmarLocalHandler"/> → <see cref="ValidadorConfianzaFirmanteIofe"/> → <see cref="ValidadorCertificados"/>
/// (cadena X.509, TSL, CRL, OCSP) → PAdES-LT (<c>PdfDssWriter</c>) → PAdES-LTA (<c>ClienteTsaRfc3161</c> + sello de archivo)
/// → <see cref="ValidadorDocumentoPades"/> sobre el documento que el handler dejó guardado. Solo son dobles los bordes
/// de red/persistencia: HTTP (AIA, CRL, OCSP, TSA — una CA y una TSA de prueba con las mismas llaves/protocolos
/// reales), el repositorio y los clientes hacia otros servicios. La firma del hash la hace una llave RSA en memoria:
/// el camino PKCS#11 ya se prueba aparte contra un SoftHSM2 real (RUNBOOK 12.26). Ver RUNBOOK 12.33.
/// </summary>
public sealed class FirmaLocalDePuntaAPuntaTests : IDisposable
{
    private readonly List<string> _archivos = [];
    private readonly List<string> _directorios = [];

    public void Dispose()
    {
        foreach (var a in _archivos) { try { File.Delete(a); } catch { /* mejor esfuerzo */ } }
        foreach (var d in _directorios) { try { Directory.Delete(d, recursive: true); } catch { /* mejor esfuerzo */ } }
    }

    // ---------------------------------------------------------------- dobles de los bordes

    private sealed class InterruptorRed(HttpMessageHandler interno) : DelegatingHandler(interno)
    {
        public bool Caida { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Caida ? throw new HttpRequestException("red caída (prueba)") : base.SendAsync(request, ct);
    }

    private sealed class TsaFalsa(TimeStampTokenGenerator generador) : HttpMessageHandler
    {
        public bool Caida { get; set; }
        public int Solicitudes { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Solicitudes++;
            if (Caida) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

            var solicitud = new TimeStampRequest(await request.Content!.ReadAsByteArrayAsync(ct));
            var respuesta = new TimeStampResponseGenerator(generador, TspAlgorithms.Allowed)
                .Generate(solicitud, BigInteger.ValueOf(Random.Shared.NextInt64(1, long.MaxValue)), DateTime.UtcNow);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(respuesta.GetEncoded()) };
        }
    }

    private sealed class RepositorioEnMemoria : ISolicitudFirmaRepository
    {
        public SolicitudFirma? Solicitud { get; set; }
        public int Actualizaciones { get; private set; }

        public Task AgregarAsync(SolicitudFirma s, CancellationToken ct = default) { Solicitud = s; return Task.CompletedTask; }
        public Task<SolicitudFirma?> ObtenerPorIdAsync(Guid t, Guid id, CancellationToken ct = default) => Task.FromResult(Solicitud);
        public Task<SolicitudFirma?> ObtenerPorCodigoVerificacionAsync(string c, CancellationToken ct = default) => Task.FromResult(Solicitud);
        public Task ActualizarAsync(SolicitudFirma s, CancellationToken ct = default) { Actualizaciones++; return Task.CompletedTask; }
        public Task<IReadOnlyList<SolicitudFirma>> ListarPendientesPorFirmanteAsync(Guid t, Guid u, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SolicitudFirma>>([]);
    }

    private sealed class IdentidadFalsa(int indice) : IIdentidadServiceClient
    {
        public Task<IndiceConfianzaRemoto> ObtenerIndiceConfianzaAsync(Guid usuarioId, CancellationToken ct = default) =>
            Task.FromResult(new IndiceConfianzaRemoto(indice));
    }

    private sealed class DocumentosFalso(DocumentoRemoto documento) : IDocumentosServiceClient
    {
        public byte[]? PadesGuardado { get; private set; }
        public bool MarcadoFirmado { get; private set; }

        public Task<DocumentoRemoto?> ObtenerAsync(Guid id, CancellationToken ct = default) => Task.FromResult<DocumentoRemoto?>(documento);
        public Task MarcarFirmadoAsync(Guid id, CancellationToken ct = default) { MarcadoFirmado = true; return Task.CompletedTask; }
        public Task<ContenidoDocumentoRemoto?> ObtenerContenidoAsync(Guid id, CancellationToken ct = default) => Task.FromResult<ContenidoDocumentoRemoto?>(null);
        public Task GuardarDocumentoFirmadoPadesAsync(Guid id, byte[] contenido, CancellationToken ct = default) { PadesGuardado = contenido; return Task.CompletedTask; }
    }

    private sealed class EvidenciasFalso : IEvidenciasServiceClient
    {
        public List<string> Tipos { get; } = [];
        public Task RegistrarAsync(Guid documentoId, string tipo, DatosContextualesRemoto datos, Guid? firmaId = null, CancellationToken ct = default) { Tipos.Add(tipo); return Task.CompletedTask; }
    }

    private sealed class AuditoriaFalsa : IAuditoriaServiceClient
    {
        public List<(string Tipo, string Detalle)> Eventos { get; } = [];
        public Task RegistrarAsync(string tipo, string detalle, Guid? tenantId, CancellationToken ct = default) { Eventos.Add((tipo, detalle)); return Task.CompletedTask; }
    }

    // ---------------------------------------------------------------- escenario

    private sealed class Escenario
    {
        public required FirmarLocalHandler Handler { get; init; }
        public required ValidadorDocumentoPades ValidadorIndependiente { get; init; }
        public required RepositorioEnMemoria Repositorio { get; init; }
        public required DocumentosFalso Documentos { get; init; }
        public required EvidenciasFalso Evidencias { get; init; }
        public required AuditoriaFalsa Auditoria { get; init; }
        public required InterruptorRed Red { get; init; }
        public required TsaFalsa Tsa { get; init; }
        public required NodoCadena Hoja { get; init; }
        public required NodoCadena Intermedia { get; init; }
        public required byte[] PdfOriginal { get; init; }
        public required Guid TenantId { get; init; }
        public required Guid SolicitudId { get; init; }
        public required Guid FlujoId { get; init; }

        public byte[] CertificadoDer => Hoja.CertificadoBc.GetEncoded();
        public byte[] HashDocumento => SHA256.HashData(PdfOriginal);

        public static RSA RsaDe(NodoCadena nodo)
        {
            var rsa = RSA.Create();
            rsa.ImportParameters(DotNetUtilities.ToRSAParameters((RsaPrivateCrtKeyParameters)nodo.Llaves.Private));
            return rsa;
        }

        /// <summary>Lo que haría el Firmador Local: PAdES sobre el PDF + firma del hash desacoplada, ambos con la llave del firmante.</summary>
        public FirmarLocalCommand ComoLoHaceElFirmadorLocal(NodoCadena? firmanteDelPades = null, Func<byte[], byte[]>? alterarPades = null, ClaimsTicketFirmaLocal? ticket = null)
        {
            using var rsaPades = RsaDe(firmanteDelPades ?? Hoja);
            using var certPades = (firmanteDelPades ?? Hoja).ComoDotNet();
            var preparado = PdfSignaturePlaceholder.Preparar(PdfOriginal, "Firmante de prueba", "Prueba de punta a punta", DateTimeOffset.UtcNow);
            byte[] cms = CmsBuilder.Firmar(preparado.ContenidoCubierto, certPades, cadenaCertificacion: null,
                datos => rsaPades.SignData(datos, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            byte[] pades = PdfSignaturePlaceholder.Inyectar(preparado, cms);
            if (alterarPades is not null) pades = alterarPades(pades);

            using var rsaHash = RsaDe(Hoja);
            byte[] firmaHash = rsaHash.SignHash(HashDocumento, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            return new FirmarLocalCommand(
                TenantId, SolicitudId, FlujoId,
                Convert.ToBase64String(firmaHash), Convert.ToBase64String(CertificadoDer), "RsaSha256",
                new DatosContextualesRemoto("127.0.0.1", "prueba", "prueba"),
                Convert.ToBase64String(pades), ticket);
        }
    }

    private Escenario Construir(
        DateTime? notBeforeHoja = null, DateTime? notAfterHoja = null,
        bool hojaEnTsl = true, bool hojaRevocada = false, int indiceConfianza = 95,
        string[]? oidsEku = null, string[]? oidsPolitica = null, OpcionesPoliticaCertificado? politica = null)
    {
        string sufijo = Guid.NewGuid().ToString("N");
        string urlRaiz = $"https://ca.prueba.local/raiz-{sufijo}.cer";
        string urlIntermedia = $"https://ca.prueba.local/intermedia-{sufijo}.cer";
        string urlOcsp = $"https://ca.prueba.local/ocsp-{sufijo}";
        string urlCrl = $"https://ca.prueba.local/crl-{sufijo}.crl";

        var raiz = CadenaDePruebaHelper.GenerarRaiz($"Raiz Prueba {sufijo}");
        var intermedia = CadenaDePruebaHelper.GenerarIntermedia(raiz, $"Intermedia Prueba {sufijo}", urlRaiz);
        var hoja = CadenaDePruebaHelper.GenerarHoja(intermedia, $"Firmante Prueba {sufijo}", urlIntermedia, urlOcsp, urlCrl, notBeforeHoja, notAfterHoja,
            oidsEku: oidsEku, oidsPolitica: oidsPolitica);

        // CA de prueba: sirve la cadena por AIA, y CRL + OCSP firmados con las llaves reales de la intermedia.
        var ca = new HandlerHttpFalso();
        ca.ResponderBytes(urlRaiz, raiz.CertificadoBc.GetEncoded(), "application/pkix-cert");
        ca.ResponderBytes(urlIntermedia, intermedia.CertificadoBc.GetEncoded(), "application/pkix-cert");
        var revocados = hojaRevocada ? [new BigInteger(hoja.ComoDotNet().SerialNumber, 16)] : Array.Empty<BigInteger>();
        ca.ResponderBytes(urlCrl, CadenaDePruebaHelper.GenerarCrl(intermedia, revocados), "application/pkix-crl");
        ca.Responder(urlOcsp, req => ResponderOcsp(req, intermedia, hojaRevocada ? new RevokedStatus(DateTime.UtcNow.AddHours(-1), 0) : CertificateStatus.Good));
        var red = new InterruptorRed(ca);

        string dirRaices = Path.Combine(Path.GetTempPath(), $"securesign-e2e-raices-{sufijo}");
        Directory.CreateDirectory(dirRaices);
        _directorios.Add(dirRaices);
        File.WriteAllBytes(Path.Combine(dirRaices, "raiz.cer"), raiz.CertificadoBc.GetEncoded());

        string tsl = TslDePruebaHelper.EscribirArchivoTemporal(
            hojaEnTsl ? [("Intermedia Acreditada", intermedia.CertificadoBc.GetEncoded())] : []);
        _archivos.Add(tsl);

        var http = new HttpClient(red);
        var validadorCertificados = new ValidadorCertificados(
            AlmacenRaicesConfiables.CargarDesdeDirectorio(dirRaices), ListaConfianzaIofe.CargarDesdeArchivo(tsl),
            new DescargadorCertificadosIntermedios(http), new VerificadorRevocacionCrl(http), new VerificadorRevocacionOcsp(http),
            politica);

        var (generadorTsa, _) = TsaDePruebaHelper.CrearTsaDePrueba();
        var tsa = new TsaFalsa(generadorTsa);

        byte[] pdf = PdfDePruebaHelper.CrearPdfMinimo($"Documento de punta a punta {sufijo}");
        var tenant = Guid.NewGuid();
        var documentoId = Guid.NewGuid();
        var solicitud = SolicitudFirma.Crear(tenant, documentoId, TipoFirma.Avanzada, false, [(Guid.NewGuid(), 1)], Guid.NewGuid()).Valor!;
        var flujo = solicitud.Flujos.Single();
        solicitud.NotificarFirmante(flujo.Id);
        solicitud.RegistrarVisualizacion(flujo.Id);

        var repositorio = new RepositorioEnMemoria { Solicitud = solicitud };
        var documentos = new DocumentosFalso(new DocumentoRemoto(documentoId, Convert.ToHexString(SHA256.HashData(pdf)), "Registrado", "application/pdf"));
        var evidencias = new EvidenciasFalso();
        var auditoria = new AuditoriaFalsa();

        var handler = new FirmarLocalHandler(
            repositorio, new IdentidadFalsa(indiceConfianza), documentos, evidencias,
            new ValidadorConfianzaFirmanteIofe(validadorCertificados), auditoria,
            new ClienteTsaRfc3161(new HttpClient(tsa)), new OpcionesTsa { UrlTsa = "https://tsa.prueba.local/" });

        return new Escenario
        {
            Handler = handler, ValidadorIndependiente = new ValidadorDocumentoPades(validadorCertificados),
            Repositorio = repositorio, Documentos = documentos, Evidencias = evidencias, Auditoria = auditoria,
            Red = red, Tsa = tsa, Hoja = hoja, Intermedia = intermedia, PdfOriginal = pdf,
            TenantId = tenant, SolicitudId = solicitud.Id, FlujoId = flujo.Id,
        };
    }

    private static async Task<HttpResponseMessage> ResponderOcsp(HttpRequestMessage request, NodoCadena firmante, CertificateStatus estado)
    {
        byte[] cuerpo = await request.Content!.ReadAsByteArrayAsync();
        var certId = new OcspReq(cuerpo).GetRequestList()[0].GetCertID();
        var generador = new BasicOcspRespGenerator(firmante.Llaves.Public);
        generador.AddResponse(certId, estado);
        var basico = generador.Generate(new Asn1SignatureFactory("SHA256WITHRSA", firmante.Llaves.Private), [], DateTime.UtcNow);
        var respuesta = new OCSPRespGenerator().Generate(OCSPRespGenerator.Successful, basico);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(respuesta.GetEncoded()) };
    }

    private static string Latin1(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    // ---------------------------------------------------------------- camino feliz

    [Fact]
    public async Task Firma_valida_produce_PAdES_LTA_que_el_validador_independiente_da_por_valido()
    {
        var e = Construir();

        var resultado = await e.Handler.Handle(e.ComoLoHaceElFirmadorLocal(), CancellationToken.None);

        Assert.True(resultado.EsExitoso, resultado.Error);
        Assert.Equal("Firmado", resultado.Valor!.EstadoSolicitud);
        Assert.True(e.Documentos.MarcadoFirmado);
        Assert.Contains("Firma", e.Evidencias.Tipos);
        Assert.Equal(1, e.Repositorio.Actualizaciones);

        var guardado = e.Documentos.PadesGuardado;
        Assert.NotNull(guardado);
        string texto = Latin1(guardado!);
        Assert.Contains("/DSS", texto);            // PAdES-LT: cadena + CRL + OCSP embebidos
        Assert.Contains("/DocTimeStamp", texto);   // PAdES-LTA: sello de archivo
        Assert.Equal(1, e.Tsa.Solicitudes);

        // Validador INDEPENDIENTE (otro camino de código que el que generó el PDF) sobre lo que quedó guardado.
        var validacion = await e.ValidadorIndependiente.ValidarAsync(guardado);
        Assert.True(validacion.DocumentoValido, string.Join(" | ", validacion.Firmas.SelectMany(f => f.Evidencia)));
        Assert.Equal(1, validacion.TotalFirmas);
        var sello = Assert.Single(validacion.SellosDeArchivo);
        Assert.True(sello.Valido, sello.Error);
    }

    [Fact]
    public async Task El_DSS_embebido_contiene_la_cadena_la_CRL_y_la_respuesta_OCSP_reales()
    {
        var e = Construir();
        await e.Handler.Handle(e.ComoLoHaceElFirmadorLocal(), CancellationToken.None);

        string texto = Latin1(e.Documentos.PadesGuardado!);

        Assert.Contains("/Certs", texto);
        Assert.Contains("/CRLs", texto);
        Assert.Contains("/OCSPs", texto);
        Assert.Contains("/VRI", texto);
    }

    // ---------------------------------------------------------------- rechazos: nada se guarda, todo se audita

    private static void AssertRechazoSinEfectos(Escenario e, Result<FirmarDocumentoResponse> resultado, string esperadoEnError, string? motivoEspecifico = null)
    {
        Assert.False(resultado.EsExitoso);
        Assert.Contains(esperadoEnError, resultado.Error, StringComparison.OrdinalIgnoreCase);
        if (motivoEspecifico is not null) Assert.Contains(motivoEspecifico, resultado.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Null(e.Documentos.PadesGuardado);
        Assert.False(e.Documentos.MarcadoFirmado);
        Assert.Equal(0, e.Repositorio.Actualizaciones);
        Assert.DoesNotContain("Firma", e.Evidencias.Tipos);
        Assert.NotEqual(EstadoSolicitudFirma.Firmado, e.Repositorio.Solicitud!.Estado);
    }

    [Fact]
    public async Task Certificado_revocado_se_rechaza_y_queda_auditado()
    {
        var e = Construir(hojaRevocada: true);

        var resultado = await e.Handler.Handle(e.ComoLoHaceElFirmadorLocal(), CancellationToken.None);

        AssertRechazoSinEfectos(e, resultado, "validación de confianza", motivoEspecifico: "Revoked");
        Assert.Contains(e.Auditoria.Eventos, ev => ev.Tipo == "CertificadoRechazadoPorConfianza");
    }

    [Fact]
    public async Task Certificado_expirado_se_rechaza_y_queda_auditado()
    {
        var e = Construir(notBeforeHoja: DateTime.UtcNow.AddYears(-2), notAfterHoja: DateTime.UtcNow.AddYears(-1));

        var resultado = await e.Handler.Handle(e.ComoLoHaceElFirmadorLocal(), CancellationToken.None);

        AssertRechazoSinEfectos(e, resultado, "validación de confianza", motivoEspecifico: "NO vigente");
        Assert.Contains(e.Auditoria.Eventos, ev => ev.Tipo == "CertificadoRechazadoPorConfianza");
    }

    [Fact]
    public async Task Certificado_de_una_CA_que_no_esta_en_la_TSL_se_rechaza()
    {
        var e = Construir(hojaEnTsl: false);

        var resultado = await e.Handler.Handle(e.ComoLoHaceElFirmadorLocal(), CancellationToken.None);

        AssertRechazoSinEfectos(e, resultado, "validación de confianza", motivoEspecifico: "TSL");
    }

    [Fact]
    public async Task Sin_red_hacia_la_CA_no_se_puede_determinar_la_revocacion_y_se_rechaza()
    {
        var e = Construir();
        e.Red.Caida = true;

        var resultado = await e.Handler.Handle(e.ComoLoHaceElFirmadorLocal(), CancellationToken.None);

        // Fail closed: nunca se acepta "no pude comprobarlo" como "no está revocado".
        AssertRechazoSinEfectos(e, resultado, "validación de confianza", motivoEspecifico: "Unavailable");
    }

    [Fact]
    public async Task Firmante_sin_indice_de_confianza_suficiente_se_rechaza_antes_de_tocar_el_certificado()
    {
        var e = Construir(indiceConfianza: 30);

        var resultado = await e.Handler.Handle(e.ComoLoHaceElFirmadorLocal(), CancellationToken.None);

        AssertRechazoSinEfectos(e, resultado, "nivel de confianza");
        Assert.Empty(e.Auditoria.Eventos);
    }

    [Fact]
    public async Task PDF_alterado_despues_de_firmar_se_rechaza()
    {
        var e = Construir();
        var comando = e.ComoLoHaceElFirmadorLocal(alterarPades: pdf => { var copia = (byte[])pdf.Clone(); copia[60] ^= 0xFF; return copia; });

        var resultado = await e.Handler.Handle(comando, CancellationToken.None);

        AssertRechazoSinEfectos(e, resultado, "PAdES");
    }

    [Fact]
    public async Task PAdES_firmado_con_otro_certificado_que_la_firma_desacoplada_se_rechaza()
    {
        var e = Construir();
        var otro = CadenaDePruebaHelper.GenerarHoja(e.Intermedia, "Otro Firmante", "https://x/i", "https://x/o", "https://x/c");

        var resultado = await e.Handler.Handle(e.ComoLoHaceElFirmadorLocal(firmanteDelPades: otro), CancellationToken.None);

        AssertRechazoSinEfectos(e, resultado, "no coincide");
    }

    [Fact]
    public async Task Ticket_cuyo_hash_ya_no_coincide_con_el_documento_se_rechaza_y_queda_auditado()
    {
        var e = Construir();
        var ticketViejo = new ClaimsTicketFirmaLocal(e.SolicitudId, e.FlujoId, e.Repositorio.Solicitud!.DocumentoId, new string('0', 64));

        var resultado = await e.Handler.Handle(e.ComoLoHaceElFirmadorLocal(ticket: ticketViejo), CancellationToken.None);

        AssertRechazoSinEfectos(e, resultado, "ticket");
        Assert.Contains(e.Auditoria.Eventos, ev => ev.Tipo == "TicketFirmaLocalRechazado");
    }

    // ---------------------------------------------------------------- EKU y CertificatePolicies (RUNBOOK 12.34)

    private const string EkuCorreoSeguro = "1.3.6.1.5.5.7.3.4";   // el que declara el DNIe real (id-kp-emailProtection)
    private const string PoliticaDePrueba = "1.3.6.1.4.1.99999.1.1";
    private const string OtraPolitica = "1.3.6.1.4.1.99999.2.2";

    [Fact]
    public async Task Por_defecto_EKU_y_politicas_se_reportan_pero_no_rechazan()
    {
        // Configuración por defecto (sin OID permitidos): un certificado con el EKU "Secure Email" del DNIe real y una política cualquiera se acepta.
        var e = Construir(oidsEku: [EkuCorreoSeguro], oidsPolitica: [PoliticaDePrueba]);

        var resultado = await e.Handler.Handle(e.ComoLoHaceElFirmadorLocal(), CancellationToken.None);
        Assert.True(resultado.EsExitoso, resultado.Error);

        var validacion = await e.ValidadorIndependiente.ValidarAsync(e.Documentos.PadesGuardado!);
        var evidencia = validacion.Firmas.Single().Evidencia;
        Assert.True(validacion.DocumentoValido);
        Assert.Contains(evidencia, l => l.Contains("ExtendedKeyUsage") && l.Contains(EkuCorreoSeguro));
        Assert.Contains(evidencia, l => l.Contains("CertificatePolicies") && l.Contains(PoliticaDePrueba));
        Assert.Contains(evidencia, l => l.Contains("solo informativa"));
        Assert.Equal(EstadoPolitica.SoloInformativa, validacion.Firmas.Single().ValidacionCertificado!.Politica!.Estado);
    }

    [Fact]
    public async Task Un_certificado_sin_esas_extensiones_se_acepta_y_se_reporta_como_no_declarado()
    {
        var e = Construir();

        var resultado = await e.Handler.Handle(e.ComoLoHaceElFirmadorLocal(), CancellationToken.None);
        Assert.True(resultado.EsExitoso, resultado.Error);

        var validacion = await e.ValidadorIndependiente.ValidarAsync(e.Documentos.PadesGuardado!);
        Assert.Contains(validacion.Firmas.Single().Evidencia, l => l.Contains("CertificatePolicies: (no declarado)"));
    }

    [Fact]
    public async Task Exigida_y_el_certificado_cumple_se_acepta()
    {
        var politica = new OpcionesPoliticaCertificado { OidsPoliticaPermitidos = [PoliticaDePrueba, OtraPolitica], OidsEkuPermitidos = [EkuCorreoSeguro], Exigir = true };
        var e = Construir(oidsEku: [EkuCorreoSeguro], oidsPolitica: [PoliticaDePrueba], politica: politica);

        var resultado = await e.Handler.Handle(e.ComoLoHaceElFirmadorLocal(), CancellationToken.None);

        Assert.True(resultado.EsExitoso, resultado.Error);
    }

    [Fact]
    public async Task Exigida_y_el_certificado_no_cumple_se_rechaza_con_el_motivo()
    {
        var politica = new OpcionesPoliticaCertificado { OidsPoliticaPermitidos = [OtraPolitica], Exigir = true };
        var e = Construir(oidsPolitica: [PoliticaDePrueba], politica: politica);

        var resultado = await e.Handler.Handle(e.ComoLoHaceElFirmadorLocal(), CancellationToken.None);

        AssertRechazoSinEfectos(e, resultado, "validación de confianza", motivoEspecifico: "EXIGIDA");
        Assert.Contains(e.Auditoria.Eventos, ev => ev.Tipo == "CertificadoRechazadoPorConfianza");
    }

    [Fact]
    public async Task Exigida_y_el_certificado_no_declara_ninguna_politica_se_rechaza()
    {
        var politica = new OpcionesPoliticaCertificado { OidsPoliticaPermitidos = [PoliticaDePrueba], Exigir = true };
        var e = Construir(politica: politica);

        var resultado = await e.Handler.Handle(e.ComoLoHaceElFirmadorLocal(), CancellationToken.None);

        AssertRechazoSinEfectos(e, resultado, "validación de confianza", motivoEspecifico: "EXIGIDA");
    }

    [Fact]
    public async Task Configurada_pero_no_exigida_solo_se_registra_el_incumplimiento()
    {
        var politica = new OpcionesPoliticaCertificado { OidsPoliticaPermitidos = [OtraPolitica], Exigir = false };
        var e = Construir(oidsPolitica: [PoliticaDePrueba], politica: politica);

        var resultado = await e.Handler.Handle(e.ComoLoHaceElFirmadorLocal(), CancellationToken.None);
        Assert.True(resultado.EsExitoso, resultado.Error);

        var validacion = await e.ValidadorIndependiente.ValidarAsync(e.Documentos.PadesGuardado!);
        Assert.True(validacion.DocumentoValido);
        Assert.Equal(EstadoPolitica.NoCumple, validacion.Firmas.Single().ValidacionCertificado!.Politica!.Estado);
        Assert.Contains(validacion.Firmas.Single().Evidencia, l => l.Contains("solo informativa"));
    }

    // ---------------------------------------------------------------- mejoras best-effort: nunca abortan la firma

    [Fact]
    public async Task Si_la_TSA_no_responde_la_firma_igual_se_completa_como_PAdES_LT_sin_sello_de_archivo()
    {
        var e = Construir();
        e.Tsa.Caida = true;

        var resultado = await e.Handler.Handle(e.ComoLoHaceElFirmadorLocal(), CancellationToken.None);

        Assert.True(resultado.EsExitoso, resultado.Error);
        string texto = Latin1(e.Documentos.PadesGuardado!);
        Assert.Contains("/DSS", texto);
        Assert.DoesNotContain("/DocTimeStamp", texto);

        var validacion = await e.ValidadorIndependiente.ValidarAsync(e.Documentos.PadesGuardado!);
        Assert.True(validacion.DocumentoValido);
        Assert.Empty(validacion.SellosDeArchivo);
    }

    // ---------------------------------------------------------------- validación posterior

    /// <summary>La razón de ser de PAdES-LT: validar el documento guardado cuando la CA ya no responde.</summary>
    [Fact]
    public async Task Validacion_posterior_con_la_CA_caida_usa_el_DSS_embebido_y_sigue_siendo_valida()
    {
        var e = Construir();
        await e.Handler.Handle(e.ComoLoHaceElFirmadorLocal(), CancellationToken.None);
        e.Red.Caida = true; // la CRL/OCSP originales ya no están publicados

        var validacion = await e.ValidadorIndependiente.ValidarAsync(e.Documentos.PadesGuardado!);

        Assert.True(validacion.DocumentoValido, string.Join(" | ", validacion.Firmas.SelectMany(f => f.Evidencia)));
        Assert.Contains(validacion.Firmas.Single().Evidencia, l => l.Contains("material embebido", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Sin_DSS_y_con_la_CA_caida_el_mismo_documento_no_se_puede_validar()
    {
        // Control del test anterior: sin el DSS (PAdES-B/T) y sin red, "no revocado" es indemostrable — fail closed.
        var e = Construir();
        var comando = e.ComoLoHaceElFirmadorLocal();
        e.Red.Caida = true;

        var validacion = await e.ValidadorIndependiente.ValidarAsync(Convert.FromBase64String(comando.DocumentoPadesBase64!));

        Assert.False(validacion.DocumentoValido);
        Assert.Contains(validacion.Firmas.Single().Evidencia, l => l.Contains("Unavailable"));
    }

    [Fact]
    public async Task Control_un_DSS_armado_a_mano_con_una_CRL_genuina_de_la_CA_si_valida_sin_red()
    {
        // Mismo montaje que el test de la CRL forjada, pero con la CRL firmada por la CA real: demuestra que
        // el rechazo de aquel test se debe a la firma de la CRL y no a un defecto del montaje.
        var e = Construir();
        var comando = e.ComoLoHaceElFirmadorLocal();
        byte[] sinDss = Convert.FromBase64String(comando.DocumentoPadesBase64!);
        byte[] crlGenuina = CadenaDePruebaHelper.GenerarCrl(e.Intermedia, []);
        byte[] conDss = PdfDssWriter.AgregarDss(sinDss, PdfSignatureVerifier.VerificarUltima(sinDss).CmsDer!,
            new MaterialDss([e.Intermedia.CertificadoBc.GetEncoded(), e.CertificadoDer], crlGenuina, null));
        e.Red.Caida = true;

        var validacion = await e.ValidadorIndependiente.ValidarAsync(conDss);

        Assert.True(validacion.DocumentoValido, string.Join(" | ", validacion.Firmas.SelectMany(f => f.Evidencia)));
    }

    [Fact]
    public async Task Un_DSS_con_una_CRL_firmada_por_otra_llave_no_se_acepta()
    {
        var e = Construir();
        var comando = e.ComoLoHaceElFirmadorLocal();
        byte[] sinDss = Convert.FromBase64String(comando.DocumentoPadesBase64!);

        // El atacante fabrica una CRL "limpia" con el mismo emisor declarado, pero firmada con SU llave.
        var impostora = new NodoCadena(e.Intermedia.CertificadoBc, CadenaDePruebaHelper.GenerarLlaves());
        byte[] crlForjada = CadenaDePruebaHelper.GenerarCrl(impostora, []);
        var material = new MaterialDss(
            [e.Intermedia.CertificadoBc.GetEncoded(), e.CertificadoDer], crlForjada, OcspRespuestaDer: null);
        byte[] conDssForjado = PdfDssWriter.AgregarDss(sinDss, PdfSignatureVerifier.VerificarUltima(sinDss).CmsDer!, material);
        e.Red.Caida = true;

        var validacion = await e.ValidadorIndependiente.ValidarAsync(conDssForjado);

        Assert.False(validacion.DocumentoValido);
    }

    [Fact]
    public async Task Un_DSS_cuya_CRL_lista_al_certificado_como_revocado_gana_aunque_la_red_diga_que_esta_bien()
    {
        var e = Construir(); // la red (CRL/OCSP en vivo) dice "no revocado"
        var comando = e.ComoLoHaceElFirmadorLocal();
        byte[] sinDss = Convert.FromBase64String(comando.DocumentoPadesBase64!);

        var serial = new BigInteger(e.Hoja.ComoDotNet().SerialNumber, 16);
        byte[] crlConRevocado = CadenaDePruebaHelper.GenerarCrl(e.Intermedia, [serial]); // firmada por la CA real
        byte[] conDss = PdfDssWriter.AgregarDss(sinDss, PdfSignatureVerifier.VerificarUltima(sinDss).CmsDer!,
            new MaterialDss([e.Intermedia.CertificadoBc.GetEncoded(), e.CertificadoDer], crlConRevocado, null));

        var validacion = await e.ValidadorIndependiente.ValidarAsync(conDss);

        Assert.False(validacion.DocumentoValido);
        Assert.Contains(validacion.Firmas.Single().Evidencia, l => l.Contains("Revoked"));
    }

    [Fact]
    public async Task El_validador_independiente_detecta_una_alteracion_posterior_al_guardado()
    {
        var e = Construir();
        await e.Handler.Handle(e.ComoLoHaceElFirmadorLocal(), CancellationToken.None);
        var alterado = (byte[])e.Documentos.PadesGuardado!.Clone();
        alterado[60] ^= 0xFF;

        var validacion = await e.ValidadorIndependiente.ValidarAsync(alterado);

        Assert.False(validacion.DocumentoValido);
    }
}
