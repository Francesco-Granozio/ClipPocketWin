using ClipPocketWin.Shared.ResultPattern;

namespace ClipPocketWin.Domain.Abstractions;

public interface IClipboardEncryptionService
{
    Task<Result<byte[]>> EncryptAsync(byte[] clearData, CancellationToken cancellationToken = default);

    Task<Result<byte[]>> DecryptAsync(byte[] encryptedData, CancellationToken cancellationToken = default);
}
