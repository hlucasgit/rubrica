using MediatR;
using SecureSign.Domain.Primitives;

namespace SecureSign.Identity.Application.RegistrarSenal;

public enum TipoSenalIdentidad
{
    ValidacionExitosa,
    CertificadoEmitido,
    CertificadoRevocado,
    CuentaInstitucional,
    RechazoPorSospechaFraude
}

/// <summary>
/// Registra una señal que modifica el perfil de confianza de un usuario.
/// En producción esto lo invocarían el Servicio de Certificados (al emitir/
/// revocar un certificado), el flujo de OTP/biometría al validar
/// exitosamente, o el motor antifraude (innovación #3). En este scaffold,
/// sin esas integraciones reales, se expone como endpoint interno para que
/// el efecto sobre el Índice de Confianza Digital sea end-to-end verificable
/// — ver docs/02-innovacion-patente, innovación #5.
/// </summary>
public sealed record RegistrarSenalCommand(Guid TenantId, Guid UsuarioId, TipoSenalIdentidad TipoSenal) : IRequest<Result<int>>;
