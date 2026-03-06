namespace ClipPocketWin.Shared.ResultPattern;

/// <summary>
/// Rappresenta il risultato di un'operazione con un valore di ritorno
/// </summary>
public class Result<T>
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public T? Value { get; }
    public Error? Error { get; }

    private Result(bool isSuccess, T? value, Error? error)
    {
        if (isSuccess && error != null)
        {
            throw new InvalidOperationException("A successful result cannot have an error.");
        }

        if (!isSuccess && error == null)
        {
            throw new InvalidOperationException("A failed result must have an error.");
        }

        IsSuccess = isSuccess;
        Value = value;
        Error = error;
    }

    public static Result<T> Success(T value) => new(true, value, null);

    public static Result<T> Failure(Error error) => new(false, default, error);

    public static implicit operator Result<T>(T value) => Success(value);

    public static implicit operator Result<T>(Error error) => Failure(error);

    public TResult Match<TResult>(Func<T, TResult> onSuccess, Func<Error, TResult> onFailure)
    {
        return IsSuccess ? onSuccess(Value!) : onFailure(Error!);
    }

    /// <summary>
    /// Esegue un'azione se il risultato è un successo
    /// </summary>
    public Result<T> OnSuccess(Action<T> action)
    {
        if (IsSuccess && Value != null)
        {
            action(Value);
        }

        return this;
    }

    /// <summary>
    /// Esegue un'azione se il risultato è un fallimento
    /// </summary>
    public Result<T> OnFailure(Action<Error> action)
    {
        if (IsFailure && Error != null)
        {
            action(Error);
        }

        return this;
    }
}
