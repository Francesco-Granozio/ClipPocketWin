namespace ClipPocketWin.Shared.ResultPattern;

/// <summary>
/// Rappresenta il risultato di un'operazione senza valore di ritorno (void operations)
/// </summary>
public class Result
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error? Error { get; }

    private Result(bool isSuccess, Error? error)
    {
        if (isSuccess && error != null)
            throw new InvalidOperationException("A successful result cannot have an error.");
        if (!isSuccess && error == null)
            throw new InvalidOperationException("A failed result must have an error.");

        IsSuccess = isSuccess;
        Error = error;
    }

    public static Result Success() => new(true, null);

    public static Result Failure(Error error) => new(false, error);

    public static implicit operator Result(Error error) => Failure(error);

    public TResult Match<TResult>(Func<TResult> onSuccess, Func<Error, TResult> onFailure)
    {
        return IsSuccess ? onSuccess() : onFailure(Error!);
    }

    /// <summary>
    /// Esegue un'azione se il risultato è un successo
    /// </summary>
    public Result OnSuccess(Action action)
    {
        if (IsSuccess)
            action();
        return this;
    }

    /// <summary>
    /// Esegue un'azione se il risultato è un fallimento
    /// </summary>
    public Result OnFailure(Action<Error> action)
    {
        if (IsFailure && Error != null)
            action(Error);
        return this;
    }
}

