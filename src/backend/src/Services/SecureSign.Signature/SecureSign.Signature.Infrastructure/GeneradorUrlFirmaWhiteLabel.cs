using SecureSign.Signature.Application.CrearSolicitudFirma;

namespace SecureSign.Signature.Infrastructure;

/// <summary>
/// Implementación de referencia: en producción el dominio debe resolverse
/// consultando TenantBranding.DominioPersonalizado (ver docs/06-white-label);
/// aquí se usa el dominio genérico como fallback documentado.
/// </summary>
public sealed class GeneradorUrlFirmaWhiteLabel : IGeneradorUrlFirma
{
    public string Generar(Guid tenantId, string tokenAccesoUnico)
        => $"https://firma.securesign.pe/t/{tenantId}/f/{tokenAccesoUnico}";
}
