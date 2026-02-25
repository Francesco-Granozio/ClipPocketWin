using ClipPocketWin.Domain.Abstractions;
using ClipPocketWin.Domain.Models;
using ClipPocketWin.Infrastructure.Errors;
using ClipPocketWin.Shared.ResultPattern;
using System.Text.Json;

namespace ClipPocketWin.Infrastructure.Persistence;

public sealed class FileSettingsRepository : ISettingsRepository
{
    public async Task<Result<ClipPocketSettings>> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(StoragePaths.SettingsJson))
            {
                return Result<ClipPocketSettings>.Success(new ClipPocketSettings());
            }

            byte[] data = await File.ReadAllBytesAsync(StoragePaths.SettingsJson, cancellationToken);
            if (data.Length == 0)
            {
                return Result<ClipPocketSettings>.Success(new ClipPocketSettings());
            }

            ClipPocketSettings settings = JsonSerializer.Deserialize<ClipPocketSettings>(data, JsonSerialization.Options) ?? new ClipPocketSettings();
            return Result<ClipPocketSettings>.Success(settings);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            string context = "Failed to load application settings.";
            ErrorCode fallback = exception is JsonException ? ErrorCode.DeserializationFailed : ErrorCode.StorageReadFailed;
            return Result<ClipPocketSettings>.Failure(InfrastructureErrorFactory.FromException(exception, context, fallback));
        }
    }

    public async Task<Result> SaveAsync(ClipPocketSettings settings, CancellationToken cancellationToken = default)
    {
        if (settings is null)
        {
            return Result.Failure(new Error(ErrorCode.ValidationError, "Cannot save null settings."));
        }

        try
        {
            byte[] data = JsonSerializer.SerializeToUtf8Bytes(settings, JsonSerialization.Options);
            await File.WriteAllBytesAsync(StoragePaths.SettingsJson, data, cancellationToken);
            return Result.Success();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            string context = "Failed to save application settings.";
            return Result.Failure(InfrastructureErrorFactory.FromException(exception, context, ErrorCode.StorageWriteFailed));
        }
    }
}
