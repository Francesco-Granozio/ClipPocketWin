namespace ClipPocketWin.Shared.Imaging;

public static class DibBitmapConverter
{
    public static bool TryBuildBitmapFromDib(byte[] dibPayload, out byte[]? bmpBytes)
    {
        bmpBytes = null;
        if (dibPayload.Length < 40)
        {
            return false;
        }

        int headerSize = BitConverter.ToInt32(dibPayload, 0);
        if (headerSize < 40 || headerSize > dibPayload.Length)
        {
            return false;
        }

        short bitsPerPixel = BitConverter.ToInt16(dibPayload, 14);
        int compression = BitConverter.ToInt32(dibPayload, 16);
        int colorsUsed = BitConverter.ToInt32(dibPayload, 32);

        int colorTableEntries = colorsUsed;
        if (colorTableEntries == 0 && bitsPerPixel is > 0 and <= 8)
        {
            colorTableEntries = 1 << bitsPerPixel;
        }

        int maskBytes = compression is 3 or 6 ? 12 : 0;
        int colorTableBytes = colorTableEntries * 4;
        int pixelDataOffset = 14 + headerSize + maskBytes + colorTableBytes;
        if (pixelDataOffset > int.MaxValue - dibPayload.Length)
        {
            return false;
        }

        int fileSize = 14 + dibPayload.Length;
        byte[] fileHeader = new byte[14];
        fileHeader[0] = (byte)'B';
        fileHeader[1] = (byte)'M';
        Array.Copy(BitConverter.GetBytes(fileSize), 0, fileHeader, 2, 4);
        Array.Copy(BitConverter.GetBytes(pixelDataOffset), 0, fileHeader, 10, 4);

        bmpBytes = new byte[fileSize];
        Buffer.BlockCopy(fileHeader, 0, bmpBytes, 0, fileHeader.Length);
        Buffer.BlockCopy(dibPayload, 0, bmpBytes, fileHeader.Length, dibPayload.Length);
        return true;
    }
}
