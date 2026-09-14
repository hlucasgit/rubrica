using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

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
/// Emisor del ticket de firma de un solo uso para el Firmador Local. Ver
/// RUNBOOK.md 12.13 sobre por qué es SEGURO reutilizar aquí la misma llave
/// HS256 (JwtOptions) que el resto de la plataforma: el Firmador Local (un
/// ejecutable distribuido públicamente a la máquina de cualquier usuario)
/// NUNCA necesita verificar la firma de este JWT — solo lo REENVÍA como
/// credencial Bearer hacia el propio backend, que es quien realmente valida
/// la firma, la expiración y (en FirmarLocalHandler) que los claims ligados
/// coincidan con la operación que se está completando. La llave privada de
/// firma, por tanto, nunca sale del servidor — igual que cualquier otro
/// token de la plataforma.
/// </summary>
public sealed class EmisorTicketFirmaLocal(IOptions<JwtOptions> opciones)
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

    public TicketFirmaLocalEmitido Emitir(DatosTicketFirmaLocal datos, int? minutosExpiracion = null)
    {
        var llave = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opciones.SigningKey));
        var credenciales = new SigningCredentials(llave, SecurityAlgorithms.HmacSha256);
        var nonce = Guid.NewGuid().ToString("N");

        var claims = new List<Claim>
        {
            new(ClaimsSecureSign.TenantId, datos.TenantId.ToString()),
            new(ClaimsSecureSign.UsuarioId, datos.UsuarioId.ToString()),
            new(ClaimsSecureSign.Scope, "firmar:local"),
            new(JwtRegisteredClaimNames.Jti, nonce),
            new(ClaimTipo, ValorTipoTicketFirmaLocal),
            new(ClaimSolicitudFirmaId, datos.SolicitudFirmaId.ToString()),
            new(ClaimFlujoFirmaId, datos.FlujoFirmaId.ToString()),
            new(ClaimDocumentoId, datos.DocumentoId.ToString()),
            new(ClaimDocumentoHash, datos.DocumentoHashSha256Hex),
        };
        if (!string.IsNullOrEmpty(datos.Origen))
            claims.Add(new Claim(ClaimOrigen, datos.Origen));

        var expira = DateTime.UtcNow.AddMinutes(minutosExpiracion ?? MinutosExpiracionPorDefecto);

        var token = new JwtSecurityToken(
            issuer: _opciones.Issuer,
            audience: _opciones.Audience,
            claims: claims,
            expires: expira,
            signingCredentials: credenciales);

        var jwt = new JwtSecurityTokenHandler().WriteToken(token);
        return new TicketFirmaLocalEmitido(jwt, nonce, expira);
    }
}
