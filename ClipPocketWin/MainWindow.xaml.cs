using ClipPocketWin.Application.Abstractions;
using ClipPocketWin.Domain.Models;
using ClipPocketWin.Shared.ResultPattern;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using WinRT;
using WinRT.Interop;

namespace ClipPocketWin
{
    public sealed partial class MainWindow : Window
    {
        private const float MinTintOpacity = 0.18f;
        private const float MaxTintOpacity = 0.79f;
        private const float MinLuminosityOpacity = 0.00f;
        private const float MaxLuminosityOpacity = 0.52f;
        private const float MaxAdditionalContrastTint = 0.22f;
        private const double LuminanceProtectionThreshold = 0.62;
        private const double LuminanceFallbackValue = 0.74;
        private const double LuminanceSmoothing = 0.62;
        private const int SamplingPadding = 8;
        private const int ReadabilityUpdateIntervalMs = 260;
        private const double CardHoverScale = 1.03;
        private const double CardHoverAnimationDurationMs = 120;
        private static readonly Windows.UI.Color BaseTintColor = Windows.UI.Color.FromArgb(255, 15, 20, 50);
        private static readonly Windows.UI.Color StrongProtectionTintColor = Windows.UI.Color.FromArgb(255, 1, 4, 12);

        private readonly AppWindow m_AppWindow;
        private readonly IClipboardStateService _clipboardStateService;
        private readonly IQuickActionsService _quickActionsService;
        private readonly IWindowPanelService _windowPanelService;
        private readonly ILogger<MainWindow>? _logger;
        private readonly ObservableCollection<ClipboardCardViewModel> _clipboardCards = [];
        private string _searchText = string.Empty;
        private ClipboardSection _selectedSection = ClipboardSection.Recent;
        private ClipboardItemType? _selectedTypeFilter;
        private DesktopAcrylicController? _acrylicController;
        private SystemBackdropConfiguration? _configurationSource;
        private DispatcherQueueTimer? _readabilityTimer;
        private DispatcherQueueTimer? _relativeTimeTimer;
        private double _smoothedBackdropLuminance = LuminanceFallbackValue;
        private bool _hasBackdropSample;
        private bool _isBackdropSampling;

        public MainWindow()
        {
            InitializeComponent();

            App app = (App)Microsoft.UI.Xaml.Application.Current;
            _clipboardStateService = app.Services.GetRequiredService<IClipboardStateService>();
            _quickActionsService = app.Services.GetRequiredService<IQuickActionsService>();
            _windowPanelService = app.Services.GetRequiredService<IWindowPanelService>();
            _logger = app.Services.GetService<ILogger<MainWindow>>();
            _clipboardStateService.StateChanged += ClipboardStateService_StateChanged;
            ClipboardItemsListView.ItemsSource = _clipboardCards;
            RefreshClipboardCards();
            UpdateSectionButtons();
            StartRelativeTimeUpdates();

            IntPtr hWnd = WindowNative.GetWindowHandle(this);
            WindowId wndId = Win32Interop.GetWindowIdFromWindow(hWnd);
            m_AppWindow = AppWindow.GetFromWindowId(wndId);

            // Extend content into title bar for seamless glass effect
            m_AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
            m_AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
            m_AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            m_AppWindow.TitleBar.ButtonForegroundColor = Colors.White;
            m_AppWindow.TitleBar.ButtonInactiveForegroundColor = Colors.Transparent;

            OverlappedPresenter? presenter = m_AppWindow.Presenter as OverlappedPresenter;
            if (presenter != null)
            {
                presenter.IsResizable = true;
                presenter.IsMaximizable = true;
                presenter.IsMinimizable = true;
                presenter.IsAlwaysOnTop = false;
            }

            // Size the window
            DisplayArea displayArea = DisplayArea.GetFromWindowId(wndId, DisplayAreaFallback.Primary);
            int windowWidth = 1100;
            int windowHeight = 420;
            int x = (displayArea.WorkArea.Width - windowWidth) / 2;
            int y = (displayArea.WorkArea.Height - windowHeight) / 2;
            m_AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, windowWidth, windowHeight));

            // Apply true glass blur with maximum transparency
            if (TrySetAcrylicBackdrop())
            {
                StartReadabilityMonitoring();
            }

