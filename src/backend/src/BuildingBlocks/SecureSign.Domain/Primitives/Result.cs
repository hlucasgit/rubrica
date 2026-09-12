namespace SecureSign.Domain.Primitives;

public class Result
{
    public bool EsExitoso { get; }
    public string? Error { get; }

    protected Result(bool esExitoso, string? error)
    {
        if (esExitoso && error is not null)
            throw new InvalidOperationException("Un resultado exitoso no puede tener error.");
        if (!esExitoso && error is null)
            throw new InvalidOperationException("Un resultado fallido debe tener un error.");

        EsExitoso = esExitoso;
        Error = error;
    }

    public static Result Exitoso() => new(true, null);
    public static Result Fallido(string error) => new(false, error);
    public static Result<T> Exitoso<T>(T valor) => new(valor, true, null);
    public static Result<T> Fallido<T>(string error) => new(default, false, error);
}

public class Result<T> : Result
{
    private readonly T? _valor;

    protected internal Result(T? valor, bool esExitoso, string? error) : base(esExitoso, error)
    {
        _valor = valor;
    }

    public T Valor => EsExitoso
        ? _valor!
        : throw new InvalidOperationException("No se puede acceder al valor de un resultado fallido.");
}
