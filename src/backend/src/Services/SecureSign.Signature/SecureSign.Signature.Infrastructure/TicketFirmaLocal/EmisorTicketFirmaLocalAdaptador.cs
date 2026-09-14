using SecureSign.Shared.Auth;
using SecureSign.Signature.Application.TicketFirmaLocal;

namespace SecureSign.Signature.Infrastructure.TicketFirmaLocal;

/// <summary>Adaptador delgado sobre SecureSign.Shared.Auth.EmisorTicketFirmaLocal — ver IEmisorTicketFirmaLocal.</summary>
public sealed class EmisorTicketFirmaLocalAdaptador(EmisorTicketFirmaLocal emisor) : IEmisorTicketFirmaLocal
{
    public TicketFirmaLocalResponse Emitir(DatosParaEmitirTicket datos)
    {
        var resultado = emisor.Emitir(new DatosTicketFirmaLocal(
            datos.TenantId, datos.UsuarioId, datos.SolicitudFirmaId, datos.FlujoFirmaId,
            datos.DocumentoId, datos.DocumentoHashSha256Hex, datos.Origen));

        int expiraEnSegundos = (int)Math.Max(0, (resultado.ExpiraEn - DateTimeOffset.UtcNow).TotalSeconds);
        return new TicketFirmaLocalResponse(resultado.Jwt, expiraEnSegundos);
    }
}
