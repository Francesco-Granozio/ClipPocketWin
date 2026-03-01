using ClipPocketWin.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using Windows.System;
using WinRT.Interop;

namespace ClipPocketWin;

public sealed partial class EditTextWindow : Window
{
    private static readonly Windows.UI.Color AcrylicTintColor = Windows.UI.Color.FromArgb(255, 18, 22, 52);
    private readonly string _initialText;
    private readonly AppWindow _appWindow;
    private readonly AdaptiveBackdropController? _adaptiveBackdropController;

    public event EventHandler<TextCommittedEventArgs>? TextCommitted;

    public EditTextWindow(string initialText)
    {
        InitializeComponent();
        _initialText = initialText ?? string.Empty;
        EditorTextBox.Text = _initialText;
        _appWindow = ConfigureWindowPresentation();

        App app = (App)Microsoft.UI.Xaml.Application.Current;
        ILogger<EditTextWindow>? logger = app.Services.GetService<ILogger<EditTextWindow>>();
        AdaptiveBackdropOptions backdropOptions = new()
        {
            DiagnosticName = nameof(EditTextWindow),
            BaseTintColor = AcrylicTintColor,
            StrongProtectionTintColor = Windows.UI.Color.FromArgb(255, 3, 5, 14),
            MinTintOpacity = 0.20f,
            MaxTintOpacity = 0.90f,
            MinLuminosityOpacity = 0.08f,
            MaxLuminosityOpacity = 0.72f,
            FallbackMinAlpha = 220,
            FallbackMaxAlpha = 255
        };

        _adaptiveBackdropController = new AdaptiveBackdropController(this, _appWindow, backdropOptions, logger);
        if (_adaptiveBackdropController.Initialize())
        {
            _appWindow.Changed += AppWindow_Changed;
        }

        Activated += Window_Activated;
        Closed += Window_Closed;
    }

    private AppWindow ConfigureWindowPresentation()
    {
        nint windowHandle = WindowNative.GetWindowHandle(this);
        WindowId windowId = Win32Interop.GetWindowIdFromWindow(windowHandle);
        AppWindow appWindow = AppWindow.GetFromWindowId(windowId);

        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMinimizable = false;
            presenter.IsMaximizable = false;
            presenter.IsResizable = false;
            presenter.SetBorderAndTitleBar(true, true);
        }

        appWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        appWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        appWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        appWindow.TitleBar.ButtonForegroundColor = Colors.White;
        appWindow.TitleBar.ButtonInactiveForegroundColor = Colors.Transparent;

