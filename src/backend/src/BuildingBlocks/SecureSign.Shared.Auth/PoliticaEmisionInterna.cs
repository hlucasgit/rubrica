using System.IdentityModel.Tokens.Jwt;

namespace SecureSign.Shared.Auth;

/// <summary>
/// Política que el Gateway aplica a CADA petición de <c>POST /api/auth/interno/emitir</c>
/// después de autenticar al servicio que la hace. Sin ella, quien tuviera un
/// secreto interno podía pedir la firma de cualquier conjunto de claims — p. ej.
/// un token de audiencia externa con todos los scopes para cualquier tenant —,
/// o sea que comprometer UN servicio equivalía a comprometer la autorización
/// de toda la plataforma. Ahora cada servicio solo obtiene los DOS tipos de
/// token que legítimamente necesita, con claims de una lista cerrada (RUNBOOK.md 12.28):
///
/// 1. Intercambio interno (RFC 8693, ver TokenExchangeService): audiencia
///    interna, scope <c>internal-service</c>, <c>act</c> igual al servicio
///    autenticado (no puede hacerse pasar por otro), vida ≤ la interna.
/// 2. Ticket del Firmador Local (ver EmisorTicketFirmaLocal): solo servicios
///    marcados <see cref="ServicioEmisorOpciones.PuedeEmitirTickets"/>,
///    audiencia externa, scope <c>firmar:local</c>, vida ≤ 5 minutos.
///
/// Cualquier claim fuera de la lista del tipo correspondiente se rechaza en
/// vez de ignorarse: un claim inesperado es una señal, no ruido.
/// </summary>
public static class PoliticaEmisionInterna
{
    public const string ScopeIntercambio = "internal-service";
    public const string ScopeTicketFirmaLocal = "firmar:local";
    public const int MinutosMaximosTicket = 5;

    private static readonly HashSet<string> ClaimsIntercambio = new(StringComparer.Ordinal)
    {
        ClaimsSecureSign.TenantId, ClaimsSecureSign.UsuarioId, ClaimsSecureSign.Scope, "act",
    };

    private static readonly HashSet<string> ClaimsTicket = new(StringComparer.Ordinal)
    {
        ClaimsSecureSign.TenantId, ClaimsSecureSign.UsuarioId, ClaimsSecureSign.Scope,
        JwtRegisteredClaimNames.Jti,
        EmisorTicketFirmaLocal.ClaimTipo, EmisorTicketFirmaLocal.ClaimSolicitudFirmaId,
        EmisorTicketFirmaLocal.ClaimFlujoFirmaId, EmisorTicketFirmaLocal.ClaimDocumentoId,
        EmisorTicketFirmaLocal.ClaimDocumentoHash, EmisorTicketFirmaLocal.ClaimOrigen,
    };

    /// <returns><c>null</c> si la petición cumple la política; si no, el motivo del rechazo.</returns>
    public static string? Evaluar(
        ServicioEmisorOpciones servicio, IReadOnlyDictionary<string, string> claims,
        string audiencia, int minutosExpiracion, JwtOptions opciones)
    {
        if (claims.Count == 0 || string.IsNullOrWhiteSpace(audiencia) || minutosExpiracion <= 0)
            return "Se requieren claims, audiencia y minutosExpiracion (> 0).";

        bool esTicket = claims.ContainsKey(EmisorTicketFirmaLocal.ClaimTipo);
        return esTicket
            ? EvaluarTicket(servicio, claims, audiencia, minutosExpiracion, opciones)
            : EvaluarIntercambio(servicio, claims, audiencia, minutosExpiracion, opciones);
    }

    private static string? EvaluarIntercambio(
        ServicioEmisorOpciones servicio, IReadOnlyDictionary<string, string> claims,
        string audiencia, int minutos, JwtOptions opciones)
    {
        if (audiencia != opciones.InternalAudience)
            return "Un token de intercambio solo puede llevar la audiencia interna.";
        if (minutos > opciones.MinutosExpiracionInterno)
            return $"Un token de intercambio no puede durar más de {opciones.MinutosExpiracionInterno} minutos.";
        if (claims.Keys.FirstOrDefault(k => !ClaimsIntercambio.Contains(k)) is { } inesperado)
            return $"Claim no permitido en un token de intercambio: '{inesperado}'.";
        if (!claims.TryGetValue(ClaimsSecureSign.Scope, out var scope) || scope != ScopeIntercambio)
            return $"Un token de intercambio solo puede llevar scope '{ScopeIntercambio}'.";
        if (!claims.TryGetValue("act", out var actor) || actor != servicio.Nombre)
            return "El claim 'act' debe ser el servicio autenticado.";
        return null;
    }

    private static string? EvaluarTicket(
        ServicioEmisorOpciones servicio, IReadOnlyDictionary<string, string> claims,
        string audiencia, int minutos, JwtOptions opciones)
    {
        if (!servicio.PuedeEmitirTickets)
            return "Este servicio no está autorizado a emitir tickets del Firmador Local.";
        if (audiencia != opciones.Audience)
            return "Un ticket del Firmador Local solo puede llevar la audiencia externa.";
        if (minutos > MinutosMaximosTicket)
            return $"Un ticket del Firmador Local no puede durar más de {MinutosMaximosTicket} minutos.";
        if (claims.Keys.FirstOrDefault(k => !ClaimsTicket.Contains(k)) is { } inesperado)
            return $"Claim no permitido en un ticket: '{inesperado}'.";
        if (claims[EmisorTicketFirmaLocal.ClaimTipo] != EmisorTicketFirmaLocal.ValorTipoTicketFirmaLocal)
            return "Valor de ssg_tipo inválido.";
        if (!claims.TryGetValue(ClaimsSecureSign.Scope, out var scope) || scope != ScopeTicketFirmaLocal)
            return $"Un ticket solo puede llevar scope '{ScopeTicketFirmaLocal}'.";
        return null;
    }
}
