using System.Text;
using SecureSign.Documents.Domain;
using Xunit;

namespace SecureSign.UnitTests.Documents;

public class DocumentoTests
{
    [Fact]
    public void Registrar_calcula_el_hash_sha256_del_contenido()
    {
        var contenido = Encoding.UTF8.GetBytes("contrato de prueba");

        var resultado = Documento.Registrar(Guid.NewGuid(), "contrato.pdf", "application/pdf", contenido, "s3://bucket/x", Guid.NewGuid());

        Assert.True(resultado.EsExitoso);
        Assert.Equal(64, resultado.Valor.Hash.ValorHex.Length);
    }

    [Fact]
    public void Registrar_documento_vacio_falla()
    {
        var resultado = Documento.Registrar(Guid.NewGuid(), "vacio.pdf", "application/pdf", Array.Empty<byte>(), "s3://bucket/x", Guid.NewGuid());

        Assert.False(resultado.EsExitoso);
    }

    [Fact]
    public void ValidarIntegridadPreFirma_falla_si_el_contenido_fue_modificado()
    {
        var original = Encoding.UTF8.GetBytes("contrato original");
        var documento = Documento.Registrar(Guid.NewGuid(), "contrato.pdf", "application/pdf", original, "s3://bucket/x", Guid.NewGuid()).Valor;

        var modificado = Encoding.UTF8.GetBytes("contrato modificado despues de firmar el hash");
        var resultado = documento.ValidarIntegridadPreFirma(modificado);

        Assert.False(resultado.EsExitoso);
    }

    [Fact]
    public void ValidarIntegridadPreFirma_pasa_si_el_contenido_no_cambio()
    {
        var contenido = Encoding.UTF8.GetBytes("contrato sin cambios");
        var documento = Documento.Registrar(Guid.NewGuid(), "contrato.pdf", "application/pdf", contenido, "s3://bucket/x", Guid.NewGuid()).Valor;

        var resultado = documento.ValidarIntegridadPreFirma(contenido);

        Assert.True(resultado.EsExitoso);
    }
}
