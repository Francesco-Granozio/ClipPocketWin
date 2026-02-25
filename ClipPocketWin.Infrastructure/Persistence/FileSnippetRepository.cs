using ClipPocketWin.Domain;
using ClipPocketWin.Domain.Abstractions;
using ClipPocketWin.Domain.Models;
using ClipPocketWin.Infrastructure.Errors;
using ClipPocketWin.Shared.ResultPattern;
using System.Text.Json;

namespace ClipPocketWin.Infrastructure.Persistence;

public sealed class FileSnippetRepository : ISnippetRepository
{
    public async Task<Result<IReadOnlyList<Snippet>>> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(StoragePaths.SnippetsJson))
            {
                return Result<IReadOnlyList<Snippet>>.Success([]);
            }

            byte[] data = await File.ReadAllBytesAsync(StoragePaths.SnippetsJson, cancellationToken);
            if (data.Length == 0)
            {
                return Result<IReadOnlyList<Snippet>>.Success([]);
            }

            List<Snippet> snippets = JsonSerializer.Deserialize<List<Snippet>>(data, JsonSerialization.Options) ?? [];
            return Result<IReadOnlyList<Snippet>>.Success(snippets.Take(DomainLimits.MaxSnippets).ToArray());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            string context = "Failed to load snippets.";
            ErrorCode fallback = exception is JsonException ? ErrorCode.DeserializationFailed : ErrorCode.StorageReadFailed;
            return Result<IReadOnlyList<Snippet>>.Failure(InfrastructureErrorFactory.FromException(exception, context, fallback));
        }
    }

    public async Task<Result> SaveAsync(IReadOnlyList<Snippet> snippets, CancellationToken cancellationToken = default)
    {
        if (snippets is null)
        {
            return Result.Failure(new Error(ErrorCode.ValidationError, "Cannot save null snippets collection."));
        }

        try
        {
            Snippet[] filtered = snippets.Take(DomainLimits.MaxSnippets).ToArray();
            byte[] data = JsonSerializer.SerializeToUtf8Bytes(filtered, JsonSerialization.Options);
            await File.WriteAllBytesAsync(StoragePaths.SnippetsJson, data, cancellationToken);
            return Result.Success();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            string context = "Failed to save snippets.";
            return Result.Failure(InfrastructureErrorFactory.FromException(exception, context, ErrorCode.StorageWriteFailed));
        }
    }
}
