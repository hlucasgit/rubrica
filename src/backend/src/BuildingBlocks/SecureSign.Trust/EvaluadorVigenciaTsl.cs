namespace SecureSign.Trust;

public enum NivelAvisoTsl { Informacion, Advertencia, Error }

/// <param name="DebeAbortarElArranque">true solo con la lista vencida Y la opción de fallar activada.</param>
public sealed record EvaluacionVigenciaTsl(EstadoVigenciaTsl Estado, NivelAvisoTsl Nivel, string Mensaje, bool DebeAbortarElArranque);

/// <summary>
/// Decide qué hacer con la vigencia de la TSL (RUNBOOK.md 12.40): función pura, sin reloj ni registro propios,
/// para poder probar CADA rama —incluida "vencida", que en vivo no se puede provocar sin adelantar el reloj— y
/// para reutilizarla tanto en el arranque como en la comprobación periódica del servicio.
/// </summary>
public static class EvaluadorVigenciaTsl
{
    public static readonly TimeSpan AvisoPorDefecto = TimeSpan.FromDays(14);

    public static EvaluacionVigenciaTsl Evaluar(ListaConfianzaIofe lista, DateTimeOffset ahora, bool fallarSiVencida, TimeSpan? aviso = null)
    {
        var plazo = aviso ?? AvisoPorDefecto;
        var estado = lista.Vigencia(ahora, plazo);
        string descripcion = lista.DescribirVigencia(ahora, plazo);

        return estado switch
        {
            EstadoVigenciaTsl.Vencida => new(estado, NivelAvisoTsl.Error,
                $"{descripcion} Descarga la TSL oficial nueva y reemplaza ConfianzaIofe/tsl-pe.xml.", DebeAbortarElArranque: fallarSiVencida),
            EstadoVigenciaTsl.PorVencer => new(estado, NivelAvisoTsl.Advertencia,
                $"{descripcion} Descarga pronto la TSL oficial nueva.", false),
            EstadoVigenciaTsl.Desconocida => new(estado, NivelAvisoTsl.Advertencia,
                $"{descripcion} La TSL no declara NextUpdate: no se puede saber si está vigente.", false),
            _ => new(estado, NivelAvisoTsl.Informacion, descripcion, false),
        };
    }
}
