using ClipPocketWin.Domain.Models;
using ClipPocketWin.Shared.ResultPattern;

namespace ClipPocketWin.Domain.Abstractions;

public interface ISettingsRepository
{
    Task<Result<ClipPocketSettings>> LoadAsync(CancellationToken cancellationToken = default);

    Task<Result> SaveAsync(ClipPocketSettings settings, CancellationToken cancellationToken = default);
}
