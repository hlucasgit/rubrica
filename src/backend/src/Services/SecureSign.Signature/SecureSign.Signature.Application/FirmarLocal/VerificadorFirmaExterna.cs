using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace SecureSign.Signature.Application.FirmarLocal;

/// <summary>
/// Verifica una firma calculada FUERA de este sistema (por el Firmador Local,
/// ver docs/RUNBOOK.md sección 12) contra el hash del documento y el
/// certificado que el propio firmante envía junto con la firma.
///
/// Esta es la pieza clave de la arquitectura de "firma con hash desacoplado":
/// el PIN de la tarjeta del firmante NUNCA sale de su máquina, ni siquiera
/// hacia nuestro propio Servicio Criptográfico — solo el resultado (firma +
/// certificado público) viaja por la red. Verificar una firma con la llave
/// PÚBLICA de un certificado es una operación estándar de .NET que no
/// requiere PKCS#11 ni ningún módulo criptográfico especial.
///
/// SIMPLIFICACIÓN: no se valida aquí la cadena de certificación (CRL/OCSP)
/// ni que el certificado pertenezca realmente a una Entidad de Certificación
/// acreditada en el marco de la IOFE — solo se verifica la operación
/// criptográfica (que la firma corresponde matemáticamente a ese hash y esa
/// llave pública). Ver limitación equivalente en ProveedorCriptograficoPkcs11.
/// </summary>
public static class VerificadorFirmaExterna
{
    public static bool Verificar(byte[] hashDocumento, byte[] firma, byte[] certificadoDer)
    {
        using var certificado = new X509Certificate2(certificadoDer);

        using var rsa = certificado.GetRSAPublicKey();
        if (rsa is not null)
            return rsa.VerifyHash(hashDocumento, firma, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        using var ecdsa = certificado.GetECDsaPublicKey();
        return ecdsa is not null && ecdsa.VerifyHash(hashDocumento, firma);
    }
}
