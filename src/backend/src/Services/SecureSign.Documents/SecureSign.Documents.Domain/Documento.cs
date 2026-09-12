using SecureSign.Documents.Domain.Events;
using SecureSign.Domain.Primitives;
using SecureSign.Domain.ValueObjects;

namespace SecureSign.Documents.Domain;

public enum EstadoDocumento
{
    Registrado,
    EnProceso,
    Firmado,
    Rechazado,
    Cancelado
}

/// <summary>
/// Aggregate root del documento. No conoce el flujo de firma (eso vive en
/// SecureSign.Signature.Domain) — solo custodia el contenido, su hash de
/// referencia y las transiciones de estado que le competen.
/// </summary>
public sealed class Documento : Entity
{
    public Guid TenantId { get; private set; }
    public string? CodigoExterno { get; private set; }
    public string NombreArchivo { get; private set; } = default!;
    public string TipoContenido { get; private set; } = default!;
    public HashDocumental Hash { get; private set; } = default!;
    public long TamanoBytes { get; private set; }
    public string UrlAlmacenamiento { get; private set; } = default!;
    public EstadoDocumento Estado { get; private set; }
    public Guid CreadoPor { get; private set; }
    public DateTimeOffset CreadoEn { get; private set; }

    private Documento() { }

    public static Result<Documento> Registrar(
        Guid tenantId,
        string nombreArchivo,
        string tipoContenido,
        byte[] contenido,
        string urlAlmacenamiento,
        Guid creadoPor,
        string? codigoExterno = null)
    {
        if (contenido.Length == 0)
            return Result.Fallido<Documento>("El documento no puede estar vacío.");

        if (contenido.LongLength > 50 * 1024 * 1024)
            return Result.Fallido<Documento>("El documento excede el tamaño máximo permitido (50 MB).");

        var documento = new Documento
        {
            TenantId = tenantId,
            CodigoExterno = codigoExterno,
            NombreArchivo = nombreArchivo,
            TipoContenido = tipoContenido,
            Hash = HashDocumental.CalcularDesde(contenido),
            TamanoBytes = contenido.LongLength,
            UrlAlmacenamiento = urlAlmacenamiento,
            Estado = EstadoDocumento.Registrado,
            CreadoPor = creadoPor,
            CreadoEn = DateTimeOffset.UtcNow
        };

        documento.RaiseDomainEvent(new DocumentoRegistradoEvent(tenantId, documento.Id, documento.Hash.ValorHex, DateTimeOffset.UtcNow));
        return Result.Exitoso(documento);
    }

    /// <summary>
    /// Revalida que el contenido actual siga correspondiendo al hash registrado.
    /// Es el "gate" técnico descrito como innovación #2 (motor de validación
    /// pre-firma): debe ejecutarse inmediatamente antes de habilitar la firma.
    /// </summary>
    public Result ValidarIntegridadPreFirma(byte[] contenidoActual)
    {
        var hashActual = HashDocumental.CalcularDesde(contenidoActual);
        return hashActual.CoincideCon(Hash)
            ? Result.Exitoso()
            : Result.Fallido("El documento fue modificado después de su registro. Firma bloqueada.");
    }

    public Result MarcarComoFirmado()
    {
        if (Estado is EstadoDocumento.Cancelado or EstadoDocumento.Rechazado)
            return Result.Fallido($"No se puede firmar un documento en estado {Estado}.");

        Estado = EstadoDocumento.Firmado;
        return Result.Exitoso();
    }

    public void MarcarEnProceso() => Estado = EstadoDocumento.EnProceso;
    public void MarcarRechazado() => Estado = EstadoDocumento.Rechazado;
    public void MarcarCancelado() => Estado = EstadoDocumento.Cancelado;
}
