using System.Security.Cryptography;
using System.Text;
using SecureSign.Pades;
using static SecureSign.UnitTests.Pades.PdfDePruebaHelper;

namespace SecureSign.UnitTests.Pades;

/// <summary>Lectura del DSS que escribe <see cref="PdfDssWriter"/> — la otra mitad de PAdES-LT (RUNBOOK.md 12.33).</summary>
public sealed class PdfDssReaderTests
{
    private static byte[] Bytes(string semilla, int longitud)
    {
        byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes(semilla));
        byte[] r = new byte[longitud];
        for (int i = 0; i < longitud; i++) r[i] = hash[i % hash.Length];
        return r;
    }

    [Fact]
    public void Lo_escrito_es_exactamente_lo_leido()
    {
        using var firmante = CertificadoDePruebaHelper.GenerarFirmante("Firmante");
        byte[] pdf = FirmarConCertificadoDePrueba(CrearPdfMinimo("documento"), firmante);
        byte[] hoja = Bytes("hoja", 400), raiz = Bytes("raiz", 350), crl = Bytes("crl", 900), ocsp = Bytes("ocsp", 500);

        byte[] conDss = PdfDssWriter.AgregarDss(pdf, CmsDeLaUltimaFirma(pdf), new MaterialDss([hoja, raiz], crl, ocsp));
        var leido = PdfDssReader.Leer(conDss);

        Assert.NotNull(leido);
        Assert.Equal([hoja, raiz], leido!.CertificadosDer);
        Assert.Equal([crl], leido.CrlsDer);
        Assert.Equal([ocsp], leido.OcspsDer);
    }

    [Fact]
    public void Un_PDF_sin_DSS_devuelve_null()
    {
        using var firmante = CertificadoDePruebaHelper.GenerarFirmante("Firmante");
        Assert.Null(PdfDssReader.Leer(FirmarConCertificadoDePrueba(CrearPdfMinimo("documento"), firmante)));
    }

    [Fact]
    public void Con_dos_firmantes_se_lee_el_material_de_ambos()
    {
        byte[] pdf = CrearPdfMinimo("dos firmantes");
        using var a = CertificadoDePruebaHelper.GenerarFirmante("A");
        pdf = FirmarConCertificadoDePrueba(pdf, a);
        pdf = PdfDssWriter.AgregarDss(pdf, CmsDeLaUltimaFirma(pdf), new MaterialDss([Bytes("cert-A", 300)], Bytes("crl-A", 400), null));
        using var b = CertificadoDePruebaHelper.GenerarFirmante("B");
        pdf = FirmarConCertificadoDePrueba(pdf, b);
        pdf = PdfDssWriter.AgregarDss(pdf, CmsDeLaUltimaFirma(pdf), new MaterialDss([Bytes("cert-B", 300)], Bytes("crl-B", 400), null));

        var leido = PdfDssReader.Leer(pdf)!;

        Assert.Equal([Bytes("cert-A", 300), Bytes("cert-B", 300)], leido.CertificadosDer);
        Assert.Equal([Bytes("crl-A", 400), Bytes("crl-B", 400)], leido.CrlsDer);
    }

    [Fact]
    public void Bytes_binarios_arbitrarios_incluida_la_palabra_endobj_se_recuperan_intactos()
    {
        // El contenido es DER: podría contener por casualidad "endobj"/"endstream" — se corta por /Length, no buscando esas palabras.
        using var firmante = CertificadoDePruebaHelper.GenerarFirmante("Firmante");
        byte[] pdf = FirmarConCertificadoDePrueba(CrearPdfMinimo("documento"), firmante);
        byte[] conTrampa = [.. Bytes("previo", 100), .. Encoding.ASCII.GetBytes("endstream\nendobj\n"), .. Bytes("posterior", 100), 0x00, 0xFF, 0x0A];

        byte[] conDss = PdfDssWriter.AgregarDss(pdf, CmsDeLaUltimaFirma(pdf), new MaterialDss([conTrampa], null, null));

        Assert.Equal([conTrampa], PdfDssReader.Leer(conDss)!.CertificadosDer);
    }

    [Fact]
    public void Basura_o_PDF_ilegible_devuelve_null_sin_lanzar()
    {
        Assert.Null(PdfDssReader.Leer([]));
        Assert.Null(PdfDssReader.Leer(Encoding.ASCII.GetBytes("esto no es un pdf")));
        Assert.Null(PdfDssReader.Leer(Encoding.ASCII.GetBytes("%PDF-1.4\nstartxref\n99999\n%%EOF")));
    }
}
