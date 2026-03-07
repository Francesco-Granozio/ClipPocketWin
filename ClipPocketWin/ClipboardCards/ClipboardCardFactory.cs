using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using ClipPocketWin.Domain.Models;
using ClipPocketWin.Shared.Colors;
using ClipPocketWin.Shared.Imaging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace ClipPocketWin;

internal static class ClipboardCardFactory
{
    public static ClipboardCardViewModel Create(ClipboardItem item, bool isPinned)
    {
        SourceAppVisual sourceAppVisual = SourceAppIconCache.Resolve(item.SourceApplicationExecutablePath);
        ClipboardCardStyle style = ResolveCardStyle(item, sourceAppVisual.VibrantColor);
        BitmapImage? previewImage = item.IsImage
            ? ImagePreviewCache.Resolve(item.BinaryContent)
            : null;

        bool isImage = previewImage is not null;
        bool isColor = item.IsColor;
        bool isFile = item.IsFile;
        bool isCode = item.IsCode;

        Visibility imagePreviewVisibility = isImage ? Visibility.Visible : Visibility.Collapsed;
        Visibility colorPreviewVisibility = isColor ? Visibility.Visible : Visibility.Collapsed;
        Visibility filePreviewVisibility = isFile ? Visibility.Visible : Visibility.Collapsed;
        Visibility codePreviewVisibility = isCode ? Visibility.Visible : Visibility.Collapsed;
        Visibility textPreviewVisibility = (!isImage && !isColor && !isFile && !isCode) ? Visibility.Visible : Visibility.Collapsed;

        FileCardInfo fileCardInfo = CreateFileCardInfo(item.FilePath);
        BitmapImage? fileIcon = isFile ? FileTypeIconCache.Resolve(item.FilePath) : null;
        Visibility fileIconVisibility = fileIcon is null ? Visibility.Collapsed : Visibility.Visible;
        Visibility fileGlyphVisibility = fileIcon is null ? Visibility.Visible : Visibility.Collapsed;

        Windows.UI.Color? parsedColor = TryResolveClipboardColor(item);
        Brush colorPreviewForegroundBrush = isColor && parsedColor is Windows.UI.Color resolvedColor
            ? new SolidColorBrush(GetContrastingColor(resolvedColor))
            : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));

        return new ClipboardCardViewModel(
            item.Id,
            item.TypeLabel,
            item.Timestamp,
            item.PreviewText,
            item.Glyph,
            isPinned ? "Pinned" : string.Empty,
            sourceAppVisual.Icon,
            sourceAppVisual.HasIcon ? Visibility.Visible : Visibility.Collapsed,
            sourceAppVisual.HasIcon ? Visibility.Collapsed : Visibility.Visible,
            previewImage,
            imagePreviewVisibility,
            colorPreviewVisibility,
            filePreviewVisibility,
            codePreviewVisibility,
            textPreviewVisibility,
            style.CardBackgroundBrush,
            style.HeaderBackgroundBrush,
            style.BodyBackgroundBrush,
            style.IconBackgroundBrush,
            style.IconForegroundBrush,
            item.TextContent ?? string.Empty,
            colorPreviewForegroundBrush,
            fileIcon,
            fileIconVisibility,
            fileGlyphVisibility,
            "\uE7C3",
            fileCardInfo.Name,
            fileCardInfo.Path,
            fileCardInfo.Size,
            item.TextContent ?? string.Empty);
    }

    private static ClipboardCardStyle ResolveCardStyle(ClipboardItem item, Windows.UI.Color? sourceVibrantColor)
    {
        if (TryResolveClipboardColor(item) is Windows.UI.Color parsedColor)
        {
            return BuildSolidColorCardStyle(parsedColor);
        }

        if (sourceVibrantColor is Windows.UI.Color iconAccent)
        {
            return BuildCardStyle(iconAccent, Windows.UI.Color.FromArgb(255, 255, 255, 255));
        }

        ClipboardCardStyleColors colors = ClipboardCardStylePalette.Resolve(item);
        return BuildCardStyle(colors.AccentColor, colors.IconColor);
    }

    private static ClipboardCardStyle BuildCardStyle(Windows.UI.Color accentColor, Windows.UI.Color iconColor)
    {
        return new ClipboardCardStyle(
            CreateCardBackgroundBrush(accentColor),
            CreateHeaderBackgroundBrush(accentColor),
            CreateBodyBackgroundBrush(accentColor),
            new SolidColorBrush(Windows.UI.Color.FromArgb(102, accentColor.R, accentColor.G, accentColor.B)),
            new SolidColorBrush(iconColor));
    }

    private static ClipboardCardStyle BuildSolidColorCardStyle(Windows.UI.Color color)
    {
        return new ClipboardCardStyle(
            new SolidColorBrush(color),
            CreateColorHeaderBackgroundBrush(color),
            new SolidColorBrush(color),
            new SolidColorBrush(Windows.UI.Color.FromArgb(58, 255, 255, 255)),
            new SolidColorBrush(GetContrastingColor(color)));
    }

    private static LinearGradientBrush CreateColorHeaderBackgroundBrush(Windows.UI.Color color)
    {
        LinearGradientBrush brush = new()
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(0, 1)
        };

        brush.GradientStops.Add(new GradientStop
        {
            Color = color,
            Offset = 0
        });

        brush.GradientStops.Add(new GradientStop
        {
            Color = Darken(color, 0.32f),
            Offset = 1
        });

        return brush;
    }

    private static LinearGradientBrush CreateHeaderBackgroundBrush(Windows.UI.Color accentColor)
    {
        Windows.UI.Color upper = Lighten(accentColor, 0.18f);
        Windows.UI.Color lower = Darken(accentColor, 0.26f);
        LinearGradientBrush brush = new()
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(1, 1)
        };

        brush.GradientStops.Add(new GradientStop
        {
            Color = upper,
            Offset = 0
        });

        brush.GradientStops.Add(new GradientStop
        {
            Color = accentColor,
            Offset = 0.52
        });

        brush.GradientStops.Add(new GradientStop
        {
            Color = lower,
            Offset = 1
        });

        return brush;
    }

    private static LinearGradientBrush CreateCardBackgroundBrush(Windows.UI.Color accentColor)
    {
        LinearGradientBrush brush = new()
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(1, 1)
        };

        brush.GradientStops.Add(new GradientStop
        {
            Color = Windows.UI.Color.FromArgb(96, accentColor.R, accentColor.G, accentColor.B),
            Offset = 0
        });

        brush.GradientStops.Add(new GradientStop
        {
            Color = Windows.UI.Color.FromArgb(54, accentColor.R, accentColor.G, accentColor.B),
            Offset = 0.58
        });

        brush.GradientStops.Add(new GradientStop
        {
            Color = Windows.UI.Color.FromArgb(62, 10, 15, 24),
            Offset = 1
        });

        return brush;
    }

    private static LinearGradientBrush CreateBodyBackgroundBrush(Windows.UI.Color accentColor)
    {
        LinearGradientBrush brush = new()
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(1, 1)
        };

        brush.GradientStops.Add(new GradientStop
        {
            Color = Windows.UI.Color.FromArgb(44, accentColor.R, accentColor.G, accentColor.B),
            Offset = 0
        });

        brush.GradientStops.Add(new GradientStop
        {
            Color = Windows.UI.Color.FromArgb(24, accentColor.R, accentColor.G, accentColor.B),
            Offset = 0.6
        });

        brush.GradientStops.Add(new GradientStop
        {
            Color = Windows.UI.Color.FromArgb(12, 255, 255, 255),
            Offset = 1
        });

        return brush;
    }

    private static Windows.UI.Color? TryResolveClipboardColor(ClipboardItem item)
    {
        if (!item.IsColor)
        {
            return null;
        }

        string? text = item.TextContent;
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (!ClipboardColorParser.TryParse(text, out ClipboardColor parsedColor))
        {
            return null;
        }

        return ToWindowsColor(parsedColor);
    }

    private static Windows.UI.Color ToWindowsColor(ClipboardColor color)
    {
        return Windows.UI.Color.FromArgb(color.A, color.R, color.G, color.B);
    }

    private static Windows.UI.Color Darken(Windows.UI.Color color, float factor)
    {
        float clamped = Math.Clamp(factor, 0f, 1f);
        byte r = (byte)Math.Clamp((int)Math.Round(color.R * (1f - clamped)), 0, 255);
        byte g = (byte)Math.Clamp((int)Math.Round(color.G * (1f - clamped)), 0, 255);
        byte b = (byte)Math.Clamp((int)Math.Round(color.B * (1f - clamped)), 0, 255);
        return Windows.UI.Color.FromArgb(color.A, r, g, b);
    }

    private static Windows.UI.Color Lighten(Windows.UI.Color color, float factor)
    {
        float clamped = Math.Clamp(factor, 0f, 1f);
        byte r = (byte)Math.Clamp((int)Math.Round(color.R + ((255 - color.R) * clamped)), 0, 255);
        byte g = (byte)Math.Clamp((int)Math.Round(color.G + ((255 - color.G) * clamped)), 0, 255);
        byte b = (byte)Math.Clamp((int)Math.Round(color.B + ((255 - color.B) * clamped)), 0, 255);
        return Windows.UI.Color.FromArgb(color.A, r, g, b);
    }

    private static Windows.UI.Color GetContrastingColor(Windows.UI.Color backgroundColor)
    {
        double luminance = ((0.2126d * backgroundColor.R) + (0.7152d * backgroundColor.G) + (0.0722d * backgroundColor.B)) / 255d;
        return luminance > 0.56d
            ? Windows.UI.Color.FromArgb(255, 16, 21, 30)
            : Windows.UI.Color.FromArgb(255, 255, 255, 255);
    }

    private static FileCardInfo CreateFileCardInfo(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return new FileCardInfo("File", "Path unavailable", "Unknown size");
        }

        string normalizedPath = filePath.Trim();
        string fileName = Path.GetFileName(normalizedPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = normalizedPath;
        }

        string fileSizeLabel = "Unknown size";
        try
        {
            if (File.Exists(normalizedPath))
            {
                long fileSize = new FileInfo(normalizedPath).Length;
                fileSizeLabel = FormatFileSize(fileSize);
            }
        }
        catch
        {
            fileSizeLabel = "Unknown size";
        }

        return new FileCardInfo(fileName, normalizedPath, fileSizeLabel);
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 0)
        {
            return "Unknown size";
        }

        string[] units = ["bytes", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unitIndex = 0;
        while (value >= 1024d && unitIndex < units.Length - 1)
        {
            value /= 1024d;
            unitIndex++;
        }

        if (unitIndex == 0)
        {
            return $"{bytes} bytes";
        }

        return $"{value:0.#} {units[unitIndex]}";
    }

    private sealed record FileCardInfo(string Name, string Path, string Size);

    private sealed record ClipboardCardStyle(
        Brush CardBackgroundBrush,
        Brush HeaderBackgroundBrush,
        Brush BodyBackgroundBrush,
        Brush IconBackgroundBrush,
        Brush IconForegroundBrush);

    private sealed record SourceAppVisual(BitmapImage? Icon, Windows.UI.Color? VibrantColor, bool HasIcon);

    private static class ImagePreviewCache
    {
        private const int MaxInMemoryEntries = 160;
        private const int MaxDiskFileCount = 320;
        private const long MaxDiskBytes = 384L * 1024 * 1024;

        private static readonly Dictionary<string, BitmapImage?> Cache = new(StringComparer.Ordinal);
        private static readonly Dictionary<string, LinkedListNode<string>> CacheNodes = new(StringComparer.Ordinal);
        private static readonly LinkedList<string> CacheLru = [];
        private static readonly object SyncRoot = new();
        private static readonly string PreviewCacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClipPocketWin",
            "cache",
            "image-previews");

        public static BitmapImage? Resolve(byte[]? binaryContent)
        {
            if (binaryContent is null || binaryContent.Length == 0)
            {
                return null;
            }

            string hash = ComputeStableHash(binaryContent);
            lock (SyncRoot)
            {
                if (Cache.TryGetValue(hash, out BitmapImage? cached))
                {
                    Touch(hash);
                    return cached;
                }
            }

            BitmapImage? image = BuildPreviewImage(hash, binaryContent);
            lock (SyncRoot)
            {
                if (Cache.TryGetValue(hash, out BitmapImage? cached))
                {
                    Touch(hash);
                    return cached;
                }

                Cache[hash] = image;
                Touch(hash);
                TrimInMemoryCache();
                return image;
            }
        }

        private static BitmapImage? BuildPreviewImage(string hash, byte[] dibPayload)
        {
            try
            {
                CacheDirectoryPolicy.EnsureDirectoryAndApplyPolicy(
                    PreviewCacheDirectory,
                    MaxDiskFileCount,
                    MaxDiskBytes);

                string bmpPath = Path.Combine(PreviewCacheDirectory, hash + ".bmp");
                if (File.Exists(bmpPath))
                {
                    CacheDirectoryPolicy.TouchFile(bmpPath);
                }
                else
                {
                    if (DibBitmapConverter.TryBuildBitmapFromDib(dibPayload, out byte[]? bmpBytes) && bmpBytes is not null)
                    {
                        File.WriteAllBytes(bmpPath, bmpBytes);
                    }
                    else
                    {
                        File.WriteAllBytes(bmpPath, dibPayload);
                    }
                }

                return new BitmapImage(new Uri(bmpPath));
            }
            catch
            {
                return null;
            }
        }

        private static string ComputeStableHash(byte[] payload)
        {
            byte[] bytes = SHA256.HashData(payload);
            return Convert.ToHexString(bytes);
        }

        private static void Touch(string key)
        {
            if (CacheNodes.TryGetValue(key, out LinkedListNode<string>? node))
            {
                CacheLru.Remove(node);
            }
            else
            {
                node = new LinkedListNode<string>(key);
                CacheNodes[key] = node;
            }

            CacheLru.AddLast(node);
        }

        private static void TrimInMemoryCache()
        {
            while (Cache.Count > MaxInMemoryEntries && CacheLru.First is LinkedListNode<string> oldest)
            {
                string oldestKey = oldest.Value;
                CacheLru.RemoveFirst();
                CacheNodes.Remove(oldestKey);
                Cache.Remove(oldestKey);
            }
        }
    }

    private static class FileTypeIconCache
    {
        private const int MaxInMemoryEntries = 220;
        private const int MaxDiskFileCount = 480;
        private const long MaxDiskBytes = 256L * 1024 * 1024;

        private static readonly Dictionary<string, BitmapImage?> Cache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, LinkedListNode<string>> CacheNodes = new(StringComparer.OrdinalIgnoreCase);
        private static readonly LinkedList<string> CacheLru = [];
        private static readonly object SyncRoot = new();
        private static readonly string IconsCacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClipPocketWin",
            "cache",
            "file-icons");

        public static BitmapImage? Resolve(string? filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return null;
            }

            string extension = Path.GetExtension(filePath);
            bool isPerFileIcon = extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".url", StringComparison.OrdinalIgnoreCase);
            string cacheKey = isPerFileIcon
                ? filePath
                : (string.IsNullOrWhiteSpace(extension) ? "_noext" : extension);

            lock (SyncRoot)
            {
                if (Cache.TryGetValue(cacheKey, out BitmapImage? cached))
                {
                    Touch(cacheKey);
                    return cached;
                }
            }

            BitmapImage? icon = BuildIcon(filePath, extension, cacheKey, isPerFileIcon);
            lock (SyncRoot)
            {
                if (Cache.TryGetValue(cacheKey, out BitmapImage? cached))
                {
                    Touch(cacheKey);
                    return cached;
                }

                Cache[cacheKey] = icon;
                Touch(cacheKey);
                TrimInMemoryCache();
                return icon;
            }
        }

        private static BitmapImage? BuildIcon(string filePath, string extension, string cacheKey, bool isPerFileIcon)
        {
            try
            {
                CacheDirectoryPolicy.EnsureDirectoryAndApplyPolicy(
                    IconsCacheDirectory,
                    MaxDiskFileCount,
                    MaxDiskBytes);

                string safeKey = isPerFileIcon
                    ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cacheKey)))[..16]
                    : cacheKey.Replace('.', '_');

                string iconPngPath = Path.Combine(IconsCacheDirectory, safeKey + ".png");
                if (File.Exists(iconPngPath))
                {
                    CacheDirectoryPolicy.TouchFile(iconPngPath);
                }
                else
                {
                    using System.Drawing.Icon? icon = ResolveShellIcon(filePath, extension);
                    if (icon is null)
                    {
                        return null;
                    }

                    using System.Drawing.Bitmap bitmap = icon.ToBitmap();
                    bitmap.Save(iconPngPath, System.Drawing.Imaging.ImageFormat.Png);
                }

                return new BitmapImage(new Uri(iconPngPath));
            }
            catch
            {
                return null;
            }
        }

        private static void Touch(string key)
        {
            if (CacheNodes.TryGetValue(key, out LinkedListNode<string>? node))
            {
                CacheLru.Remove(node);
            }
            else
            {
                node = new LinkedListNode<string>(key);
                CacheNodes[key] = node;
            }

            CacheLru.AddLast(node);
        }

        private static void TrimInMemoryCache()
        {
            while (Cache.Count > MaxInMemoryEntries && CacheLru.First is LinkedListNode<string> oldest)
            {
                string oldestKey = oldest.Value;
                CacheLru.RemoveFirst();
                CacheNodes.Remove(oldestKey);
                Cache.Remove(oldestKey);
            }
        }

        private static System.Drawing.Icon? ResolveShellIcon(string filePath, string extension)
        {
            bool fileExists = File.Exists(filePath);
            string shellPath = fileExists
                ? filePath
                : string.IsNullOrWhiteSpace(extension) ? "placeholder.bin" : "placeholder" + extension;

            uint attributes = fileExists ? 0u : FileAttributeNormal;
            uint flags = ShgfiIcon | ShgfiLargeIcon;
            if (!fileExists)
            {
                flags |= ShgfiUseFileAttributes;
            }

            ShFileInfo info = new();
            nuint result = SHGetFileInfo(shellPath, attributes, ref info, (uint)Marshal.SizeOf<ShFileInfo>(), flags);
            if (result == 0 || info.IconHandle == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return (System.Drawing.Icon)System.Drawing.Icon.FromHandle(info.IconHandle).Clone();
            }
            finally
            {
                _ = DestroyIcon(info.IconHandle);
            }
        }

        private const uint FileAttributeNormal = 0x00000080;
        private const uint ShgfiIcon = 0x000000100;
        private const uint ShgfiLargeIcon = 0x000000000;
        private const uint ShgfiUseFileAttributes = 0x000000010;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ShFileInfo
        {
            public IntPtr IconHandle;
            public int IconIndex;
            public uint Attributes;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string DisplayName;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string TypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern nuint SHGetFileInfo(string pszPath, uint fileAttributes, ref ShFileInfo info, uint cbFileInfo, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);
    }

    private static class SourceAppIconCache
    {
        private const int MaxInMemoryEntries = 180;
        private const int MaxDiskFileCount = 360;
        private const long MaxDiskBytes = 192L * 1024 * 1024;

        private static readonly Dictionary<string, SourceAppVisual> Cache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, LinkedListNode<string>> CacheNodes = new(StringComparer.OrdinalIgnoreCase);
        private static readonly LinkedList<string> CacheLru = [];
        private static readonly object SyncRoot = new();
        private static readonly string IconsCacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClipPocketWin",
            "cache",
            "source-icons");

        public static SourceAppVisual Resolve(string? executablePath)
        {
            string? resolvedExecutablePath = ResolveExecutablePath(executablePath);
            if (string.IsNullOrWhiteSpace(resolvedExecutablePath) || !File.Exists(resolvedExecutablePath))
            {
                return new SourceAppVisual(null, null, false);
            }

            lock (SyncRoot)
            {
                if (Cache.TryGetValue(resolvedExecutablePath, out SourceAppVisual? cached))
                {
                    Touch(resolvedExecutablePath);
                    return cached;
                }
            }

            SourceAppVisual resolved = BuildVisual(resolvedExecutablePath);
            lock (SyncRoot)
            {
                if (Cache.TryGetValue(resolvedExecutablePath, out SourceAppVisual? cached))
                {
                    Touch(resolvedExecutablePath);
                    return cached;
                }

                Cache[resolvedExecutablePath] = resolved;
                Touch(resolvedExecutablePath);
                TrimInMemoryCache();
                return resolved;
            }
        }

        private static SourceAppVisual BuildVisual(string executablePath)
        {
            try
            {
                CacheDirectoryPolicy.EnsureDirectoryAndApplyPolicy(
                    IconsCacheDirectory,
                    MaxDiskFileCount,
                    MaxDiskBytes);

                string iconPngPath = Path.Combine(IconsCacheDirectory, ComputeStableHash(executablePath) + ".png");
                if (File.Exists(iconPngPath))
                {
                    CacheDirectoryPolicy.TouchFile(iconPngPath);
                }
                else
                {
                    using System.Drawing.Icon? icon = System.Drawing.Icon.ExtractAssociatedIcon(executablePath);
                    if (icon is null)
                    {
                        return new SourceAppVisual(null, null, false);
                    }

                    using System.Drawing.Bitmap bitmap = icon.ToBitmap();
                    bitmap.Save(iconPngPath, System.Drawing.Imaging.ImageFormat.Png);
                }

                BitmapImage image = new(new Uri(iconPngPath));
                Windows.UI.Color? vibrantColor = TryComputeVibrantColor(iconPngPath);
                return new SourceAppVisual(image, vibrantColor, true);
            }
            catch
            {
                return new SourceAppVisual(null, null, false);
            }
        }

        private static string? ResolveExecutablePath(string? executablePath)
        {
            if (!string.IsNullOrWhiteSpace(executablePath) && File.Exists(executablePath))
            {
                return executablePath;
            }

            return null;
        }

        private static Windows.UI.Color? TryComputeVibrantColor(string imagePath)
        {
            try
            {
                using System.Drawing.Bitmap bitmap = new(imagePath);
                long totalR = 0;
                long totalG = 0;
                long totalB = 0;
                int sampleCount = 0;

                int stepX = Math.Max(1, bitmap.Width / 12);
                int stepY = Math.Max(1, bitmap.Height / 12);
                for (int x = 0; x < bitmap.Width; x += stepX)
                {
                    for (int y = 0; y < bitmap.Height; y += stepY)
                    {
                        System.Drawing.Color pixel = bitmap.GetPixel(x, y);
                        if (pixel.A == 0)
                        {
                            continue;
                        }

                        totalR += pixel.R;
                        totalG += pixel.G;
                        totalB += pixel.B;
                        sampleCount++;
                    }
                }

                if (sampleCount == 0)
                {
                    return null;
                }

                int avgR = (int)(totalR / sampleCount);
                int avgG = (int)(totalG / sampleCount);
                int avgB = (int)(totalB / sampleCount);
                System.Drawing.Color average = System.Drawing.Color.FromArgb(avgR, avgG, avgB);

                double hue = average.GetHue();
                double saturation = Math.Min(1d, average.GetSaturation() * 1.7d);
                double brightness = Math.Min(1d, average.GetBrightness() * 1.3d);

                (byte r, byte g, byte b) = FromHsb(hue, saturation, brightness);
                return Windows.UI.Color.FromArgb(255, r, g, b);
            }
            catch
            {
                return null;
            }
        }

        private static (byte R, byte G, byte B) FromHsb(double hue, double saturation, double brightness)
        {
            double c = brightness * saturation;
            double x = c * (1 - Math.Abs((hue / 60d % 2) - 1));
            double m = brightness - c;

            (double r, double g, double b) = hue switch
            {
                >= 0 and < 60 => (c, x, 0d),
                >= 60 and < 120 => (x, c, 0d),
                >= 120 and < 180 => (0d, c, x),
                >= 180 and < 240 => (0d, x, c),
                >= 240 and < 300 => (x, 0d, c),
                _ => (c, 0d, x)
            };

            byte rr = (byte)Math.Clamp((int)Math.Round((r + m) * 255), 0, 255);
            byte gg = (byte)Math.Clamp((int)Math.Round((g + m) * 255), 0, 255);
            byte bb = (byte)Math.Clamp((int)Math.Round((b + m) * 255), 0, 255);
            return (rr, gg, bb);
        }

        private static string ComputeStableHash(string value)
        {
            byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
            return Convert.ToHexString(bytes);
        }

        private static void Touch(string key)
        {
            if (CacheNodes.TryGetValue(key, out LinkedListNode<string>? node))
            {
                CacheLru.Remove(node);
            }
            else
            {
                node = new LinkedListNode<string>(key);
                CacheNodes[key] = node;
            }

            CacheLru.AddLast(node);
        }

        private static void TrimInMemoryCache()
        {
            while (Cache.Count > MaxInMemoryEntries && CacheLru.First is LinkedListNode<string> oldest)
            {
                string oldestKey = oldest.Value;
                CacheLru.RemoveFirst();
                CacheNodes.Remove(oldestKey);
                Cache.Remove(oldestKey);
            }
        }
    }
}
