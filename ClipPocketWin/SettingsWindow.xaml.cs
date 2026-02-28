using ClipPocketWin.Application.Abstractions;
using ClipPocketWin.Domain;
using ClipPocketWin.Domain.Models;
using ClipPocketWin.Runtime;
using ClipPocketWin.Shared.ResultPattern;
using Microsoft.Extensions.Logging;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.System;
using WinRT.Interop;

namespace ClipPocketWin;

public sealed partial class SettingsWindow : Window
{
    private readonly IClipboardStateService _clipboardStateService;
    private readonly IGlobalHotkeyService _globalHotkeyService;
    private readonly ILogger<SettingsWindow>? _logger;

    private ClipPocketSettings _settings;
    private bool _isLoading;
    private bool _isRecordingShortcut;

    public SettingsWindow(
        IClipboardStateService clipboardStateService,
        IGlobalHotkeyService globalHotkeyService,
        ILogger<SettingsWindow>? logger)
    {
        InitializeComponent();
        _clipboardStateService = clipboardStateService;
        _globalHotkeyService = globalHotkeyService;
        _logger = logger;
        _settings = clipboardStateService.Settings;

        ConfigureWindowPresentation();
        Activated += SettingsWindow_Activated;
        InitializeView();
    }

    private void ConfigureWindowPresentation()
    {
        nint windowHandle = WindowNative.GetWindowHandle(this);
        WindowId windowId = Win32Interop.GetWindowIdFromWindow(windowHandle);
        AppWindow appWindow = AppWindow.GetFromWindowId(windowId);

        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMinimizable = false;
            presenter.IsMaximizable = false;
            presenter.IsResizable = false;
        }

