using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Options;

namespace SecureSign.Shared.Auth;

/// <summary>
/// Datos que el ticket liga inequívocamente a UNA sola operación de firma —
/// ver informe de preauditoría INDECOPI/IOFE, sección 12 ("Seguridad del
/// Firmador Local"): "el ticket debe vincular tenantId, userId, solicitudId,
/// flujoId, documentId, documentHash, origin, issuedAt, expiresAt, nonce,
/// audience".
/// </summary>
public sealed record DatosTicketFirmaLocal(
    Guid TenantId,
    Guid UsuarioId,
    Guid SolicitudFirmaId,
    Guid FlujoFirmaId,
    Guid DocumentoId,
    string DocumentoHashSha256Hex,
    string? Origen);

public sealed record TicketFirmaLocalEmitido(string Jwt, string Nonce, DateTimeOffset ExpiraEn);

/// <summary>
/// Emisor del ticket de firma de un solo uso para el Firmador Local. Desde
/// RUNBOOK.md 12.21, ya no se auto-firma localmente — se le pide al Gateway
/// vía EmisorTokenInterno (único que tiene la llave privada). El Firmador
/// Local (un ejecutable distribuido públicamente a la máquina de cualquier
/// usuario) sigue sin necesitar verificar la firma de este JWT — solo lo
/// REENVÍA como credencial Bearer hacia el propio backend, que es quien
/// realmente valida la firma, la expiración y (en FirmarLocalHandler) que
/// los claims ligados coincidan con la operación que se está completando.
/// </summary>
public sealed class EmisorTicketFirmaLocal(EmisorTokenInterno emisor, IOptions<JwtOptions> opciones)
{
    public const string ClaimTipo = "ssg_tipo";
    public const string ValorTipoTicketFirmaLocal = "firmador-local-ticket";
    public const string ClaimSolicitudFirmaId = "ssg_solicitud_id";
    public const string ClaimFlujoFirmaId = "ssg_flujo_id";
    public const string ClaimDocumentoId = "ssg_documento_id";
    public const string ClaimDocumentoHash = "ssg_documento_hash";
    public const string ClaimOrigen = "ssg_origen";

    /// <summary>Vida deliberadamente corta (ver hallazgo P1 del informe de preauditoría) — solo el tiempo de completar una firma, nunca reutilizable como token general.</summary>
    public const int MinutosExpiracionPorDefecto = 2;

    private readonly JwtOptions _opciones = opciones.Value;

    public async Task<TicketFirmaLocalEmitido> Emitir(DatosTicketFirmaLocal datos, int? minutosExpiracion = null, CancellationToken ct = default)
    {
        var nonce = Guid.NewGuid().ToString("N");

        var claims = new Dictionary<string, string>
        {
            [ClaimsSecureSign.TenantId] = datos.TenantId.ToString(),
            [ClaimsSecureSign.UsuarioId] = datos.UsuarioId.ToString(),
            [ClaimsSecureSign.Scope] = "firmar:local",
            [JwtRegisteredClaimNames.Jti] = nonce,
            [ClaimTipo] = ValorTipoTicketFirmaLocal,
            [ClaimSolicitudFirmaId] = datos.SolicitudFirmaId.ToString(),
            [ClaimFlujoFirmaId] = datos.FlujoFirmaId.ToString(),
            [ClaimDocumentoId] = datos.DocumentoId.ToString(),
            [ClaimDocumentoHash] = datos.DocumentoHashSha256Hex,
        };
        if (!string.IsNullOrEmpty(datos.Origen))
            claims[ClaimOrigen] = datos.Origen;

        int minutos = minutosExpiracion ?? MinutosExpiracionPorDefecto;
        var resultado = await emisor.EmitirAsync(claims, _opciones.Audience, minutos, ct);
        var expira = DateTimeOffset.UtcNow.AddMinutes(minutos);
        return new TicketFirmaLocalEmitido(resultado.AccessToken, nonce, expira);
    }
}
