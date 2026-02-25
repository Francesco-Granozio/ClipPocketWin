using ClipPocketWin.Domain.Models;
using ClipPocketWin.Shared.ResultPattern;

namespace ClipPocketWin.Domain.Abstractions;

public interface ISnippetRepository
{
    Task<Result<IReadOnlyList<Snippet>>> LoadAsync(CancellationToken cancellationToken = default);

    Task<Result> SaveAsync(IReadOnlyList<Snippet> snippets, CancellationToken cancellationToken = default);
}