        const int width = 520;
        const int height = 640;
        DisplayArea displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        int x = displayArea.WorkArea.X + ((displayArea.WorkArea.Width - width) / 2);
        int y = displayArea.WorkArea.Y + ((displayArea.WorkArea.Height - height) / 2);
        appWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, width, height));
    }

    private void SettingsWindow_Activated(object sender, Microsoft.UI.Xaml.WindowActivatedEventArgs args)
    {
        if (!_isRecordingShortcut)
        {
            return;
        }

        RecordShortcutButton.Content = "Press shortcut...";
    }

    private void InitializeView()
    {
        _isLoading = true;
        try
        {
            _settings = _clipboardStateService.Settings;
            LaunchAtLoginToggle.IsOn = _settings.LaunchAtLogin;
            IncognitoModeToggle.IsOn = _settings.IncognitoMode;
            HistoryLimitToggle.IsOn = _settings.EnableHistoryLimit;
            MaxHistoryNumberBox.Minimum = 10;
            MaxHistoryNumberBox.Maximum = 500;
            MaxHistoryNumberBox.Value = Math.Clamp(_settings.MaxHistoryItems, 10, 500);
            MaxHistoryNumberBox.IsEnabled = _settings.EnableHistoryLimit;
            RecordShortcutButton.Content = _settings.KeyboardShortcut.DisplayString;
            int clampedHistory = (int)Math.Clamp(_settings.MaxHistoryItems, 10, 500);
            HistoryLimitDescription.Text = $"Keep up to {clampedHistory} items in history.";

            PackageVersion version = Package.Current.Id.Version;
            VersionText.Text = $"Version {version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
        }
        finally
        {
            _isLoading = false;
        }
    }

    private async void LaunchAtLoginToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        bool enabled = LaunchAtLoginToggle.IsOn;
        Result registrationResult = LaunchAtLoginRegistry.SetEnabled(enabled);
        if (registrationResult.IsFailure)
        {
            _logger?.LogWarning(registrationResult.Error?.Exception, "Failed to set launch at login. Code {ErrorCode}: {Message}", registrationResult.Error?.Code, registrationResult.Error?.Message);
            await ShowMessageAsync("Launch at Login", "Unable to apply launch-at-login at OS level. Setting value was not changed.");
            _isLoading = true;
            LaunchAtLoginToggle.IsOn = _settings.LaunchAtLogin;
            _isLoading = false;
            return;
        }

        await SaveSettingsAsync(_settings with { LaunchAtLogin = enabled });
    }

    private void RecordShortcutButton_Click(object sender, RoutedEventArgs e)
    {
        _isRecordingShortcut = !_isRecordingShortcut;
        RecordShortcutButton.Content = _isRecordingShortcut ? "Press shortcut..." : _settings.KeyboardShortcut.DisplayString;
    }

    private async void ResetShortcutButton_Click(object sender, RoutedEventArgs e)
    {
        _isRecordingShortcut = false;
        await SaveShortcutAsync(KeyboardShortcut.Default);
    }

    private void RootGrid_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!_isRecordingShortcut)
        {
            return;
        }

        ShortcutModifiers modifiers = ReadModifiers();
        if (modifiers == ShortcutModifiers.None)
        {
            return;
        }

        VirtualKey key = e.Key;
        if (IsModifierKey(key))
        {
            return;
        }

        uint keyCode = (uint)key;
        string display = BuildShortcutDisplay(modifiers, key);
        KeyboardShortcut shortcut = new(keyCode, modifiers, display);
        _isRecordingShortcut = false;
        _ = SaveShortcutAsync(shortcut);
        e.Handled = true;
    }

    private async Task SaveShortcutAsync(KeyboardShortcut shortcut)
    {
        await SaveSettingsAsync(_settings with { KeyboardShortcut = shortcut });
        Result updateResult = await _globalHotkeyService.UpdateShortcutAsync(shortcut);
        if (updateResult.IsFailure)
        {
            _logger?.LogWarning(updateResult.Error?.Exception, "Failed to update runtime global shortcut. Code {ErrorCode}: {Message}", updateResult.Error?.Code, updateResult.Error?.Message);
            await ShowMessageAsync("Keyboard Shortcut", "Shortcut was saved but runtime registration failed. It may be already used by another app.");
        }
    }

    private async void HistoryLimitToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        bool enabled = HistoryLimitToggle.IsOn;
        MaxHistoryNumberBox.IsEnabled = enabled;
        await SaveSettingsAsync(_settings with { EnableHistoryLimit = enabled });
    }

    private async void MaxHistoryNumberBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_isLoading || double.IsNaN(args.NewValue))
        {
            return;
        }

        if (args.NewValue > 500)
        {
            sender.Value = 500;
            return;
        }

        int nextValue = (int)Math.Clamp(args.NewValue, 10, 500);
        HistoryLimitDescription.Text = $"Keep up to {nextValue} items in history.";

        if (_settings.MaxHistoryItems == nextValue)
        {
            return;
        }

        await SaveSettingsAsync(_settings with { MaxHistoryItems = nextValue });
    }

    private async void MaxHistoryNumberBox_KeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not NumberBox nb)
        {
            return;
        }

        TextBox? editor = FindChild<TextBox>(nb);
        string typedText = editor?.Text ?? nb.Text;
        if (!double.TryParse(typedText, out double typedValue) || (typedValue <= DomainLimits.MaxHistoryItemsHardLimit && typedValue >= 1))
        {
            return;
        }

        const int maxHistoryItems = DomainLimits.MaxHistoryItemsHardLimit;
        nb.Value = maxHistoryItems;
        HistoryLimitDescription.Text = $"Keep up to {maxHistoryItems} items in history.";

        if (editor is not null)
        {
            editor.Text = maxHistoryItems.ToString();
            editor.Select(editor.Text.Length, 0);
        }
        else
        {
            nb.Text = maxHistoryItems.ToString();
        }

        if (_settings.MaxHistoryItems != maxHistoryItems)
        {
            await SaveSettingsAsync(_settings with { MaxHistoryItems = maxHistoryItems });
        }
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent == null) return null;

        for (int i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
            {
                return typedChild;
            }

            T? result = FindChild<T>(child);
            if (result != null)
            {
                return result;
            }
        }
        return null;
    }

    private async void ClearHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        ContentDialog dialog = new()
        {
            XamlRoot = ResolveXamlRoot(),
            Title = "Clear Clipboard History",
            Content = "Are you sure you want to clear all clipboard history? This action cannot be undone.",
            PrimaryButtonText = "Clear",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        Result result = await _clipboardStateService.ClearClipboardHistoryAsync();
        if (result.IsFailure)
        {
            _logger?.LogWarning(result.Error?.Exception, "Failed to clear clipboard history from settings. Code {ErrorCode}: {Message}", result.Error?.Code, result.Error?.Message);
        }
    }

    private async void IncognitoModeToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        await SaveSettingsAsync(_settings with { IncognitoMode = IncognitoModeToggle.IsOn });
    }

    private async void ExcludedApplicationsButton_Click(object sender, RoutedEventArgs e)
    {
        HashSet<string> excluded = _settings.ExcludedAppIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Gather process info with window status and executable path
        var processInfos = new List<(string Name, bool HasWindow, string? ExePath, string? WindowTitle)>();
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (Process proc in Process.GetProcesses())
        {
            try
            {
                string name = proc.ProcessName;
                if (string.IsNullOrWhiteSpace(name)
                    || string.Equals(name, "Idle", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "System", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "ClipPocketWin", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!seen.Add(name))
                {
                    continue;
                }

                bool hasWindow = proc.MainWindowHandle != IntPtr.Zero
                    && !string.IsNullOrWhiteSpace(proc.MainWindowTitle);
                string? windowTitle = hasWindow ? proc.MainWindowTitle : null;
                string? exePath = null;
                try { exePath = proc.MainModule?.FileName; } catch { /* access denied */ }

                processInfos.Add((name, hasWindow, exePath, windowTitle));
            }
            catch
            {
                // Ignore inaccessible processes
            }
        }

        // Sort: apps with visible windows first, then background, each alphabetically
        processInfos = processInfos
            .OrderByDescending(p => p.HasWindow)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        StackPanel appListPanel = new() { Spacing = 6 };
        List<(CheckBox CheckBox, string Name)> checkBoxes = [];
        bool addedSeparator = false;

        foreach (var info in processInfos)
        {
            // Add separator between windowed apps and background processes
            if (!info.HasWindow && !addedSeparator)
            {
                addedSeparator = true;
                appListPanel.Children.Add(new Border
                {
                    Height = 1,
                    Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                        Windows.UI.Color.FromArgb(40, 255, 255, 255)),
                    Margin = new Thickness(0, 4, 0, 4)
                });
            }

            // Try to extract icon
            BitmapImage? iconImage = null;
            if (!string.IsNullOrWhiteSpace(info.ExePath))
            {
                try
                {
                    string iconCacheDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "ClipPocketWin", "cache", "excluded-icons");
                    Directory.CreateDirectory(iconCacheDir);
                    string safeFileName = Convert.ToHexString(
                        System.Security.Cryptography.SHA256.HashData(
                            System.Text.Encoding.UTF8.GetBytes(info.ExePath)))[..16] + ".png";
                    string iconPngPath = Path.Combine(iconCacheDir, safeFileName);

                    if (!File.Exists(iconPngPath))
                    {
                        using System.Drawing.Icon? icon = System.Drawing.Icon.ExtractAssociatedIcon(info.ExePath);
                        if (icon is not null)
                        {
                            using System.Drawing.Bitmap bmp = icon.ToBitmap();
                            bmp.Save(iconPngPath, System.Drawing.Imaging.ImageFormat.Png);
                        }
                    }

                    if (File.Exists(iconPngPath))
                    {
                        iconImage = new BitmapImage(new Uri(iconPngPath));
                    }
                }
                catch { /* icon extraction failed, continue without icon */ }
            }

            // Build row: [icon] [checkbox with name + optional count]
            Grid row = new();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            if (iconImage is not null)
            {
                Image img = new()
                {
                    Source = iconImage,
                    Width = 22,
                    Height = 22,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Left
                };
                Grid.SetColumn(img, 0);
                row.Children.Add(img);
            }
            else
            {
                FontIcon fallbackIcon = new()
                {
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons"),
                    Glyph = "\uE8FC",
                    FontSize = 18,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                        Windows.UI.Color.FromArgb(150, 255, 255, 255)),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Left
                };
                Grid.SetColumn(fallbackIcon, 0);
                row.Children.Add(fallbackIcon);
            }

            string displayLabel = info.WindowTitle is not null
                ? $"{info.Name}  —  {info.WindowTitle}"
                : info.Name;

            CheckBox checkBox = new()
            {
                Content = displayLabel,
                IsChecked = excluded.Contains(info.Name),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(checkBox, 1);
            row.Children.Add(checkBox);

            checkBoxes.Add((checkBox, info.Name));
            appListPanel.Children.Add(row);
        }

        TextBox manualBox = new()
        {
            PlaceholderText = "Add process names manually (comma/new line)",
            AcceptsReturn = true,
            MinHeight = 80
        };

        StackPanel content = new() { Spacing = 12 };
        content.Children.Add(new TextBlock
        {
            Text = "Clipboard monitoring will be paused for excluded apps.",
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(new ScrollViewer
        {
            MaxHeight = 340,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = appListPanel
        });
        content.Children.Add(manualBox);

        ContentDialog dialog = new()
        {
            XamlRoot = ResolveXamlRoot(),
            Title = "Excluded Applications",
            Content = content,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel"
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        HashSet<string> nextExcluded = checkBoxes
            .Where(entry => entry.CheckBox.IsChecked == true)
            .Select(entry => entry.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        string[] manualEntries = manualBox.Text
            .Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string entry in manualEntries)
        {
            if (!string.IsNullOrWhiteSpace(entry))
            {
                nextExcluded.Add(entry);
            }
        }

        await SaveSettingsAsync(_settings with { ExcludedAppIds = nextExcluded });
    }

    private async void CheckForUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        await ShowMessageAsync("Check for Updates", "Update checker placeholder: this button is currently non-operational.");
    }

    private async Task SaveSettingsAsync(ClipPocketSettings nextSettings)
    {
        Result result = await _clipboardStateService.SaveSettingsAsync(nextSettings);
        if (result.IsFailure)
        {
            _logger?.LogWarning(result.Error?.Exception, "Failed to save settings. Code {ErrorCode}: {Message}", result.Error?.Code, result.Error?.Message);
            await ShowMessageAsync("Settings", "Failed to save settings. Please try again.");
            InitializeView();
            return;
        }

        _settings = _clipboardStateService.Settings;
        RecordShortcutButton.Content = _settings.KeyboardShortcut.DisplayString;
    }

    private async Task ShowMessageAsync(string title, string content)
    {
        ContentDialog dialog = new()
        {
            XamlRoot = ResolveXamlRoot(),
            Title = title,
            Content = content,
            CloseButtonText = "OK"
        };

        _ = await dialog.ShowAsync();
    }

    private XamlRoot ResolveXamlRoot()
    {
        if (Content is FrameworkElement element && element.XamlRoot is not null)
        {
            return element.XamlRoot;
        }

        throw new InvalidOperationException("Settings window content does not expose a XamlRoot.");
    }

    private static bool IsModifierKey(VirtualKey key)
    {
        return key is VirtualKey.Control or VirtualKey.LeftControl or VirtualKey.RightControl
            or VirtualKey.Menu or VirtualKey.LeftMenu or VirtualKey.RightMenu
            or VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift
            or VirtualKey.LeftWindows or VirtualKey.RightWindows;
    }

    private static ShortcutModifiers ReadModifiers()
    {
        ShortcutModifiers modifiers = ShortcutModifiers.None;
        if (IsKeyDown(VirtualKey.Control) || IsKeyDown(VirtualKey.LeftControl) || IsKeyDown(VirtualKey.RightControl))
        {
            modifiers |= ShortcutModifiers.Control;
        }

        if (IsKeyDown(VirtualKey.Menu) || IsKeyDown(VirtualKey.LeftMenu) || IsKeyDown(VirtualKey.RightMenu))
        {
            modifiers |= ShortcutModifiers.Alt;
        }

        if (IsKeyDown(VirtualKey.Shift) || IsKeyDown(VirtualKey.LeftShift) || IsKeyDown(VirtualKey.RightShift))
        {
            modifiers |= ShortcutModifiers.Shift;
        }

        if (IsKeyDown(VirtualKey.LeftWindows) || IsKeyDown(VirtualKey.RightWindows))
        {
            modifiers |= ShortcutModifiers.Windows;
        }

        return modifiers;
    }

    private static bool IsKeyDown(VirtualKey key)
    {
        Windows.UI.Core.CoreVirtualKeyStates state = InputKeyboardSource.GetKeyStateForCurrentThread(key);
        return state.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
    }

    private static string BuildShortcutDisplay(ShortcutModifiers modifiers, VirtualKey key)
    {
        List<string> parts = [];
        if (modifiers.HasFlag(ShortcutModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (modifiers.HasFlag(ShortcutModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (modifiers.HasFlag(ShortcutModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (modifiers.HasFlag(ShortcutModifiers.Windows))
        {
            parts.Add("Win");
        }

        parts.Add(GetKeyLabel(key));
        return string.Join('+', parts);
    }

    private static string GetKeyLabel(VirtualKey key)
    {
        uint keyCode = (uint)key;
        if (keyCode is 0xBA or 0xC0)
        {
            return "ò";
        }

        if (key is >= VirtualKey.Number0 and <= VirtualKey.Number9)
        {
            int number = (int)key - (int)VirtualKey.Number0;
            return number.ToString();
        }

        if (key is >= VirtualKey.A and <= VirtualKey.Z)
        {
            char c = (char)('A' + ((int)key - (int)VirtualKey.A));
            return c.ToString();
        }

        return key.ToString();
    }
}