            Activated += Window_Activated;
            Closed += Window_Closed;
        }

        private bool TrySetAcrylicBackdrop()
        {
            if (!DesktopAcrylicController.IsSupported())
            {
                return false;
            }

            _configurationSource = new SystemBackdropConfiguration
            {
                Theme = SystemBackdropTheme.Dark,
                IsInputActive = true
            };

            _acrylicController = new DesktopAcrylicController
            {
                // ── KEY SETTINGS FOR REAL GLASS TRANSPARENCY ──
                // TintColor: the color overlay on the blurred background
                // TintOpacity: 0 = no tint (fully transparent), 1 = solid tint
                // LuminosityOpacity: 0 = maximum see-through, 1 = opaque luminosity layer
                TintColor = BaseTintColor,
                TintOpacity = MinTintOpacity,
                LuminosityOpacity = MinLuminosityOpacity,
                FallbackColor = Windows.UI.Color.FromArgb(200, BaseTintColor.R, BaseTintColor.G, BaseTintColor.B)
            };

            // Attach to the window
            _acrylicController.AddSystemBackdropTarget(
                this.As<ICompositionSupportsSystemBackdrop>());
            _acrylicController.SetSystemBackdropConfiguration(_configurationSource);

            _ = RefreshBackdropProtectionAsync();

            return true;
        }

        private void StartReadabilityMonitoring()
        {
            _readabilityTimer ??= DispatcherQueue.CreateTimer();
            _readabilityTimer.Interval = TimeSpan.FromMilliseconds(ReadabilityUpdateIntervalMs);
            _readabilityTimer.IsRepeating = true;
            _readabilityTimer.Tick += ReadabilityTimer_Tick;
            _readabilityTimer.Start();
        }

        private void StopReadabilityMonitoring()
        {
            if (_readabilityTimer == null)
            {
                return;
            }

            _readabilityTimer.Tick -= ReadabilityTimer_Tick;
            _readabilityTimer.Stop();
            _readabilityTimer = null;
        }

        private void StartRelativeTimeUpdates()
        {
            _relativeTimeTimer ??= DispatcherQueue.CreateTimer();
            _relativeTimeTimer.Interval = TimeSpan.FromSeconds(1);
            _relativeTimeTimer.IsRepeating = true;
            _relativeTimeTimer.Tick += RelativeTimeTimer_Tick;
            _relativeTimeTimer.Start();
        }

        private void StopRelativeTimeUpdates()
        {
            if (_relativeTimeTimer == null)
            {
                return;
            }

            _relativeTimeTimer.Tick -= RelativeTimeTimer_Tick;
            _relativeTimeTimer.Stop();
            _relativeTimeTimer = null;
        }

        private void RelativeTimeTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            UpdateVisibleCardRelativeTimes();
        }

        private void UpdateVisibleCardRelativeTimes()
        {
            if (_clipboardCards.Count == 0)
            {
                return;
            }

            foreach (ClipboardCardViewModel card in _clipboardCards)
            {
                card.RefreshRelativeTime();
            }
        }

        private void ReadabilityTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            _ = RefreshBackdropProtectionAsync();
        }

        private async void ClipboardCard_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (sender is not Border { Tag: Guid itemId })
            {
                return;
            }

            _logger?.LogInformation("Double-click selection started for item {ItemId}.", itemId);
            await PasteAndHideAsync(itemId);
        }

        private async void ClipboardCard_DragStarting(UIElement sender, DragStartingEventArgs args)
        {
            if (sender is not FrameworkElement { Tag: Guid itemId })
            {
                return;
            }

            ClipboardItem? item = ResolveClipboardItem(itemId);
            if (item is null)
            {
                return;
            }

            args.Data.RequestedOperation = DataPackageOperation.Copy;
            string? textPayload = ResolveTextPayload(item);

            switch (item.Type)
            {
                case ClipboardItemType.File:
                    {
                        if (!string.IsNullOrWhiteSpace(item.FilePath) && File.Exists(item.FilePath))
                        {
                            StorageFile storageFile = await StorageFile.GetFileFromPathAsync(item.FilePath);
                            args.Data.SetStorageItems([storageFile]);
                            return;
                        }

                        break;
                    }
                case ClipboardItemType.Image:
                    {
                        if (item.BinaryContent is { Length: > 0 } binaryContent)
                        {
                            string? dragImagePath = BuildDragImagePath(binaryContent);
                            if (!string.IsNullOrWhiteSpace(dragImagePath) && File.Exists(dragImagePath))
                            {
                                StorageFile storageFile = await StorageFile.GetFileFromPathAsync(dragImagePath);
                                args.Data.SetStorageItems([storageFile]);
                                return;
                            }
                        }

                        break;
                    }
            }

            if (!string.IsNullOrWhiteSpace(textPayload))
            {
                args.Data.SetText(textPayload);
            }
        }

        private void ClipboardStateService_StateChanged(object? sender, EventArgs e)
        {
            if (DispatcherQueue.HasThreadAccess)
            {
                RefreshClipboardCards();
                UpdateSectionButtons();
                return;
            }

            _ = DispatcherQueue.TryEnqueue(() =>
            {
                RefreshClipboardCards();
                UpdateSectionButtons();
            });
        }

        private void RefreshClipboardCards()
        {
            List<ClipboardCardViewModel> nextCards = BuildFilteredCards();

            const int maxCards = 80;
            if (nextCards.Count > maxCards)
            {
                nextCards = nextCards.Take(maxCards).ToList();
            }

            _clipboardCards.Clear();
            foreach (ClipboardCardViewModel card in nextCards)
            {
                _clipboardCards.Add(card);
            }

            bool hasCards = _clipboardCards.Count > 0;
            ClipboardItemsListView.Visibility = hasCards ? Visibility.Visible : Visibility.Collapsed;
            SampleCardsScrollViewer.Visibility = hasCards ? Visibility.Collapsed : Visibility.Visible;
        }

        private List<ClipboardCardViewModel> BuildFilteredCards()
        {
            IEnumerable<ClipboardItem> sourceItems = _selectedSection switch
            {
                ClipboardSection.Pinned => _clipboardStateService.PinnedItems.Select(x => x.OriginalItem),
                ClipboardSection.History => _clipboardStateService.ClipboardItems,
                _ => _clipboardStateService.ClipboardItems.Take(20)
            };

            if (_selectedTypeFilter is ClipboardItemType selectedType)
            {
                sourceItems = sourceItems.Where(item => item.Type == selectedType);
            }

            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                sourceItems = sourceItems.Where(item => IsFuzzyMatch(_searchText, item.DisplayString));
            }

            HashSet<Guid> pinnedIds = _clipboardStateService.PinnedItems
                .Select(x => x.OriginalItem.Id)
                .ToHashSet();

            return sourceItems
                .Select(item => CreateCardViewModel(item, pinnedIds.Contains(item.Id)))
                .ToList();
        }

        private static bool IsFuzzyMatch(string query, string text)
        {
            string normalizedQuery = query.Trim().ToLowerInvariant();
            string normalizedText = text.ToLowerInvariant();
            if (normalizedQuery.Length == 0)
            {
                return true;
            }

            if (normalizedText.Contains(normalizedQuery, StringComparison.Ordinal))
            {
                return true;
            }

            int queryIndex = 0;
            for (int i = 0; i < normalizedText.Length && queryIndex < normalizedQuery.Length; i++)
            {
                if (normalizedText[i] == normalizedQuery[queryIndex])
                {
                    queryIndex++;
                }
            }

            return queryIndex == normalizedQuery.Length;
        }

        private void UpdateSectionButtons()
        {
            int pinnedCount = _clipboardStateService.PinnedItems.Count;
            int recentCount = Math.Min(20, _clipboardStateService.ClipboardItems.Count);
            int historyCount = _clipboardStateService.ClipboardItems.Count;

            PinnedTabButton.Content = $"Pinned ({pinnedCount})";
            RecentTabButton.Content = $"Recent ({recentCount})";
            HistoryTabButton.Content = $"History ({historyCount})";

            PinnedTabButton.Opacity = _selectedSection == ClipboardSection.Pinned ? 1 : 0.75;
            RecentTabButton.Opacity = _selectedSection == ClipboardSection.Recent ? 1 : 0.75;
            HistoryTabButton.Opacity = _selectedSection == ClipboardSection.History ? 1 : 0.75;
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchText = SearchTextBox.Text ?? string.Empty;
            RefreshClipboardCards();
        }

        private void SectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string tag })
            {
                return;
            }

            _selectedSection = tag switch
            {
                "Pinned" => ClipboardSection.Pinned,
                "History" => ClipboardSection.History,
                _ => ClipboardSection.Recent
            };

            UpdateSectionButtons();
            RefreshClipboardCards();
        }

        private void TypeFilterButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string tag })
            {
                return;
            }

            _selectedTypeFilter = tag switch
            {
                "All" => null,
                "Text" => ClipboardItemType.Text,
                "Code" => ClipboardItemType.Code,
                "Url" => ClipboardItemType.Url,
                "Email" => ClipboardItemType.Email,
                "Phone" => ClipboardItemType.Phone,
                "Json" => ClipboardItemType.Json,
                "Color" => ClipboardItemType.Color,
                "Image" => ClipboardItemType.Image,
                "File" => ClipboardItemType.File,
                "RichText" => ClipboardItemType.RichText,
                _ => null
            };

            RefreshClipboardCards();
        }

        private async void ClipboardCard_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: Guid itemId })
            {
                return;
            }

            ClipboardItem? item = ResolveClipboardItem(itemId);
            if (item is null)
            {
                return;
            }

            MenuFlyout flyout = BuildCardFlyout(item);
            flyout.ShowAt((FrameworkElement)sender);
        }

        private MenuFlyout BuildCardFlyout(ClipboardItem item)
        {
            MenuFlyout flyout = new();

            MenuFlyoutItem copyItem = new() { Text = "Copy" };
            copyItem.Click += async (_, _) => await CopyOnlyAsync(item.Id);
            flyout.Items.Add(copyItem);

            MenuFlyoutSubItem quickActionsMenu = new() { Text = "Quick Actions" };

            MenuFlyoutItem saveToFileItem = new() { Text = "Save to File" };
            saveToFileItem.Click += async (_, _) => await SaveToFileAsync(item);
            quickActionsMenu.Items.Add(saveToFileItem);

            MenuFlyoutItem copyBase64Item = new() { Text = "Copy as Base64" };
            copyBase64Item.Click += async (_, _) => await CopyAsBase64Async(item);
            quickActionsMenu.Items.Add(copyBase64Item);

            MenuFlyoutItem urlEncodeItem = new() { Text = "URL Encode" };
            urlEncodeItem.Click += async (_, _) => await UrlEncodeAsync(item);
            quickActionsMenu.Items.Add(urlEncodeItem);

            MenuFlyoutItem urlDecodeItem = new() { Text = "URL Decode" };
            urlDecodeItem.Click += async (_, _) => await UrlDecodeAsync(item);
            quickActionsMenu.Items.Add(urlDecodeItem);

            flyout.Items.Add(quickActionsMenu);

            bool isPinned = _clipboardStateService.PinnedItems.Any(x => x.OriginalItem.Id == item.Id);
            MenuFlyoutItem pinToggleItem = new() { Text = isPinned ? "Unpin" : "Pin" };
            pinToggleItem.Click += async (_, _) => await TogglePinAsync(item);
            flyout.Items.Add(pinToggleItem);

            MenuFlyoutItem deleteItem = new() { Text = "Delete" };
            deleteItem.Click += async (_, _) => await DeleteClipboardItemAsync(item.Id);
            flyout.Items.Add(deleteItem);

            flyout.Items.Add(new MenuFlyoutSeparator());

            MenuFlyoutItem clearHistoryItem = new() { Text = "Clear History" };
            clearHistoryItem.Click += async (_, _) => await ClearHistoryAsync();
            flyout.Items.Add(clearHistoryItem);

            return flyout;
        }

        private ClipboardItem? ResolveClipboardItem(Guid id)
        {
            return _clipboardStateService.ClipboardItems.FirstOrDefault(x => x.Id == id)
                ?? _clipboardStateService.PinnedItems.Select(x => x.OriginalItem).FirstOrDefault(x => x.Id == id);
        }

        private async Task CopyOnlyAsync(Guid itemId)
        {
            Result copyResult = await _clipboardStateService.CopyClipboardItemAsync(itemId);
            if (copyResult.IsFailure)
            {
                _logger?.LogWarning(
                    copyResult.Error?.Exception,
                    "Clipboard copy failed for item {ItemId}. Code {ErrorCode}: {Message}",
                    itemId,
                    copyResult.Error?.Code,
                    copyResult.Error?.Message);
            }
        }

        private async Task PasteAndHideAsync(Guid itemId)
        {
            Result hideResult = await _windowPanelService.HideAsync();
            _logger?.LogInformation("Double-click hide requested for item {ItemId}. Success={Success}", itemId, hideResult.IsSuccess);
            if (hideResult.IsFailure)
            {
                _logger?.LogWarning(
                    hideResult.Error?.Exception,
                    "Failed to hide panel before clipboard paste. Item {ItemId}. Code {ErrorCode}: {Message}",
                    itemId,
                    hideResult.Error?.Code,
                    hideResult.Error?.Message);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(60));

            _logger?.LogInformation("Double-click paste requested for item {ItemId}.", itemId);
            Result pasteResult = await _clipboardStateService.PasteClipboardItemAsync(itemId);
            if (pasteResult.IsFailure)
            {
                _logger?.LogWarning(
                    pasteResult.Error?.Exception,
                    "Clipboard paste failed for item {ItemId}. Code {ErrorCode}: {Message}",
                    itemId,
                    pasteResult.Error?.Code,
                    pasteResult.Error?.Message);
                return;
            }

            _logger?.LogInformation("Double-click paste completed for item {ItemId}.", itemId);
        }

        private async Task SaveToFileAsync(ClipboardItem item)
        {
            nint windowHandle = WindowNative.GetWindowHandle(this);
            Result result = await _quickActionsService.SaveToFileAsync(item, windowHandle);
            if (result.IsFailure)
            {
                _logger?.LogWarning(result.Error?.Exception, "Quick action Save to file failed. Code {ErrorCode}: {Message}", result.Error?.Code, result.Error?.Message);
            }
        }

        private async Task CopyAsBase64Async(ClipboardItem item)
        {
            Result result = await _quickActionsService.CopyAsBase64Async(item);
            if (result.IsFailure)
            {
                _logger?.LogWarning(result.Error?.Exception, "Quick action Copy as Base64 failed. Code {ErrorCode}: {Message}", result.Error?.Code, result.Error?.Message);
            }
        }

        private async Task UrlEncodeAsync(ClipboardItem item)
        {
            Result result = await _quickActionsService.UrlEncodeAsync(item);
            if (result.IsFailure)
            {
                _logger?.LogWarning(result.Error?.Exception, "Quick action URL Encode failed. Code {ErrorCode}: {Message}", result.Error?.Code, result.Error?.Message);
            }
        }

        private async Task UrlDecodeAsync(ClipboardItem item)
        {
            Result result = await _quickActionsService.UrlDecodeAsync(item);
            if (result.IsFailure)
            {
                _logger?.LogWarning(result.Error?.Exception, "Quick action URL Decode failed. Code {ErrorCode}: {Message}", result.Error?.Code, result.Error?.Message);
            }
        }

        private async Task TogglePinAsync(ClipboardItem item)
        {
            Result toggleResult = await _clipboardStateService.TogglePinAsync(item);
            if (toggleResult.IsFailure)
            {
                _logger?.LogWarning(toggleResult.Error?.Exception, "Failed to toggle pin. Code {ErrorCode}: {Message}", toggleResult.Error?.Code, toggleResult.Error?.Message);
            }
        }

        private async Task DeleteClipboardItemAsync(Guid itemId)
        {
            Result deleteResult = await _clipboardStateService.DeleteClipboardItemAsync(itemId);
            if (deleteResult.IsFailure)
            {
                _logger?.LogWarning(deleteResult.Error?.Exception, "Failed to delete clipboard item. Code {ErrorCode}: {Message}", deleteResult.Error?.Code, deleteResult.Error?.Message);
            }
        }

        private async void ClearHistoryButton_Click(object sender, RoutedEventArgs e)
        {
            await ClearHistoryAsync();
        }

        private async Task ClearHistoryAsync()
        {
            ContentDialog dialog = new()
            {
                XamlRoot = ResolveXamlRoot(),
                Title = "Clear Clipboard History",
                Content = "Are you sure you want to clear all clipboard history? This action cannot be undone.",
                PrimaryButtonText = "Clear All",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close
            };

            ContentDialogResult dialogResult = await dialog.ShowAsync();
            if (dialogResult != ContentDialogResult.Primary)
            {
                return;
            }

            Result clearResult = await _clipboardStateService.ClearClipboardHistoryAsync();
            if (clearResult.IsFailure)
            {
                _logger?.LogWarning(clearResult.Error?.Exception, "Failed to clear clipboard history. Code {ErrorCode}: {Message}", clearResult.Error?.Code, clearResult.Error?.Message);
            }
        }

        private async void ExcludedAppsButton_Click(object sender, RoutedEventArgs e)
        {
            TextBox editor = new()
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 220,
                Text = string.Join(Environment.NewLine, _clipboardStateService.Settings.ExcludedAppIds.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            };

            ContentDialog dialog = new()
            {
                XamlRoot = ResolveXamlRoot(),
                Title = "Excluded Applications",
                Content = editor,
                PrimaryButtonText = "Save",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary
            };

            ContentDialogResult result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return;
            }

            HashSet<string> excludedIds = editor.Text
                .Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            ClipPocketSettings nextSettings = _clipboardStateService.Settings with { ExcludedAppIds = excludedIds };
            Result saveResult = await _clipboardStateService.SaveSettingsAsync(nextSettings);
            if (saveResult.IsFailure)
            {
                _logger?.LogWarning(saveResult.Error?.Exception, "Failed to save excluded app ids. Code {ErrorCode}: {Message}", saveResult.Error?.Code, saveResult.Error?.Message);
            }
        }

        private XamlRoot ResolveXamlRoot()
        {
            if (Content is FrameworkElement rootElement && rootElement.XamlRoot is not null)
            {
                return rootElement.XamlRoot;
            }

            throw new InvalidOperationException("Main window content does not expose a XamlRoot.");
        }

        private static ClipboardCardViewModel CreateCardViewModel(ClipboardItem item, bool isPinned)
        {
            SourceAppVisual sourceAppVisual = SourceAppIconCache.Resolve(item.SourceApplicationExecutablePath, item.SourceApplicationIdentifier);
            ClipboardCardStyle style = GetCardStyle(item, sourceAppVisual.VibrantColor);
            BitmapImage? previewImage = item.Type == ClipboardItemType.Image
                ? ImagePreviewCache.Resolve(item.BinaryContent)
                : null;
            Visibility imagePreviewVisibility = previewImage is null ? Visibility.Collapsed : Visibility.Visible;
            Visibility textPreviewVisibility = previewImage is null ? Visibility.Visible : Visibility.Collapsed;

            return new ClipboardCardViewModel(
                item.Id,
                GetTypeLabel(item.Type),
                item.Timestamp,
                GetPreviewText(item),
                GetGlyphForType(item.Type),
                isPinned ? "Pinned" : string.Empty,
                sourceAppVisual.Icon,
                sourceAppVisual.HasIcon ? Visibility.Visible : Visibility.Collapsed,
                sourceAppVisual.HasIcon ? Visibility.Collapsed : Visibility.Visible,
                previewImage,
                imagePreviewVisibility,
                textPreviewVisibility,
                style.CardBackgroundBrush,
                style.HeaderBackgroundBrush,
                style.IconBackgroundBrush,
                style.IconForegroundBrush);
        }

        private static ClipboardCardStyle GetCardStyle(ClipboardItem item, Windows.UI.Color? sourceVibrantColor)
        {
            if (item.Type == ClipboardItemType.Color && TryParseHexColor(item.TextContent, out Windows.UI.Color parsedColor))
            {
                return BuildCardStyle(parsedColor, Windows.UI.Color.FromArgb(255, 255, 255, 255));
            }

            if (sourceVibrantColor is Windows.UI.Color iconAccent)
            {
                return BuildCardStyle(iconAccent, Windows.UI.Color.FromArgb(255, 255, 255, 255));
            }

            return item.Type switch
            {
                ClipboardItemType.Code => BuildCardStyle(Windows.UI.Color.FromArgb(255, 122, 92, 255), Windows.UI.Color.FromArgb(255, 219, 205, 255)),
                ClipboardItemType.Url => BuildCardStyle(Windows.UI.Color.FromArgb(255, 59, 130, 246), Windows.UI.Color.FromArgb(255, 191, 219, 254)),
                ClipboardItemType.Email => BuildCardStyle(Windows.UI.Color.FromArgb(255, 6, 182, 212), Windows.UI.Color.FromArgb(255, 165, 243, 252)),
                ClipboardItemType.Phone => BuildCardStyle(Windows.UI.Color.FromArgb(255, 34, 197, 94), Windows.UI.Color.FromArgb(255, 187, 247, 208)),
                ClipboardItemType.Json => BuildCardStyle(Windows.UI.Color.FromArgb(255, 22, 163, 74), Windows.UI.Color.FromArgb(255, 187, 247, 208)),
                ClipboardItemType.Color => BuildCardStyle(Windows.UI.Color.FromArgb(255, 168, 85, 247), Windows.UI.Color.FromArgb(255, 233, 213, 255)),
                ClipboardItemType.Image => BuildCardStyle(Windows.UI.Color.FromArgb(255, 14, 165, 233), Windows.UI.Color.FromArgb(255, 186, 230, 253)),
                ClipboardItemType.File => BuildCardStyle(Windows.UI.Color.FromArgb(255, 245, 158, 11), Windows.UI.Color.FromArgb(255, 254, 243, 199)),
                ClipboardItemType.RichText => BuildCardStyle(Windows.UI.Color.FromArgb(255, 139, 92, 246), Windows.UI.Color.FromArgb(255, 221, 214, 254)),
                _ => BuildCardStyle(Windows.UI.Color.FromArgb(255, 71, 85, 105), Windows.UI.Color.FromArgb(255, 226, 232, 240))
            };
        }

        private static ClipboardCardStyle BuildCardStyle(Windows.UI.Color accentColor, Windows.UI.Color iconColor)
        {
            return new ClipboardCardStyle(
                CreateCardBackgroundBrush(accentColor),
                new SolidColorBrush(Windows.UI.Color.FromArgb(76, accentColor.R, accentColor.G, accentColor.B)),
                new SolidColorBrush(Windows.UI.Color.FromArgb(102, accentColor.R, accentColor.G, accentColor.B)),
                new SolidColorBrush(iconColor));
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
                Color = Windows.UI.Color.FromArgb(82, accentColor.R, accentColor.G, accentColor.B),
                Offset = 0
            });

            brush.GradientStops.Add(new GradientStop
            {
                Color = Windows.UI.Color.FromArgb(56, 10, 15, 24),
                Offset = 1
            });

            return brush;
        }

        private static bool TryParseHexColor(string? value, out Windows.UI.Color color)
        {
            color = Windows.UI.Color.FromArgb(255, 168, 85, 247);
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string text = value.Trim();
            if (!text.StartsWith('#'))
            {
                return false;
            }

            string hex = text[1..];
            if (hex.Length == 3)
            {
                string r = new(hex[0], 2);
                string g = new(hex[1], 2);
                string b = new(hex[2], 2);
                return TryBuildColor("FF" + r + g + b, out color);
            }

            if (hex.Length == 6)
            {
                return TryBuildColor("FF" + hex, out color);
            }

            if (hex.Length == 8)
            {
                return TryBuildColor(hex, out color);
            }

            return false;
        }

        private static bool TryBuildColor(string argbHex, out Windows.UI.Color color)
        {
            color = Windows.UI.Color.FromArgb(255, 168, 85, 247);
            if (argbHex.Length != 8)
            {
                return false;
            }

            if (!byte.TryParse(argbHex[0..2], NumberStyles.HexNumber, provider: null, out byte a)
                || !byte.TryParse(argbHex[2..4], NumberStyles.HexNumber, provider: null, out byte r)
                || !byte.TryParse(argbHex[4..6], NumberStyles.HexNumber, provider: null, out byte g)
                || !byte.TryParse(argbHex[6..8], NumberStyles.HexNumber, provider: null, out byte b))
            {
                return false;
            }

            color = Windows.UI.Color.FromArgb(a, r, g, b);
            return true;
        }

        private static string? ResolveTextPayload(ClipboardItem item)
        {
            return item.Type switch
            {
                ClipboardItemType.Text or ClipboardItemType.Code or ClipboardItemType.Url or ClipboardItemType.Email or ClipboardItemType.Phone or ClipboardItemType.Json or ClipboardItemType.Color
                    => item.TextContent,
                ClipboardItemType.RichText
                    => item.RichTextContent?.PlainText ?? item.TextContent,
                ClipboardItemType.File
                    => item.FilePath ?? item.TextContent,
                _
                    => null
            };
        }

        private static string? BuildDragImagePath(byte[] dibPayload)
        {
            try
            {
                string dragCacheDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ClipPocketWin",
                    "cache",
                    "drag-images");

                Directory.CreateDirectory(dragCacheDirectory);

                string hash = Convert.ToHexString(SHA256.HashData(dibPayload));
                string bmpPath = Path.Combine(dragCacheDirectory, hash + ".bmp");
                if (File.Exists(bmpPath))
                {
                    return bmpPath;
                }

                byte[] payloadToWrite = dibPayload;
                if (TryBuildBitmapFromDib(dibPayload, out byte[]? bmpBytes) && bmpBytes is not null)
                {
                    payloadToWrite = bmpBytes;
                }

                File.WriteAllBytes(bmpPath, payloadToWrite);
                return bmpPath;
            }
            catch
            {
                return null;
            }
        }

        private static bool TryBuildBitmapFromDib(byte[] dibPayload, out byte[]? bmpBytes)
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

            int maskBytes = (compression == 3 || compression == 6) ? 12 : 0;
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

        private static string GetTypeLabel(ClipboardItemType type)
        {
            return type switch
            {
                ClipboardItemType.Url => "URL",
                ClipboardItemType.Json => "JSON",
                ClipboardItemType.RichText => "Rich Text",
                _ => type.ToString()
            };
        }

        private static string GetRelativeTimestampLabel(DateTimeOffset timestamp)
        {
            TimeSpan elapsed = DateTimeOffset.UtcNow - timestamp;
            if (elapsed <= TimeSpan.FromSeconds(2))
            {
                return "Now";
            }

            if (elapsed < TimeSpan.FromMinutes(1))
            {
                return $"{(int)elapsed.TotalSeconds} sec";
            }

            if (elapsed < TimeSpan.FromHours(1))
            {
                int minutes = (int)elapsed.TotalMinutes;
                int seconds = elapsed.Seconds;
                return $"{minutes} min, {seconds} sec";
            }

            if (elapsed < TimeSpan.FromDays(1))
            {
                int hours = (int)elapsed.TotalHours;
                int minutes = elapsed.Minutes;
                return $"{hours} hr, {minutes} min";
            }

            int days = (int)elapsed.TotalDays;
            int hoursRemainder = elapsed.Hours;
            return $"{days} d, {hoursRemainder} hr";
        }

        private static string GetPreviewText(ClipboardItem item)
        {
            string? preview = item.Type switch
            {
                ClipboardItemType.Image => "Image content",
                ClipboardItemType.File => item.FilePath,
                ClipboardItemType.RichText => item.RichTextContent?.PlainText,
                _ => item.TextContent
            };

            if (string.IsNullOrWhiteSpace(preview))
            {
                preview = item.DisplayString;
            }

            preview = preview.Replace("\r", " ", StringComparison.Ordinal)
                             .Replace("\n", " ", StringComparison.Ordinal)
                             .Trim();

            const int maxPreviewLength = 180;
            return preview.Length > maxPreviewLength
                ? preview[..maxPreviewLength]
                : preview;
        }

        private static string GetGlyphForType(ClipboardItemType type)
        {
            return type switch
            {
                ClipboardItemType.Text => "\uE8A4",
                ClipboardItemType.Code => "\uE943",
                ClipboardItemType.Url => "\uE71B",
                ClipboardItemType.Email => "\uE715",
                ClipboardItemType.Phone => "\uE717",
                ClipboardItemType.Json => "\uE9D5",
                ClipboardItemType.Color => "\uE790",
                ClipboardItemType.Image => "\uEB9F",
                ClipboardItemType.File => "\uE7C3",
                ClipboardItemType.RichText => "\uE8D2",
                _ => "\uE8A5"
            };
        }

        private enum ClipboardSection
        {
            Pinned,
            Recent,
            History
        }

        private async Task RefreshBackdropProtectionAsync()
        {
            if (_acrylicController == null || _isBackdropSampling)
            {
                return;
            }

            int left = m_AppWindow.Position.X;
            int top = m_AppWindow.Position.Y;
            int width = m_AppWindow.Size.Width;
            int height = m_AppWindow.Size.Height;

            double measuredLuminance;
            if (width <= 0 || height <= 0)
            {
                measuredLuminance = LuminanceFallbackValue;
            }
            else
            {
                _isBackdropSampling = true;
                try
                {
                    measuredLuminance = await Task.Run(() =>
                    {
                        if (TryMeasureBackdropLuminance(left, top, width, height, out double sampledLuminance))
                        {
                            return sampledLuminance;
                        }

                        return LuminanceFallbackValue;
                    });
                }
                finally
                {
                    _isBackdropSampling = false;
                }
            }

            if (_acrylicController == null)
            {
                return;
            }

            if (!_hasBackdropSample)
            {
                _smoothedBackdropLuminance = measuredLuminance;
                _hasBackdropSample = true;
            }
            else
            {
                _smoothedBackdropLuminance = Lerp(_smoothedBackdropLuminance, measuredLuminance, LuminanceSmoothing);
            }

            ApplyBackdropReadability(_smoothedBackdropLuminance);
        }

        private void ApplyBackdropReadability(double luminance)
        {
            if (_acrylicController == null)
            {
                return;
            }

            double normalizedProtection = (luminance - LuminanceProtectionThreshold) / (1d - LuminanceProtectionThreshold);
            normalizedProtection = Math.Clamp(normalizedProtection, 0d, 1d);
            normalizedProtection = SmoothStep(normalizedProtection);

            float protection = (float)normalizedProtection;
            float contrastBoost = protection * MaxAdditionalContrastTint;

            _acrylicController.TintColor = LerpColor(BaseTintColor, StrongProtectionTintColor, protection);
            _acrylicController.TintOpacity = Math.Clamp(Lerp(MinTintOpacity, MaxTintOpacity, protection) + contrastBoost, 0f, 1f);
            _acrylicController.LuminosityOpacity = Lerp(MinLuminosityOpacity, MaxLuminosityOpacity, protection);
            _acrylicController.FallbackColor = Windows.UI.Color.FromArgb(
                (byte)Lerp(190f, 255f, protection),
                BaseTintColor.R,
                BaseTintColor.G,
                BaseTintColor.B);
        }

        private static bool TryMeasureBackdropLuminance(int left, int top, int width, int height, out double luminance)
        {
            luminance = 0d;

            if (width <= 0 || height <= 0)
            {
                return false;
            }

            int centerX = left + (width / 2);
            int centerY = top + (height / 2);

            Span<(int X, int Y)> samples = stackalloc (int X, int Y)[8];
            samples[0] = (left - SamplingPadding, top - SamplingPadding);
            samples[1] = (centerX, top - SamplingPadding);
            samples[2] = (left + width + SamplingPadding, top - SamplingPadding);
            samples[3] = (left - SamplingPadding, centerY);
            samples[4] = (left + width + SamplingPadding, centerY);
            samples[5] = (left - SamplingPadding, top + height + SamplingPadding);
            samples[6] = (centerX, top + height + SamplingPadding);
            samples[7] = (left + width + SamplingPadding, top + height + SamplingPadding);

            IntPtr desktopDc = GetDC(IntPtr.Zero);
            if (desktopDc == IntPtr.Zero)
            {
                return false;
            }

            double total = 0d;
            int validCount = 0;

            try
            {
                foreach ((int sampleX, int sampleY) in samples)
                {
                    uint colorRef = GetPixel(desktopDc, sampleX, sampleY);
                    if (colorRef == 0xFFFFFFFF)
                    {
                        continue;
                    }

                    byte r = (byte)(colorRef & 0x000000FF);
                    byte g = (byte)((colorRef & 0x0000FF00) >> 8);
                    byte b = (byte)((colorRef & 0x00FF0000) >> 16);

                    total += ((0.2126d * r) + (0.7152d * g) + (0.0722d * b)) / 255d;
                    validCount++;
                }
            }
            finally
            {
                _ = ReleaseDC(IntPtr.Zero, desktopDc);
            }

            if (validCount == 0)
            {
                return false;
            }

            luminance = total / validCount;
            return true;
        }

        private static double SmoothStep(double value)
        {
            return value * value * (3d - (2d * value));
        }

        private static float Lerp(float start, float end, float amount)
        {
            return start + ((end - start) * amount);
        }

        private static double Lerp(double start, double end, double amount)
        {
            return start + ((end - start) * amount);
        }

        private static Windows.UI.Color LerpColor(Windows.UI.Color start, Windows.UI.Color end, float amount)
        {
            return Windows.UI.Color.FromArgb(
                (byte)Lerp(start.A, end.A, amount),
                (byte)Lerp(start.R, end.R, amount),
                (byte)Lerp(start.G, end.G, amount),
                (byte)Lerp(start.B, end.B, amount));
        }

        private void Window_Activated(object sender, WindowActivatedEventArgs args)
        {
            // Always keep acrylic active, even when window loses focus
            if (_configurationSource != null)
            {
                _configurationSource.IsInputActive = true;
            }

            _ = RefreshBackdropProtectionAsync();
        }

        private void Card_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Border card)
            {
                AnimateCardScale(card, CardHoverScale);
            }
        }

        private void Card_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Border card)
            {
                AnimateCardScale(card, 1.0);
            }
        }

        private static void AnimateCardScale(Border card, double targetScale)
        {
            if (card.RenderTransform is not ScaleTransform scaleTransform)
            {
                return;
            }

            Duration duration = new(TimeSpan.FromMilliseconds(CardHoverAnimationDurationMs));

            DoubleAnimation scaleXAnimation = new()
            {
                To = targetScale,
                Duration = duration,
                EnableDependentAnimation = true,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            DoubleAnimation scaleYAnimation = new()
            {
                To = targetScale,
                Duration = duration,
                EnableDependentAnimation = true,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            Storyboard storyboard = new();
            Storyboard.SetTarget(scaleXAnimation, scaleTransform);
            Storyboard.SetTargetProperty(scaleXAnimation, nameof(ScaleTransform.ScaleX));
            Storyboard.SetTarget(scaleYAnimation, scaleTransform);
            Storyboard.SetTargetProperty(scaleYAnimation, nameof(ScaleTransform.ScaleY));
            storyboard.Children.Add(scaleXAnimation);
            storyboard.Children.Add(scaleYAnimation);
            storyboard.Begin();
        }

        private void Window_Closed(object sender, WindowEventArgs args)
        {
            _clipboardStateService.StateChanged -= ClipboardStateService_StateChanged;
            StopReadabilityMonitoring();
            StopRelativeTimeUpdates();
            _acrylicController?.Dispose();
            _acrylicController = null;
            _configurationSource = null;
        }

        private sealed class ClipboardCardViewModel : INotifyPropertyChanged
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
                Visibility textPreviewVisibility,
                Brush cardBackgroundBrush,
                Brush headerBackgroundBrush,
                Brush iconBackgroundBrush,
                Brush iconForegroundBrush)
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
                TextPreviewVisibility = textPreviewVisibility;
                CardBackgroundBrush = cardBackgroundBrush;
                HeaderBackgroundBrush = headerBackgroundBrush;
                IconBackgroundBrush = iconBackgroundBrush;
                IconForegroundBrush = iconForegroundBrush;
                _timestampLabel = GetRelativeTimestampLabel(CapturedAt);
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

            public Visibility TextPreviewVisibility { get; }

            public Brush CardBackgroundBrush { get; }

            public Brush HeaderBackgroundBrush { get; }

            public Brush IconBackgroundBrush { get; }

            public Brush IconForegroundBrush { get; }

            public void RefreshRelativeTime()
            {
                TimestampLabel = GetRelativeTimestampLabel(CapturedAt);
            }

            private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        private static class ImagePreviewCache
        {
            private static readonly Dictionary<string, BitmapImage?> Cache = new(StringComparer.Ordinal);
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
                if (Cache.TryGetValue(hash, out BitmapImage? cached))
                {
                    return cached;
                }

                BitmapImage? image = BuildPreviewImage(hash, binaryContent);
                Cache[hash] = image;
                return image;
            }

            private static BitmapImage? BuildPreviewImage(string hash, byte[] dibPayload)
            {
                try
                {
                    Directory.CreateDirectory(PreviewCacheDirectory);

                    string bmpPath = Path.Combine(PreviewCacheDirectory, hash + ".bmp");
                    if (!File.Exists(bmpPath))
                    {
                        if (TryBuildBitmapFromDib(dibPayload, out byte[]? bmpBytes) && bmpBytes is not null)
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

            private static bool TryBuildBitmapFromDib(byte[] dibPayload, out byte[]? bmpBytes)
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

                int maskBytes = (compression == 3 || compression == 6) ? 12 : 0;
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

            private static string ComputeStableHash(byte[] payload)
            {
                byte[] bytes = SHA256.HashData(payload);
                return Convert.ToHexString(bytes);
            }
        }

        private sealed record ClipboardCardStyle(
            Brush CardBackgroundBrush,
            Brush HeaderBackgroundBrush,
            Brush IconBackgroundBrush,
            Brush IconForegroundBrush);

        private sealed record SourceAppVisual(BitmapImage? Icon, Windows.UI.Color? VibrantColor, bool HasIcon);

        private static class SourceAppIconCache
        {
            private static readonly Dictionary<string, SourceAppVisual> Cache = new(StringComparer.OrdinalIgnoreCase);
            private static readonly string IconsCacheDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ClipPocketWin",
                "cache",
                "source-icons");

            public static SourceAppVisual Resolve(string? executablePath, string? processIdentifier)
            {
                string? resolvedExecutablePath = ResolveExecutablePath(executablePath);
                if (string.IsNullOrWhiteSpace(resolvedExecutablePath) || !File.Exists(resolvedExecutablePath))
                {
                    return new SourceAppVisual(null, null, false);
                }

                if (Cache.TryGetValue(resolvedExecutablePath, out SourceAppVisual? cached))
                {
                    return cached;
                }

                SourceAppVisual resolved = BuildVisual(resolvedExecutablePath);
                Cache[resolvedExecutablePath] = resolved;
                return resolved;
            }

            private static SourceAppVisual BuildVisual(string executablePath)
            {
                try
                {
                    Directory.CreateDirectory(IconsCacheDirectory);

                    string iconPngPath = Path.Combine(IconsCacheDirectory, ComputeStableHash(executablePath) + ".png");
                    if (!File.Exists(iconPngPath))
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
                double x = c * (1 - Math.Abs(((hue / 60d) % 2) - 1));
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
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);

        [DllImport("gdi32.dll")]
        private static extern uint GetPixel(IntPtr hdc, int x, int y);
    }
}
