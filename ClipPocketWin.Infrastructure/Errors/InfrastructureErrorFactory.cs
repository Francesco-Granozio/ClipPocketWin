using ClipPocketWin.Shared.ResultPattern;
using System.Security.Cryptography;
using System.Text.Json;

namespace ClipPocketWin.Infrastructure.Errors;

internal static class InfrastructureErrorFactory
{
    public static Error FromException(Exception exception, string context, ErrorCode fallbackCode)
    {
        return exception switch
        {
            UnauthorizedAccessException => new Error(ErrorCode.StorageAccessDenied, context, exception),
            DirectoryNotFoundException => new Error(ErrorCode.StoragePathUnavailable, context, exception),
            PathTooLongException => new Error(ErrorCode.StoragePathUnavailable, context, exception),
            JsonException => new Error(ErrorCode.DeserializationFailed, context, exception),
            CryptographicException => new Error(fallbackCode, context, exception),
            IOException => new Error(fallbackCode, context, exception),
            _ => new Error(ErrorCode.UnknownError, context, exception)
        };
    }
}
