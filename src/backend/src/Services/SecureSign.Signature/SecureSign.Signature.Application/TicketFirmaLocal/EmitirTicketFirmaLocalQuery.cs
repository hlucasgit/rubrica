using MediatR;
using SecureSign.Domain.Primitives;
using SecureSign.Signature.Application.Clients;
using SecureSign.Signature.Domain;

namespace SecureSign.Signature.Application.TicketFirmaLocal;

/// <summary>Datos planos del ticket — ver SecureSign.Shared.Auth.DatosTicketFirmaLocal (la Aplicación no depende de Shared.Auth directamente).</summary>
public sealed record DatosParaEmitirTicket(
    Guid TenantId,
    Guid UsuarioId,
    Guid SolicitudFirmaId,
    Guid FlujoFirmaId,
    Guid DocumentoId,
    string DocumentoHashSha256Hex,
    string? Origen);

public sealed record TicketFirmaLocalResponse(string Ticket, int ExpiraEnSegundos);

/// <summary>
/// Puerto hacia el emisor real del ticket (Infraestructura, sobre
/// SecureSign.Shared.Auth.EmisorTicketFirmaLocal) — mismo patrón que
/// IValidadorConfianzaFirmante/IValidadorDocumentoPadesIndependiente.
/// </summary>
public interface IEmisorTicketFirmaLocal
{
    TicketFirmaLocalResponse Emitir(DatosParaEmitirTicket datos);
}

/// <summary>
/// Emite el ticket de firma de un solo uso que el navegador debe entregarle
/// al Firmador Local en vez de un gatewayUrl + accessToken reusable — ver
/// informe de preauditoría INDECOPI/IOFE, sección 12, y RUNBOOK.md 12.13.
///
/// El firmante NO se toma del token que hace esta llamada (que en el caso
/// real de negocio suele ser un token de <em>integrador</em> B2B —
/// client_credentials, sin usuario, ver AuthController — que orquesta en
/// nombre de sus propios usuarios firmantes) sino del flujo mismo
/// (<c>FlujoFirma.FirmanteUsuarioId</c>), que ya es la fuente de verdad de
/// quién debe firmar esta operación.
/// </summary>
public sealed record EmitirTicketFirmaLocalQuery(
    Guid TenantId,
    Guid SolicitudFirmaId,
    Guid FlujoFirmaId,
    string? Origen) : IRequest<Result<TicketFirmaLocalResponse>>;

public sealed class EmitirTicketFirmaLocalHandler(
    ISolicitudFirmaRepository repositorio,
    IDocumentosServiceClient documentos,
    IEmisorTicketFirmaLocal emisor)
    : IRequestHandler<EmitirTicketFirmaLocalQuery, Result<TicketFirmaLocalResponse>>
{
    public async Task<Result<TicketFirmaLocalResponse>> Handle(EmitirTicketFirmaLocalQuery request, CancellationToken ct)
    {
        var solicitud = await repositorio.ObtenerPorIdAsync(request.TenantId, request.SolicitudFirmaId, ct);
        if (solicitud is null) return Result.Fallido<TicketFirmaLocalResponse>("Solicitud de firma no encontrada.");

        var flujo = solicitud.Flujos.FirstOrDefault(f => f.Id == request.FlujoFirmaId);
        if (flujo is null) return Result.Fallido<TicketFirmaLocalResponse>("Flujo de firma no encontrado.");

        var documento = await documentos.ObtenerAsync(solicitud.DocumentoId, ct);
        if (documento is null) return Result.Fallido<TicketFirmaLocalResponse>("El documento asociado ya no existe.");

        var datos = new DatosParaEmitirTicket(
            request.TenantId, flujo.FirmanteUsuarioId, request.SolicitudFirmaId, request.FlujoFirmaId,
            solicitud.DocumentoId, documento.HashSha256, request.Origen);

        return Result.Exitoso(emisor.Emitir(datos));
    }
}
