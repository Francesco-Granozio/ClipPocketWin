using ClipPocketWin.Application.Abstractions;
using ClipPocketWin.Domain.Models;
using ClipPocketWin.Runtime;
using ClipPocketWin.Shared.Imaging;
using ClipPocketWin.Shared.ResultPattern;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
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
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using WinRT.Interop;

namespace ClipPocketWin
{
    public sealed partial class MainWindow : Window
    {
        private const float MinTintOpacity = 0.12f;
        private const float MaxTintOpacity = 0.90f;
        private const float MinLuminosityOpacity = 0.00f;
        private const float MaxLuminosityOpacity = 0.66f;
        private const float MaxAdditionalContrastTint = 0.30f;
        private const double LuminanceProtectionThreshold = 0.84;
        private const double LuminanceFallbackValue = 0.74;
        private const double LuminanceRiseSmoothing = 0.72;
        private const double ReliableLuminanceDecaySmoothing = 0.20;
        private const double UnreliableLuminanceDecaySmoothing = 0.05;
        private const int MinimumReliableUnderWindowSamples = 45;
        private const double ProtectionCurveGamma = 0.72;
        private const double PostMoveReadabilityDelaySeconds = 0.45;
        private const double PostShowReadabilityDelaySeconds = 0.90;
        private const double ContinuousReadabilityIntervalSeconds = 0.22;
        private const double BackdropDiagnosticLogIntervalSeconds = 1.5;
        private const double BackdropFallbackWarningIntervalSeconds = 6.0;
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
        private readonly AdaptiveBackdropController? _adaptiveBackdropController;
        private SettingsWindow? _settingsWindow;
        private string _searchText = string.Empty;
        private ClipboardSection _selectedSection = ClipboardSection.History;
        private string _selectedTypeFilterTag = ClipboardTypeFilter.AllTag;
        private DispatcherQueueTimer? _relativeTimeTimer;

        public MainWindow()
        {
            InitializeComponent();

            App app = (App)Microsoft.UI.Xaml.Application.Current;
            _clipboardStateService = app.Services.GetRequiredService<IClipboardStateService>();
            _quickActionsService = app.Services.GetRequiredService<IQuickActionsService>();
            _windowPanelService = app.Services.GetRequiredService<IWindowPanelService>();
#if DEBUG
            _logger = app.Services.GetService<ILogger<MainWindow>>();
#else
            _logger = null;
#endif
            _clipboardStateService.StateChanged += ClipboardStateService_StateChanged;
            ClipboardItemsListView.ItemsSource = _clipboardCards;
            RefreshClipboardCards();
            UpdateSectionButtons();
            UpdateTypeFilterButtons();
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
                presenter.IsResizable = false;
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
                presenter.IsAlwaysOnTop = false;
            }

