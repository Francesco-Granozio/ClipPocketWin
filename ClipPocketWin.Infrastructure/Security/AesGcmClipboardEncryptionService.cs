using ClipPocketWin.Domain.Abstractions;
using ClipPocketWin.Shared.ResultPattern;
using System.Security.Cryptography;

namespace ClipPocketWin.Infrastructure.Security;

public sealed class AesGcmClipboardEncryptionService : IClipboardEncryptionService
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;

    public Task<Result<byte[]>> EncryptAsync(byte[] clearData, CancellationToken cancellationToken = default)
    {
        if (clearData is null)
        {
            return Task.FromResult(Result<byte[]>.Failure(new Error(ErrorCode.ValidationError, "Cannot encrypt null clipboard payload.")));
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            byte[] key = LoadOrCreateKey();
            byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
            byte[] cipher = new byte[clearData.Length];
            byte[] tag = new byte[TagSize];

            using AesGcm aes = new(key, TagSize);
            aes.Encrypt(nonce, clearData, cipher, tag);

            byte[] combined = new byte[NonceSize + TagSize + cipher.Length];
            Buffer.BlockCopy(nonce, 0, combined, 0, NonceSize);
            Buffer.BlockCopy(tag, 0, combined, NonceSize, TagSize);
            Buffer.BlockCopy(cipher, 0, combined, NonceSize + TagSize, cipher.Length);
            return Task.FromResult(Result<byte[]>.Success(combined));
        }
        catch (CryptographicException exception)
        {
            return Task.FromResult(Result<byte[]>.Failure(new Error(ErrorCode.EncryptionFailed, "Failed to encrypt clipboard history payload.", exception)));
        }
        catch (IOException exception)
        {
            return Task.FromResult(Result<byte[]>.Failure(new Error(ErrorCode.EncryptionKeyUnavailable, "Failed to load or create encryption key.", exception)));
        }
        catch (UnauthorizedAccessException exception)
        {
            return Task.FromResult(Result<byte[]>.Failure(new Error(ErrorCode.StorageAccessDenied, "Insufficient permission to access encryption key file.", exception)));
        }
        catch (Exception exception)
        {
            return Task.FromResult(Result<byte[]>.Failure(new Error(ErrorCode.UnknownError, "Unexpected encryption failure.", exception)));
        }
    }

    public Task<Result<byte[]>> DecryptAsync(byte[] encryptedData, CancellationToken cancellationToken = default)
    {
        if (encryptedData is null)
        {
            return Task.FromResult(Result<byte[]>.Failure(new Error(ErrorCode.ValidationError, "Cannot decrypt null clipboard payload.")));
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (encryptedData.Length <= NonceSize + TagSize)
        {
            return Task.FromResult(Result<byte[]>.Failure(new Error(ErrorCode.EncryptedPayloadInvalid, "Encrypted payload is invalid or truncated.")));
        }

        try
        {
            byte[] key = LoadOrCreateKey();
            byte[] nonce = encryptedData[..NonceSize];
            byte[] tag = encryptedData[NonceSize..(NonceSize + TagSize)];
            byte[] cipher = encryptedData[(NonceSize + TagSize)..];
            byte[] clear = new byte[cipher.Length];

            using AesGcm aes = new(key, TagSize);
            aes.Decrypt(nonce, cipher, tag, clear);

            return Task.FromResult(Result<byte[]>.Success(clear));
        }
        catch (CryptographicException exception)
        {
            return Task.FromResult(Result<byte[]>.Failure(new Error(ErrorCode.DecryptionFailed, "Failed to decrypt clipboard history payload.", exception)));
        }
        catch (IOException exception)
        {
            return Task.FromResult(Result<byte[]>.Failure(new Error(ErrorCode.EncryptionKeyUnavailable, "Failed to load encryption key.", exception)));
        }
        catch (UnauthorizedAccessException exception)
        {
            return Task.FromResult(Result<byte[]>.Failure(new Error(ErrorCode.StorageAccessDenied, "Insufficient permission to access encryption key file.", exception)));
        }
        catch (Exception exception)
        {
            return Task.FromResult(Result<byte[]>.Failure(new Error(ErrorCode.UnknownError, "Unexpected decryption failure.", exception)));
        }
    }

    private static byte[] LoadOrCreateKey()
    {
        string keyFile = Persistence.StoragePaths.EncryptionKeyFile;
        if (File.Exists(keyFile))
        {
            byte[] key = File.ReadAllBytes(keyFile);
            if (key.Length == KeySize)
            {
                return key;
            }
        }

        byte[] newKey = RandomNumberGenerator.GetBytes(KeySize);
        File.WriteAllBytes(keyFile, newKey);
        return newKey;
    }
}
