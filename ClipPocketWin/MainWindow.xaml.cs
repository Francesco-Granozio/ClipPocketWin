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
        private ClipboardItemType? _selectedTypeFilter;
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

                if (_selectedTypeFilter is ClipboardItemType selectedType && item.Type != selectedType)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(_searchText) && !IsFuzzyMatch(_searchText, item.DisplayString))
                {
                    continue;
                }

                cards.Add(CreateCardViewModel(item, pinnedIds.Contains(item.Id)));
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
            if (_selectedTypeFilter is null)
            {
                return string.Equals(tag, "All", StringComparison.Ordinal);
            }

            ClipboardItemType? tagType = ResolveTypeFilterTag(tag);
            return tagType == _selectedTypeFilter;
        }

        private static ClipboardItemType? ResolveTypeFilterTag(string tag)
        {
            return tag switch
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
                // "RichText" => ClipboardItemType.RichText,
                _ => null
            };
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

            _selectedTypeFilter = ResolveTypeFilterTag(tag);

            UpdateTypeFilterButtons();
            RefreshClipboardCards();
        }

        private static ClipboardCardViewModel CreateCardViewModel(ClipboardItem item, bool isPinned)
        {
            SourceAppVisual sourceAppVisual = SourceAppIconCache.Resolve(item.SourceApplicationExecutablePath, item.SourceApplicationIdentifier);
            ClipboardCardStyle style = GetCardStyle(item, sourceAppVisual.VibrantColor);
            BitmapImage? previewImage = item.Type == ClipboardItemType.Image
                ? ImagePreviewCache.Resolve(item.BinaryContent)
                : null;

            bool isImage = previewImage is not null;
            bool isColor = item.Type == ClipboardItemType.Color;
            bool isFile = item.Type == ClipboardItemType.File;
            bool isCode = item.Type == ClipboardItemType.Code;

            Visibility imagePreviewVisibility = isImage ? Visibility.Visible : Visibility.Collapsed;
            Visibility colorPreviewVisibility = isColor ? Visibility.Visible : Visibility.Collapsed;
            Visibility filePreviewVisibility = isFile ? Visibility.Visible : Visibility.Collapsed;
            Visibility codePreviewVisibility = isCode ? Visibility.Visible : Visibility.Collapsed;
            Visibility textPreviewVisibility = (!isImage && !isColor && !isFile && !isCode) ? Visibility.Visible : Visibility.Collapsed;

            FileCardInfo fileCardInfo = CreateFileCardInfo(item.FilePath);
            BitmapImage? fileIcon = isFile ? FileTypeIconCache.Resolve(item.FilePath) : null;
            Visibility fileIconVisibility = fileIcon is null ? Visibility.Collapsed : Visibility.Visible;
            Visibility fileGlyphVisibility = fileIcon is null ? Visibility.Visible : Visibility.Collapsed;

            Brush colorPreviewForegroundBrush = isColor && TryParseHexColor(item.TextContent, out Windows.UI.Color parsedColor)
                ? new SolidColorBrush(GetContrastingColor(parsedColor))
                : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));

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

        private static ClipboardCardStyle GetCardStyle(ClipboardItem item, Windows.UI.Color? sourceVibrantColor)
        {
            if (item.Type == ClipboardItemType.Color && TryParseHexColor(item.TextContent, out Windows.UI.Color parsedColor))
            {
                return BuildSolidColorCardStyle(parsedColor);
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

        private static class CacheDirectoryPolicy
        {
            private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(12);
            private static readonly Dictionary<string, DateTimeOffset> LastCleanupUtcByDirectory = new(StringComparer.OrdinalIgnoreCase);
            private static readonly object SyncRoot = new();

            public static void EnsureDirectoryAndApplyPolicy(string directoryPath, int maxFileCount, long maxTotalBytes)
            {
                Directory.CreateDirectory(directoryPath);
                if (!TryBeginCleanup(directoryPath))
                {
                    return;
                }

                try
                {
                    CleanupDirectory(directoryPath, maxFileCount, maxTotalBytes);
                }
                catch
                {
                }
            }

            public static void TouchFile(string filePath)
            {
                try
                {
                    File.SetLastWriteTimeUtc(filePath, DateTime.UtcNow);
                }
                catch
                {
                }
            }

            private static bool TryBeginCleanup(string directoryPath)
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;
                lock (SyncRoot)
                {
                    if (LastCleanupUtcByDirectory.TryGetValue(directoryPath, out DateTimeOffset lastRun)
                        && (now - lastRun) < CleanupInterval)
                    {
                        return false;
                    }

                    LastCleanupUtcByDirectory[directoryPath] = now;
                    return true;
                }
            }

            private static void CleanupDirectory(string directoryPath, int maxFileCount, long maxTotalBytes)
            {
                DirectoryInfo directory = new(directoryPath);
                FileInfo[] files = directory.GetFiles();
                if (files.Length == 0)
                {
                    return;
                }

                Array.Sort(files, static (left, right) => right.LastWriteTimeUtc.CompareTo(left.LastWriteTimeUtc));

                int keptFiles = 0;
                long keptBytes = 0;
                foreach (FileInfo file in files)
                {
                    long fileLength = Math.Max(0L, file.Length);
                    bool keepWithinCount = keptFiles < maxFileCount;
                    bool keepWithinSize = (keptBytes + fileLength) <= maxTotalBytes || keptFiles == 0;
                    if (keepWithinCount && keepWithinSize)
                    {
                        keptFiles++;
                        keptBytes += fileLength;
                        continue;
                    }

                    try
                    {
                        file.Delete();
                    }
                    catch
                    {
                    }
                }
            }
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
                TimestampLabel = GetRelativeTimestampLabel(CapturedAt);
            }

            private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

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
                {
                    lock (SyncRoot)
                    {
                        if (Cache.TryGetValue(hash, out BitmapImage? cached))
                        {
                            Touch(hash);
                            return cached;
                        }
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

        private sealed record FileCardInfo(string Name, string Path, string Size);

        private sealed record ClipboardCardStyle(
            Brush CardBackgroundBrush,
            Brush HeaderBackgroundBrush,
            Brush BodyBackgroundBrush,
            Brush IconBackgroundBrush,
            Brush IconForegroundBrush);

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
                {
                    lock (SyncRoot)
                    {
                        if (Cache.TryGetValue(cacheKey, out BitmapImage? cached))
                        {
                            Touch(cacheKey);
                            return cached;
                        }
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
                        ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                            System.Text.Encoding.UTF8.GetBytes(cacheKey)))[..16]
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
                string shellPath = File.Exists(filePath)
                    ? filePath
                    : string.IsNullOrWhiteSpace(extension) ? "placeholder.bin" : "placeholder" + extension;

                uint attributes = File.Exists(filePath) ? 0u : FileAttributeNormal;
                uint flags = ShgfiIcon | ShgfiLargeIcon;
                if (!File.Exists(filePath))
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

        private static class CodeSyntaxHighlighter
        {
            private static readonly Regex TokenRegex = new(
                @"(""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])*')|\b(\d+(?:\.\d+)?)\b|\b(class|struct|enum|interface|public|private|protected|internal|static|readonly|const|void|int|long|string|bool|var|let|if|else|switch|case|for|foreach|while|do|return|new|try|catch|finally|async|await|import|package|function|def|true|false|null|using|namespace)\b|(//.*)$",
                RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

            public static Paragraph BuildParagraph(string? code)
            {
                string text = string.IsNullOrWhiteSpace(code) ? " " : code;
                const int max = 340;
                if (text.Length > max)
                {
                    text = text[..max];
                }

                Paragraph paragraph = new();
                int current = 0;

                foreach (Match match in TokenRegex.Matches(text))
                {
                    if (match.Index > current)
                    {
                        paragraph.Inlines.Add(new Run
                        {
                            Text = text[current..match.Index],
                            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 228, 236, 247))
                        });
                    }

                    Run run = new()
                    {
                        Text = match.Value,
                        Foreground = ResolveBrush(match)
                    };
                    paragraph.Inlines.Add(run);
                    current = match.Index + match.Length;
                }

                if (current < text.Length)
                {
                    paragraph.Inlines.Add(new Run
                    {
                        Text = text[current..],
                        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 228, 236, 247))
                    });
                }

                return paragraph;
            }

            private static Brush ResolveBrush(Match match)
            {
                if (match.Groups[1].Success)
                {
                    return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 250, 176, 107));
                }

                if (match.Groups[2].Success)
                {
                    return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 104, 194, 255));
                }

                if (match.Groups[3].Success)
                {
                    return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 193, 142, 255));
                }

                if (match.Groups[4].Success)
                {
                    return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 116, 149, 163));
                }

                return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 228, 236, 247));
            }
        }

        private sealed record SourceAppVisual(BitmapImage? Icon, Windows.UI.Color? VibrantColor, bool HasIcon);

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

            public static SourceAppVisual Resolve(string? executablePath, string? processIdentifier)
            {
                string? resolvedExecutablePath = ResolveExecutablePath(executablePath);
                if (string.IsNullOrWhiteSpace(resolvedExecutablePath) || !File.Exists(resolvedExecutablePath))
                {
                    return new SourceAppVisual(null, null, false);
                }

                {
                    lock (SyncRoot)
                    {
                        if (Cache.TryGetValue(resolvedExecutablePath, out SourceAppVisual? cached))
                        {
                            Touch(resolvedExecutablePath);
                            return cached;
                        }
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
}
