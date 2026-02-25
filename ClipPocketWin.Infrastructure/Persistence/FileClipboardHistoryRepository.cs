using ClipPocketWin.Domain;
using ClipPocketWin.Domain.Abstractions;
using ClipPocketWin.Domain.Models;
using ClipPocketWin.Infrastructure.Errors;
using ClipPocketWin.Shared.ResultPattern;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace ClipPocketWin.Infrastructure.Persistence;

public sealed class FileClipboardHistoryRepository : IClipboardHistoryRepository
{
    private readonly IClipboardEncryptionService _encryptionService;
    private readonly ILogger<FileClipboardHistoryRepository> _logger;

    public FileClipboardHistoryRepository(
        IClipboardEncryptionService encryptionService,
        ILogger<FileClipboardHistoryRepository> logger)
    {
        _encryptionService = encryptionService;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<ClipboardItem>>> LoadAsync(bool encrypted, CancellationToken cancellationToken = default)
    {
        try
        {
            string expectedPath = encrypted ? StoragePaths.ClipboardHistoryEncrypted : StoragePaths.ClipboardHistoryJson;
            if (!File.Exists(expectedPath))
            {
                string fallbackPath = encrypted ? StoragePaths.ClipboardHistoryJson : StoragePaths.ClipboardHistoryEncrypted;
                if (!File.Exists(fallbackPath))
                {
                    return Result<IReadOnlyList<ClipboardItem>>.Success([]);
                }

                expectedPath = fallbackPath;
            }

            byte[] data = await File.ReadAllBytesAsync(expectedPath, cancellationToken);
            if (data.Length == 0)
            {
                return Result<IReadOnlyList<ClipboardItem>>.Success([]);
            }

            bool shouldDecrypt = encrypted || expectedPath.EndsWith(".encrypted", StringComparison.OrdinalIgnoreCase);
            if (shouldDecrypt)
            {
                Result<byte[]> decryptResult = await _encryptionService.DecryptAsync(data, cancellationToken);
                if (decryptResult.IsFailure)
                {
                    return Result<IReadOnlyList<ClipboardItem>>.Failure(decryptResult.Error!);
                }

                data = decryptResult.Value!;
            }

            List<ClipboardItem> items = JsonSerializer.Deserialize<List<ClipboardItem>>(data, JsonSerialization.Options) ?? [];
            ClipboardItem[] filtered = items
                .Where(item => item.Type != ClipboardItemType.Image || item.BinaryContent?.Length <= DomainLimits.MaxPersistedImageBytes)
                .Take(DomainLimits.MaxHistoryItemsHardLimit)
                .ToArray();

            return Result<IReadOnlyList<ClipboardItem>>.Success(filtered);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            string context = "Failed to load clipboard history from storage.";
            ErrorCode fallback = exception is JsonException ? ErrorCode.DeserializationFailed : ErrorCode.StorageReadFailed;
            return Result<IReadOnlyList<ClipboardItem>>.Failure(InfrastructureErrorFactory.FromException(exception, context, fallback));
        }
    }

    public async Task<Result> SaveAsync(IReadOnlyList<ClipboardItem> items, bool encrypted, CancellationToken cancellationToken = default)
    {
        if (items is null)
        {
            return Result.Failure(new Error(ErrorCode.ValidationError, "Cannot save null clipboard history."));
        }

        try
        {
            string destinationPath = encrypted ? StoragePaths.ClipboardHistoryEncrypted : StoragePaths.ClipboardHistoryJson;
            string secondaryPath = encrypted ? StoragePaths.ClipboardHistoryJson : StoragePaths.ClipboardHistoryEncrypted;

            ClipboardItem[] filteredItems = items
                .Where(item => item.Type != ClipboardItemType.Image || item.BinaryContent?.Length <= DomainLimits.MaxPersistedImageBytes)
                .Take(DomainLimits.MaxHistoryItemsHardLimit)
                .ToArray();

            byte[] serialized = JsonSerializer.SerializeToUtf8Bytes(filteredItems, JsonSerialization.Options);
            if (encrypted)
            {
                Result<byte[]> encryptResult = await _encryptionService.EncryptAsync(serialized, cancellationToken);
                if (encryptResult.IsFailure)
                {
                    return Result.Failure(encryptResult.Error!);
                }

                serialized = encryptResult.Value!;
            }

            await File.WriteAllBytesAsync(destinationPath, serialized, cancellationToken);
            if (File.Exists(secondaryPath))
            {
                File.Delete(secondaryPath);
            }

            _logger.LogInformation("Saved {Count} clipboard items to {Path}", filteredItems.Length, destinationPath);
            return Result.Success();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            string context = "Failed to save clipboard history to storage.";
            return Result.Failure(InfrastructureErrorFactory.FromException(exception, context, ErrorCode.StorageWriteFailed));
        }
    }

    public Task<Result> ClearAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (File.Exists(StoragePaths.ClipboardHistoryJson))
            {
                File.Delete(StoragePaths.ClipboardHistoryJson);
            }

            if (File.Exists(StoragePaths.ClipboardHistoryEncrypted))
            {
                File.Delete(StoragePaths.ClipboardHistoryEncrypted);
            }

            return Task.FromResult(Result.Success());
        }
        catch (Exception exception)
        {
            string context = "Failed to clear clipboard history files.";
            return Task.FromResult(Result.Failure(InfrastructureErrorFactory.FromException(exception, context, ErrorCode.StorageDeleteFailed)));
        }
    }
}
