namespace SecureSign.Trust;

/// <summary>Fotografía de la última vez que se comprobó la TSL — lo que expone el health check (RUNBOOK.md 12.54).</summary>
public sealed record FotografiaSaludTsl(
    bool Cargada,
    DateTimeOffset? EmitidaEn,
    DateTimeOffset? ProximaActualizacion,
    EstadoVigenciaTsl? Vigencia,
    DateTimeOffset? UltimaComprobacion,
    EstadoRevocacion? RevocacionDelFirmante,
    DateTimeOffset? UltimaComprobacionDeRevocacion);

/// <summary>
/// Estado mutable, en memoria y con thread-safety mínima (un <c>lock</c>), que <c>VigilanteVigenciaTsl</c>
/// actualiza cada vez que re-evalúa la TSL (RUNBOOK.md 12.40) y que un endpoint de salud lee para responder sin
/// tener que re-evaluar nada — informe de preauditoría INDECOPI/IOFE, hallazgo P2-02: "un monitor externo puede
/// alertar antes de NextUpdate" necesita un endpoint que no dependa de que alguien mire el registro del
/// servicio. Deliberadamente NO guarda nada sensible (ni certificados, ni URLs de CRL/OCSP, ni secretos) — solo
/// las fechas y los estados ya públicos que la propia TSL declara.
/// </summary>
public sealed class EstadoSaludTsl
{
    private readonly object _bloqueo = new();
    private bool _cargada;
    private DateTimeOffset? _emitidaEn;
    private DateTimeOffset? _proximaActualizacion;
    private EstadoVigenciaTsl? _vigencia;
    private DateTimeOffset? _ultimaComprobacion;
    private EstadoRevocacion? _revocacionDelFirmante;
    private DateTimeOffset? _ultimaComprobacionDeRevocacion;

    public void RegistrarVigencia(ListaConfianzaIofe lista, EstadoVigenciaTsl vigencia, DateTimeOffset ahora)
    {
        lock (_bloqueo)
        {
            _cargada = true;
            _emitidaEn = lista.EmitidaEn;
            _proximaActualizacion = lista.ProximaActualizacion;
            _vigencia = vigencia;
            _ultimaComprobacion = ahora;
        }
    }

    public void RegistrarRevocacionDelFirmante(EstadoRevocacion estado, DateTimeOffset ahora)
    {
        lock (_bloqueo)
        {
            _revocacionDelFirmante = estado;
            _ultimaComprobacionDeRevocacion = ahora;
        }
    }

    public FotografiaSaludTsl Leer()
    {
        lock (_bloqueo)
        {
            return new FotografiaSaludTsl(_cargada, _emitidaEn, _proximaActualizacion, _vigencia, _ultimaComprobacion,
                _revocacionDelFirmante, _ultimaComprobacionDeRevocacion);
        }
    }
}
