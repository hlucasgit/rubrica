using SecureSign.Evidence.Domain;
using Xunit;

namespace SecureSign.UnitTests.Evidence;

public class AgregadorMerkleTests
{
    [Fact]
    public void Raiz_es_deterministica_para_el_mismo_conjunto_de_hashes()
    {
        var hashes = new[] { "aa".PadRight(64, '0'), "bb".PadRight(64, '0'), "cc".PadRight(64, '0') };

        var raiz1 = AgregadorMerkle.CalcularRaiz(hashes);
        var raiz2 = AgregadorMerkle.CalcularRaiz(hashes);

        Assert.Equal(raiz1, raiz2);
    }

    [Fact]
    public void Raiz_cambia_si_un_solo_hash_del_lote_cambia()
    {
        var hashesOriginal = new[] { "aa".PadRight(64, '0'), "bb".PadRight(64, '0'), "cc".PadRight(64, '0') };
        var hashesAlterado = new[] { "aa".PadRight(64, '0'), "bb".PadRight(64, '1'), "cc".PadRight(64, '0') };

        var raizOriginal = AgregadorMerkle.CalcularRaiz(hashesOriginal);
        var raizAlterada = AgregadorMerkle.CalcularRaiz(hashesAlterado);

        Assert.NotEqual(raizOriginal, raizAlterada);
    }

    [Fact]
    public void Maneja_lotes_de_tamano_impar_duplicando_el_ultimo_elemento()
    {
        var hashes = new[] { "aa".PadRight(64, '0'), "bb".PadRight(64, '0'), "cc".PadRight(64, '0') };

        var raiz = AgregadorMerkle.CalcularRaiz(hashes);

        Assert.NotNull(raiz);
        Assert.Equal(64, raiz.Length);
    }
}