        const int width = 760;
        const int height = 520;
        DisplayArea displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        int x = displayArea.WorkArea.X + ((displayArea.WorkArea.Width - width) / 2);
        int y = displayArea.WorkArea.Y + ((displayArea.WorkArea.Height - height) / 2);
        appWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, width, height));

        return appWindow;
    }

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        _adaptiveBackdropController?.HandleAppWindowChanged(args);
    }

    private void Window_Activated(object sender, WindowActivatedEventArgs args)
    {
        _adaptiveBackdropController?.HandleWindowActivated(args);
    }

    private void Window_Closed(object sender, WindowEventArgs args)
    {
        _appWindow.Changed -= AppWindow_Changed;
        Activated -= Window_Activated;

        string editedText = EditorTextBox.Text ?? string.Empty;
        if (!string.Equals(editedText, _initialText, StringComparison.Ordinal))
        {
            TextCommitted?.Invoke(this, new TextCommittedEventArgs(editedText));
        }

        _adaptiveBackdropController?.Dispose();
    }

    private void EditorTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Tab)
        {
            return;
        }

        e.Handled = true;

        string text = EditorTextBox.Text ?? string.Empty;
        int selectionStart = Math.Clamp(EditorTextBox.SelectionStart, 0, text.Length);
        int selectionLength = Math.Clamp(EditorTextBox.SelectionLength, 0, text.Length - selectionStart);

        if (IsShiftPressed())
        {
            ApplyOutdent(text, selectionStart, selectionLength);
            return;
        }

        ApplyIndent(text, selectionStart, selectionLength);
    }

    private void ApplyIndent(string text, int selectionStart, int selectionLength)
    {
        if (selectionLength == 0)
        {
            EditorTextBox.Text = text.Insert(selectionStart, "\t");
            EditorTextBox.SelectionStart = selectionStart + 1;
            EditorTextBox.SelectionLength = 0;
            return;
        }

        List<int> lineStarts = GetAffectedLineStarts(text, selectionStart, selectionLength);
        if (lineStarts.Count == 0)
        {
            return;
        }

        string updatedText = text;
        for (int i = lineStarts.Count - 1; i >= 0; i--)
        {
            updatedText = updatedText.Insert(lineStarts[i], "\t");
        }

        EditorTextBox.Text = updatedText;
        EditorTextBox.SelectionStart = selectionStart + 1;
        EditorTextBox.SelectionLength = selectionLength + lineStarts.Count;
    }

    private void ApplyOutdent(string text, int selectionStart, int selectionLength)
    {
        if (selectionLength == 0)
        {
            int lineStart = GetLineStartIndex(text, selectionStart);
            int removeLength = GetLineIndentLength(text, lineStart);
            if (removeLength == 0)
            {
                return;
            }

            EditorTextBox.Text = text.Remove(lineStart, removeLength);
            EditorTextBox.SelectionStart = Math.Max(lineStart, selectionStart - removeLength);
            EditorTextBox.SelectionLength = 0;
            return;
        }

        List<int> lineStarts = GetAffectedLineStarts(text, selectionStart, selectionLength);
        if (lineStarts.Count == 0)
        {
            return;
        }

        List<(int LineStart, int RemoveLength)> removals = [];
        foreach (int lineStart in lineStarts)
        {
            int removeLength = GetLineIndentLength(text, lineStart);
            if (removeLength > 0)
            {
                removals.Add((lineStart, removeLength));
            }
        }

        if (removals.Count == 0)
        {
            return;
        }

        string updatedText = text;
        for (int i = removals.Count - 1; i >= 0; i--)
        {
            (int lineStart, int removeLength) = removals[i];
            updatedText = updatedText.Remove(lineStart, removeLength);
        }

        int selectionEnd = selectionStart + selectionLength;
        int removedBeforeStart = 0;
        int removedBeforeEnd = 0;
        foreach ((int lineStart, int removeLength) in removals)
        {
            if (lineStart < selectionStart)
            {
                removedBeforeStart += removeLength;
            }

            if (lineStart < selectionEnd)
            {
                removedBeforeEnd += removeLength;
            }
        }

        int newSelectionStart = Math.Max(0, selectionStart - removedBeforeStart);
        int newSelectionEnd = Math.Max(newSelectionStart, selectionEnd - removedBeforeEnd);

        EditorTextBox.Text = updatedText;
        EditorTextBox.SelectionStart = newSelectionStart;
        EditorTextBox.SelectionLength = newSelectionEnd - newSelectionStart;
    }

    private static List<int> GetAffectedLineStarts(string text, int selectionStart, int selectionLength)
    {
        List<int> lineStarts = [];
        int firstLineStart = GetLineStartIndex(text, selectionStart);
        int selectionEnd = selectionStart + selectionLength;

        int lineStart = firstLineStart;
        while (lineStart < selectionEnd)
        {
            lineStarts.Add(lineStart);

            int lineBreakIndex = text.IndexOf('\n', lineStart);
            if (lineBreakIndex < 0)
            {
                break;
            }

            lineStart = lineBreakIndex + 1;
        }

        return lineStarts;
    }

    private static int GetLineStartIndex(string text, int index)
    {
        if (index <= 0 || text.Length == 0)
        {
            return 0;
        }

        int searchIndex = Math.Min(index - 1, text.Length - 1);
        int lineBreakIndex = text.LastIndexOf('\n', searchIndex);
        return lineBreakIndex < 0 ? 0 : lineBreakIndex + 1;
    }

    private static int GetLineIndentLength(string text, int lineStart)
    {
        if (lineStart >= text.Length)
        {
            return 0;
        }

        if (text[lineStart] == '\t')
        {
            return 1;
        }

        int spaces = 0;
        while (spaces < 4 && lineStart + spaces < text.Length && text[lineStart + spaces] == ' ')
        {
            spaces++;
        }

        return spaces;
    }

    private static bool IsShiftPressed()
    {
        return IsKeyDown(VirtualKey.Shift)
            || IsKeyDown(VirtualKey.LeftShift)
            || IsKeyDown(VirtualKey.RightShift);
    }

    private static bool IsKeyDown(VirtualKey key)
    {
        Windows.UI.Core.CoreVirtualKeyStates state = InputKeyboardSource.GetKeyStateForCurrentThread(key);
        return state.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
    }
}

public sealed class TextCommittedEventArgs : EventArgs
{
    public TextCommittedEventArgs(string editedText)
    {
        EditedText = editedText;
    }

    public string EditedText { get; }
}
