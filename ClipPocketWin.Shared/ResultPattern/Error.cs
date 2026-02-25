namespace ClipPocketWin.Shared.ResultPattern;

public record Error
{
    public ErrorCode Code { get; init; }
    public string Message { get; init; }
    public Exception? Exception { get; init; }

    public Error(ErrorCode code, string message, Exception? exception = null)
    {
        Code = code;
        Message = message;
        Exception = exception;
    }

    public static Error FromException(Exception exception, ErrorCode code = ErrorCode.UnknownError)
    {
        return new Error(code, exception.Message, exception);
    }

    public static Error NotFound(string message)
    {
        return new Error(ErrorCode.NotFound, message);
    }

    public static Error AlreadyExists(string message)
    {
        return new Error(ErrorCode.AlreadyExists, message);
    }

    public static Error Unauthorized(string message)
    {
        return new Error(ErrorCode.UnauthorizedAccess, message);
    }

    public static Error InvalidOperation(string message)
    {
        return new Error(ErrorCode.InvalidOperation, message);
    }

    public static Error Validation(string message)
    {
        return new Error(ErrorCode.ValidationError, message);
    }

    public static Error Canceled(string message)
    {
        return new Error(ErrorCode.Canceled, message);
    }
}
