using SecureSign.Signature.Application.ValidarPades;
using SecureSign.Trust;
using SecureSign.Validator;

namespace SecureSign.Signature.Infrastructure.ValidarPades;

/// <summary>Adaptador delgado sobre SecureSign.Validator.ValidadorDocumentoPades — ver IValidadorDocumentoPadesIndependiente.</summary>
public sealed class ValidadorDocumentoPadesIndependiente(ValidadorDocumentoPades validador) : IValidadorDocumentoPadesIndependiente
{
    public async Task<ResultadoValidarPadesResponse> ValidarAsync(byte[] pdf, CancellationToken ct = default)
    {
        var resultado = await validador.ValidarAsync(pdf, ct);
        return new ResultadoValidarPadesResponse(
            resultado.TotalFirmas,
            resultado.DocumentoValido,
            resultado.Firmas.Select(Mapear).ToList(),
            resultado.SellosDeArchivo.Select(s => new SelloArchivoDto(s.Valido, s.GenTime, s.AutoridadTsa, s.Error)).ToList());
    }

    private static FirmaValidadaDto Mapear(ResultadoValidacionFirmaPades f)
    {
        var vc = f.ValidacionCertificado;
        return new FirmaValidadaDto(
            NombreFirmante: f.NombreFirma,
            FirmaCriptograficaValida: f.FirmaCriptograficaValida,
            CertificadoSujeto: f.Certificado?.Subject,
            CertificadoEmisor: f.Certificado?.Issuer,
            CertificadoVigenteDesde: f.Certificado is null ? null : new DateTimeOffset(f.Certificado.NotBefore),
            CertificadoVigenteHasta: f.Certificado is null ? null : new DateTimeOffset(f.Certificado.NotAfter),
            InstanteFirmaDeclarado: f.InstanteFirmaDeclarado,
            InstanteFirmaConfiable: f.InstanteFirmaConfiable,
            InstanteSelloTiempo: f.InstanteSelloTiempo,
            SelloTiempoAutoridad: f.SelloTiempoAutoridad,
            CertificadoVigente: vc?.CertificadoVigente ?? false,
            CadenaValida: vc?.CadenaValida ?? false,
            RaizConfiableIofe: vc?.RaizConfiableIofe ?? false,
            PropositoValido: vc?.PropositoValido ?? false,
            EstadoRevocacionOcsp: vc?.Revocacion.Ocsp.ToString() ?? "Unavailable",
            EstadoRevocacionCrl: vc?.Revocacion.Crl.ToString() ?? "Unavailable",
            EstadoRevocacionCombinado: vc?.Revocacion.Combinado.ToString() ?? "Unavailable",
            EstadoFinal: f.EstadoFinal,
            Evidencia: f.Evidencia,
            Error: f.Error,
            ExtendedKeyUsages: vc?.Politica?.ExtendedKeyUsages ?? [],
            PoliticasCertificado: vc?.Politica?.PoliticasCertificado ?? [],
            EstadoPolitica: (vc?.Politica?.Estado ?? EstadoPolitica.SoloInformativa).ToString());
    }
}