            // Size the window
            DisplayArea displayArea = DisplayArea.GetFromWindowId(wndId, DisplayAreaFallback.Primary);
            int windowWidth = 1100;
            int windowHeight = 420;
            int x = (displayArea.WorkArea.Width - windowWidth) / 2;
            int y = (displayArea.WorkArea.Height - windowHeight) / 2;
            m_AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, windowWidth, windowHeight));

            AdaptiveBackdropOptions backdropOptions = new()
            {
                DiagnosticName = nameof(MainWindow),
                BaseTintColor = BaseTintColor,
                StrongProtectionTintColor = StrongProtectionTintColor,
                MinTintOpacity = MinTintOpacity,
                MaxTintOpacity = MaxTintOpacity,
                MinLuminosityOpacity = MinLuminosityOpacity,
                MaxLuminosityOpacity = MaxLuminosityOpacity,
                MaxAdditionalContrastTint = MaxAdditionalContrastTint,
                LuminanceProtectionThreshold = LuminanceProtectionThreshold,
                LuminanceFallbackValue = LuminanceFallbackValue,
                ProtectionCurveGamma = ProtectionCurveGamma,
                LuminanceRiseSmoothing = LuminanceRiseSmoothing,
                ReliableLuminanceDecaySmoothing = ReliableLuminanceDecaySmoothing,
                UnreliableLuminanceDecaySmoothing = UnreliableLuminanceDecaySmoothing,
                MinimumReliableUnderWindowSamples = MinimumReliableUnderWindowSamples,
                PostMoveReadabilityDelaySeconds = PostMoveReadabilityDelaySeconds,
                PostShowReadabilityDelaySeconds = PostShowReadabilityDelaySeconds,
                ContinuousReadabilityIntervalSeconds = ContinuousReadabilityIntervalSeconds,
                BackdropDiagnosticLogIntervalSeconds = BackdropDiagnosticLogIntervalSeconds,
                BackdropFallbackWarningIntervalSeconds = BackdropFallbackWarningIntervalSeconds,
                FallbackMinAlpha = 190,
                FallbackMaxAlpha = 255
            };

            _adaptiveBackdropController = new AdaptiveBackdropController(this, m_AppWindow, backdropOptions, _logger);
            if (_adaptiveBackdropController.Initialize())
            {
                m_AppWindow.Changed += AppWindow_Changed;
            }

            Activated += Window_Activated;
            Closed += Window_Closed;
        }

        private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
        {
            _adaptiveBackdropController?.HandleAppWindowChanged(args);
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
            const int maxCards = 80;
            IReadOnlyList<ClipboardItem> historyItems = _clipboardStateService.ClipboardItems;
            IReadOnlyList<PinnedClipboardItem> pinnedItems = _clipboardStateService.PinnedItems;

            int unfilteredSectionCount = GetSectionItemCount(_selectedSection, historyItems, pinnedItems);
            List<ClipboardCardViewModel> nextCards = BuildFilteredCards(historyItems, pinnedItems, maxCards);

            _clipboardCards.Clear();
            foreach (ClipboardCardViewModel card in nextCards)
            {
                _clipboardCards.Add(card);
            }

            bool hasCards = _clipboardCards.Count > 0;
            ClipboardItemsListView.Visibility = hasCards ? Visibility.Visible : Visibility.Collapsed;
            EmptyStateContainer.Visibility = hasCards ? Visibility.Collapsed : Visibility.Visible;
            if (!hasCards)
            {
                UpdateEmptyState(unfilteredSectionCount > 0);
            }
        }

        private static int GetSectionItemCount(
            ClipboardSection section,
            IReadOnlyList<ClipboardItem> historyItems,
            IReadOnlyList<PinnedClipboardItem> pinnedItems)
        {
            return section switch
            {
                ClipboardSection.Pinned => pinnedItems.Count,
                ClipboardSection.History => historyItems.Count,
                _ => Math.Min(20, historyItems.Count)
            };
        }

        private void UpdateEmptyState(bool hadSectionItemsBeforeFiltering)
        {
            if (hadSectionItemsBeforeFiltering)
            {
                EmptyStateIcon.Glyph = "\uE8B2";
                EmptyStateTitle.Text = "No Results";
                EmptyStateSubtitle.Text = "Try changing search text or type filters.";
                return;
            }

            switch (_selectedSection)
            {
                case ClipboardSection.Pinned:
                    EmptyStateIcon.Glyph = "\uE718";
                    EmptyStateTitle.Text = "No Pinned Items";
                    EmptyStateSubtitle.Text = "Pinned clipboard items will appear here.";
                    break;
                case ClipboardSection.History:
                    EmptyStateIcon.Glyph = "\uF1DA";
                    EmptyStateTitle.Text = "No History";
                    EmptyStateSubtitle.Text = "Your clipboard history is empty.";
                    break;
                default:
                    EmptyStateIcon.Glyph = "\uE823";
                    EmptyStateTitle.Text = "No Recent Items";
                    EmptyStateSubtitle.Text = "Your recent clipboard items will appear here.";
                    break;
            }
        }

        private List<ClipboardCardViewModel> BuildFilteredCards(
            IReadOnlyList<ClipboardItem> historyItems,
            IReadOnlyList<PinnedClipboardItem> pinnedItems,
            int maxCards)
        {
            HashSet<Guid> pinnedIds = [];
            for (int i = 0; i < pinnedItems.Count; i++)
            {
                _ = pinnedIds.Add(pinnedItems[i].OriginalItem.Id);
            }

            int sourceCount = _selectedSection switch
            {
                ClipboardSection.Pinned => pinnedItems.Count,
                ClipboardSection.History => historyItems.Count,
                _ => Math.Min(20, historyItems.Count)
            };

            List<ClipboardCardViewModel> cards = new(sourceCount);
            for (int i = 0; i < sourceCount; i++)
            {
                ClipboardItem item = _selectedSection == ClipboardSection.Pinned
                    ? pinnedItems[i].OriginalItem
                    : historyItems[i];

                if (!ClipboardTypeFilter.Matches(_selectedTypeFilterTag, item))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(_searchText) && !IsFuzzyMatch(_searchText, item.DisplayString))
                {
                    continue;
                }

                cards.Add(ClipboardCardFactory.Create(item, pinnedIds.Contains(item.Id)));
                if (cards.Count >= maxCards)
                {
                    break;
                }
            }

            return cards;
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

            PinnedTabLabel.Text = $"Pinned ({pinnedCount})";
            RecentTabLabel.Text = $"Recent ({recentCount})";
            HistoryTabLabel.Text = $"History ({historyCount})";

            ApplyChipSelectionVisual(PinnedTabButton, _selectedSection == ClipboardSection.Pinned);
            ApplyChipSelectionVisual(RecentTabButton, _selectedSection == ClipboardSection.Recent);
            ApplyChipSelectionVisual(HistoryTabButton, _selectedSection == ClipboardSection.History);
        }

        private void UpdateTypeFilterButtons()
        {
            foreach (object child in TypeFilterButtonsPanel.Children)
            {
                if (child is not Button button || button.Tag is not string tag)
                {
                    continue;
                }

                bool isSelected = IsSelectedTypeFilterTag(tag);
                ApplyChipSelectionVisual(button, isSelected);
            }
        }

        private bool IsSelectedTypeFilterTag(string tag)
        {
            return ClipboardTypeFilter.IsSelected(_selectedTypeFilterTag, tag);
        }

        private static void ApplyChipSelectionVisual(Button button, bool isSelected)
        {
            Windows.UI.Color background = isSelected
                ? Windows.UI.Color.FromArgb(102, 80, 164, 255)
                : Windows.UI.Color.FromArgb(34, 255, 255, 255);
            Windows.UI.Color border = isSelected
                ? Windows.UI.Color.FromArgb(85, 255, 255, 255)
                : Windows.UI.Color.FromArgb(34, 255, 255, 255);
            Windows.UI.Color foreground = isSelected
                ? Windows.UI.Color.FromArgb(255, 255, 255, 255)
                : Windows.UI.Color.FromArgb(170, 255, 255, 255);

            ApplyChipStateResources(button, background, border, foreground);
            button.Background = new SolidColorBrush(background);
            button.BorderBrush = new SolidColorBrush(border);
            button.Foreground = new SolidColorBrush(foreground);
            button.FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Normal;

            if (button.Content is DependencyObject contentRoot)
            {
                Brush contentForeground = new SolidColorBrush(foreground);
                ApplyForegroundToChipContent(contentRoot, contentForeground);
            }

            RefreshButtonVisualState(button);
        }

        private static void ApplyChipStateResources(Button button, Windows.UI.Color background, Windows.UI.Color border, Windows.UI.Color foreground)
        {
            SetBrushResource(button.Resources, "ButtonBackground", background);
            SetBrushResource(button.Resources, "ButtonBackgroundPointerOver", background);
            SetBrushResource(button.Resources, "ButtonBackgroundPressed", background);

            SetBrushResource(button.Resources, "ButtonBorderBrush", border);
            SetBrushResource(button.Resources, "ButtonBorderBrushPointerOver", border);
            SetBrushResource(button.Resources, "ButtonBorderBrushPressed", border);

            SetBrushResource(button.Resources, "ButtonForeground", foreground);
            SetBrushResource(button.Resources, "ButtonForegroundPointerOver", foreground);
            SetBrushResource(button.Resources, "ButtonForegroundPressed", foreground);
        }

        private static void SetBrushResource(ResourceDictionary resources, string key, Windows.UI.Color color)
        {
            resources[key] = new SolidColorBrush(color);
        }

        private static void RefreshButtonVisualState(Button button)
        {
            VisualStateManager.GoToState(button, "Normal", false);
            if (button.IsPointerOver)
            {
                VisualStateManager.GoToState(button, "PointerOver", false);
            }
        }

        private static void ApplyForegroundToChipContent(DependencyObject root, Brush foreground)
        {
            if (root is TextBlock textBlock)
            {
                textBlock.Foreground = foreground;
            }
            else if (root is FontIcon fontIcon)
            {
                fontIcon.Foreground = foreground;
            }

            int childrenCount = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < childrenCount; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                ApplyForegroundToChipContent(child, foreground);
            }
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


        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_settingsWindow is null)
            {
                App app = (App)Microsoft.UI.Xaml.Application.Current;
                ILogger<SettingsWindow>? settingsLogger = null;
#if DEBUG
                settingsLogger = app.Services.GetService<ILogger<SettingsWindow>>();
#endif
                _settingsWindow = new SettingsWindow(
                    _clipboardStateService,
                    app.Services.GetRequiredService<IGlobalHotkeyService>(),
                    settingsLogger);
                _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            }

            _settingsWindow.Activate();
        }

        private void TypeFilterButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string tag })
            {
                return;
            }

            _selectedTypeFilterTag = ClipboardTypeFilter.Normalize(tag);

            UpdateTypeFilterButtons();
            RefreshClipboardCards();
        }

        private static string? BuildDragImagePath(byte[] dibPayload)
        {
            try
            {
                string dragCacheDirectory = Path.Combine(
                    Path.GetTempPath(),
                    "ClipPocketWin",
                    "drag-images");

                CacheDirectoryPolicy.EnsureDirectoryAndApplyPolicy(
                    dragCacheDirectory,
                    maxFileCount: 160,
                    maxTotalBytes: 256L * 1024 * 1024);

                string hash = Convert.ToHexString(SHA256.HashData(dibPayload));
                string bmpPath = Path.Combine(dragCacheDirectory, hash + ".bmp");
                if (File.Exists(bmpPath))
                {
                    CacheDirectoryPolicy.TouchFile(bmpPath);
                    return bmpPath;
                }

                byte[] payloadToWrite = dibPayload;
                if (DibBitmapConverter.TryBuildBitmapFromDib(dibPayload, out byte[]? bmpBytes) && bmpBytes is not null)
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

        private enum ClipboardSection
        {
            Pinned,
            Recent,
            History
        }

        private void Window_Activated(object sender, WindowActivatedEventArgs args)
        {
            _adaptiveBackdropController?.HandleWindowActivated(args);
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
            m_AppWindow.Changed -= AppWindow_Changed;

            StopRelativeTimeUpdates();
            _adaptiveBackdropController?.Dispose();
        }

    }
}
