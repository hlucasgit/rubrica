namespace SecureSign.Signature.Domain;

/// <summary>
/// Clasificación legal según Ley N° 27269 (ver docs/03-legal-normativo/marco-legal-peru.md).
/// Solo Digital goza de presunción legal de autenticidad e integridad sin prueba adicional.
/// </summary>
public enum TipoFirma
{
    Simple,
    Avanzada,
    Digital
}

public enum EstadoSolicitudFirma
{
    Pendiente,
    Visualizado,
    ValidandoIdentidad,
    Firmado,
    Rechazado,
    Cancelado,
    Expirado
}

public enum EstadoFlujoFirma
{
    Pendiente,
    Notificado,
    Visualizado,
    Firmado,
    Rechazado
}
