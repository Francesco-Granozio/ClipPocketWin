namespace ClipPocketWin.Application.Services;

internal readonly record struct ClipboardStateSettingsUpdate(bool CaptureRichTextChanged, bool HistoryLimitEnforced);
