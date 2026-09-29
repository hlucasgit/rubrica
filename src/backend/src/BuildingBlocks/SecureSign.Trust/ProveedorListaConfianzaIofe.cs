namespace SecureSign.Trust;

/// <summary>
/// Indirección para poder reemplazar la TSL EN CALIENTE dentro de un servicio ya corriendo (RUNBOOK.md 12.57,
/// informe de preauditoría INDECOPI/IOFE, hallazgo P2-01 — cierra el límite que dejó abierto RUNBOOK 12.52:
/// "hoy ese objeto es un singleton pasado por referencia directa a varios consumidores... sustituirlo en
/// caliente exige una capa de indirección"). Esta es esa capa.
///
/// <see cref="ListaConfianzaIofe"/> en sí sigue siendo inmutable (igual que antes) — lo que cambia es que nadie
/// de larga vida guarda una referencia fija a una instancia: todos leen <see cref="Actual"/> en el momento que
/// la necesitan. Un `Interlocked.Exchange` sobre una referencia es atómico a nivel del CLR — no hace falta un
/// `lock`: una lectura concurrente con un `Reemplazar` ve la lista vieja completa o la nueva completa, nunca un
/// estado a medias.
/// </summary>
public interface IProveedorListaConfianzaIofe
{
    /// <summary>La TSL vigente en este instante — se resuelve de nuevo en cada llamada, nunca se cachea.</summary>
    ListaConfianzaIofe Actual { get; }

    /// <summary>Reemplaza la TSL vigente de forma atómica. El llamador es responsable de haberla verificado antes (ver <see cref="ActualizadorTsl"/>) — esta clase no verifica nada, solo publica la referencia.</summary>
    void Reemplazar(ListaConfianzaIofe nueva);
}

public sealed class ProveedorListaConfianzaIofe(ListaConfianzaIofe inicial) : IProveedorListaConfianzaIofe
{
    private ListaConfianzaIofe _actual = inicial;

    public ListaConfianzaIofe Actual => Volatile.Read(ref _actual);

    public void Reemplazar(ListaConfianzaIofe nueva) => Interlocked.Exchange(ref _actual, nueva);
}
