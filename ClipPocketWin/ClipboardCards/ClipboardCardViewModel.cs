using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace ClipPocketWin;

internal sealed class ClipboardCardViewModel : INotifyPropertyChanged
{
    private string _timestampLabel;

    public ClipboardCardViewModel(
        Guid id,
        string typeLabel,
        DateTimeOffset capturedAt,
        string previewText,
        string iconGlyph,
        string pinLabel,
        BitmapImage? sourceAppIcon,
        Visibility sourceIconVisibility,
        Visibility sourceIconFallbackVisibility,
        BitmapImage? previewImage,
        Visibility imagePreviewVisibility,
        Visibility colorPreviewVisibility,
        Visibility filePreviewVisibility,
        Visibility codePreviewVisibility,
        Visibility textPreviewVisibility,
        Brush cardBackgroundBrush,
        Brush headerBackgroundBrush,
        Brush bodyBackgroundBrush,
        Brush iconBackgroundBrush,
        Brush iconForegroundBrush,
        string colorPreviewText,
        Brush colorPreviewForegroundBrush,
        BitmapImage? fileIcon,
        Visibility fileIconVisibility,
        Visibility fileGlyphVisibility,
        string fileGlyph,
        string fileName,
        string filePath,
        string fileSize,
        string codeText)
    {
        Id = id;
        TypeLabel = typeLabel;
        CapturedAt = capturedAt;
        PreviewText = previewText;
        IconGlyph = iconGlyph;
        PinLabel = pinLabel;
        SourceAppIcon = sourceAppIcon;
        SourceIconVisibility = sourceIconVisibility;
        SourceIconFallbackVisibility = sourceIconFallbackVisibility;
        PreviewImage = previewImage;
        ImagePreviewVisibility = imagePreviewVisibility;
        ColorPreviewVisibility = colorPreviewVisibility;
        FilePreviewVisibility = filePreviewVisibility;
        CodePreviewVisibility = codePreviewVisibility;
        TextPreviewVisibility = textPreviewVisibility;
        CardBackgroundBrush = cardBackgroundBrush;
        HeaderBackgroundBrush = headerBackgroundBrush;
        BodyBackgroundBrush = bodyBackgroundBrush;
        IconBackgroundBrush = iconBackgroundBrush;
        IconForegroundBrush = iconForegroundBrush;
        ColorPreviewText = colorPreviewText;
        ColorPreviewForegroundBrush = colorPreviewForegroundBrush;
        FileIcon = fileIcon;
        FileIconVisibility = fileIconVisibility;
        FileGlyphVisibility = fileGlyphVisibility;
        FileGlyph = fileGlyph;
        FileName = fileName;
        FilePath = filePath;
        FileSize = fileSize;
        CodeText = codeText;
        _timestampLabel = ClipboardCardTimeFormatter.GetRelativeTimestampLabel(CapturedAt);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id { get; }

    public string TypeLabel { get; }

    public DateTimeOffset CapturedAt { get; }

    public string TimestampLabel
    {
        get => _timestampLabel;
        private set
        {
            if (string.Equals(_timestampLabel, value, StringComparison.Ordinal))
            {
                return;
            }

            _timestampLabel = value;
            OnPropertyChanged();
        }
    }

    public string PreviewText { get; }

    public string IconGlyph { get; }

    public string PinLabel { get; }

    public BitmapImage? SourceAppIcon { get; }

    public Visibility SourceIconVisibility { get; }

    public Visibility SourceIconFallbackVisibility { get; }

    public BitmapImage? PreviewImage { get; }

    public Visibility ImagePreviewVisibility { get; }

    public Visibility ColorPreviewVisibility { get; }

    public Visibility FilePreviewVisibility { get; }

    public Visibility CodePreviewVisibility { get; }

    public Visibility TextPreviewVisibility { get; }

    public Brush CardBackgroundBrush { get; }

    public Brush HeaderBackgroundBrush { get; }

    public Brush BodyBackgroundBrush { get; }

    public Brush IconBackgroundBrush { get; }

    public Brush IconForegroundBrush { get; }

    public string ColorPreviewText { get; }

    public Brush ColorPreviewForegroundBrush { get; }

    public BitmapImage? FileIcon { get; }

    public Visibility FileIconVisibility { get; }

    public Visibility FileGlyphVisibility { get; }

    public string FileGlyph { get; }

    public string FileName { get; }

    public string FilePath { get; }

    public string FileSize { get; }

    public string CodeText { get; }

    public void RefreshRelativeTime()
    {
        TimestampLabel = ClipboardCardTimeFormatter.GetRelativeTimestampLabel(CapturedAt);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
