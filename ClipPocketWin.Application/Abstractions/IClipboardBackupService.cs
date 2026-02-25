using ClipPocketWin.Shared.ResultPattern;

namespace ClipPocketWin.Application.Abstractions;

public interface IClipboardBackupService
{
    Task<Result<byte[]>> ExportBackupAsync(CancellationToken cancellationToken = default);

    Task<Result> ImportBackupAsync(byte[] payload, CancellationToken cancellationToken = default);
}
