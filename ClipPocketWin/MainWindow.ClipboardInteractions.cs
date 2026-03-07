using ClipPocketWin.Domain.Models;
using ClipPocketWin.Shared.ResultPattern;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using WinRT.Interop;

namespace ClipPocketWin;

public sealed partial class MainWindow
{
    private async void ClipboardCard_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (sender is not Border { Tag: Guid itemId })
        {
            return;
        }

#if DEBUG
        _logger?.LogInformation("Double-click selection started for item {ItemId}.", itemId);
#endif
        await PasteAndHideAsync(itemId);
    }

    private void CodePreview_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not RichTextBlock richTextBlock)
        {
            return;
        }

        RenderCodePreview(richTextBlock, richTextBlock.DataContext as ClipboardCardViewModel);
    }

    private void CodePreview_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (sender is not RichTextBlock richTextBlock)
        {
            return;
        }

        RenderCodePreview(richTextBlock, args.NewValue as ClipboardCardViewModel);
    }

    private static void RenderCodePreview(RichTextBlock richTextBlock, ClipboardCardViewModel? card)
    {
        richTextBlock.Blocks.Clear();
        Paragraph paragraph = CodeSyntaxHighlighter.BuildParagraph(card?.CodeText);
        richTextBlock.Blocks.Add(paragraph);
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
#if DEBUG
            _logger?.LogWarning("Drag started but item {ItemId} not found in state.", itemId);
#endif
            return;
        }

#if DEBUG
        _logger?.LogInformation("Drag started for item {ItemId}, Type={Type}, FilePath={FilePath}", itemId, item.Type, item.FilePath);
#endif

        args.Data.RequestedOperation = DataPackageOperation.Copy;
        string? textPayload = item.ResolveTextPayload();

        if (item.IsFile)
        {
            if (!string.IsNullOrWhiteSpace(item.FilePath) && File.Exists(item.FilePath))
            {
#if DEBUG
                _logger?.LogInformation("Drag file resolved: {FilePath}, exists=true", item.FilePath);
#endif
                StorageFile storageFile = await StorageFile.GetFileFromPathAsync(item.FilePath);
                args.Data.SetStorageItems([storageFile]);
                return;
            }

#if DEBUG
            _logger?.LogWarning("Drag file path missing or not found: {FilePath}", item.FilePath);
#endif
        }

        if (item.IsImage)
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
        }

        if (!string.IsNullOrWhiteSpace(textPayload))
        {
            args.Data.SetText(textPayload);
        }
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

        if (item.CanEditText)
        {
            MenuFlyoutItem editItem = new() { Text = "Edit" };
            editItem.Click += (_, _) => OpenEditTextWindow(item);
            flyout.Items.Add(editItem);
        }

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
        IReadOnlyList<ClipboardItem> historyItems = _clipboardStateService.ClipboardItems;
        for (int i = 0; i < historyItems.Count; i++)
        {
            ClipboardItem item = historyItems[i];
            if (item.Id == id)
            {
                return item;
            }
        }

        IReadOnlyList<PinnedClipboardItem> pinnedItems = _clipboardStateService.PinnedItems;
        for (int i = 0; i < pinnedItems.Count; i++)
        {
            ClipboardItem item = pinnedItems[i].OriginalItem;
            if (item.Id == id)
            {
                return item;
            }
        }

        return null;
    }

    private async Task CopyOnlyAsync(Guid itemId)
    {
        Result copyResult = await _clipboardStateService.CopyClipboardItemAsync(itemId);
        if (copyResult.IsFailure)
        {
#if DEBUG
            _logger?.LogWarning(
                copyResult.Error?.Exception,
                "Clipboard copy failed for item {ItemId}. Code {ErrorCode}: {Message}",
                itemId,
                copyResult.Error?.Code,
                copyResult.Error?.Message);
#endif
        }
    }

    private async Task PasteAndHideAsync(Guid itemId)
    {
        Result hideResult = await _windowPanelService.HideAsync();
#if DEBUG
        _logger?.LogInformation("Double-click hide requested for item {ItemId}. Success={Success}", itemId, hideResult.IsSuccess);
#endif
        if (hideResult.IsFailure)
        {
#if DEBUG
            _logger?.LogWarning(
                hideResult.Error?.Exception,
                "Failed to hide panel before clipboard paste. Item {ItemId}. Code {ErrorCode}: {Message}",
                itemId,
                hideResult.Error?.Code,
                hideResult.Error?.Message);
#endif
        }

        await Task.Delay(TimeSpan.FromMilliseconds(60));

#if DEBUG
        _logger?.LogInformation("Double-click paste requested for item {ItemId}.", itemId);
#endif
        Result pasteResult = await _clipboardStateService.PasteClipboardItemAsync(itemId);
        if (pasteResult.IsFailure)
        {
#if DEBUG
            _logger?.LogWarning(
                pasteResult.Error?.Exception,
                "Clipboard paste failed for item {ItemId}. Code {ErrorCode}: {Message}",
                itemId,
                pasteResult.Error?.Code,
                pasteResult.Error?.Message);
#endif
            return;
        }

#if DEBUG
        _logger?.LogInformation("Double-click paste completed for item {ItemId}.", itemId);
#endif
    }

    private async Task SaveToFileAsync(ClipboardItem item)
    {
        nint windowHandle = WindowNative.GetWindowHandle(this);
        Result result = await _quickActionsService.SaveToFileAsync(item, windowHandle);
        if (result.IsFailure)
        {
#if DEBUG
            _logger?.LogWarning(result.Error?.Exception, "Quick action Save to file failed. Code {ErrorCode}: {Message}", result.Error?.Code, result.Error?.Message);
#endif
        }
    }

    private void OpenEditTextWindow(ClipboardItem item)
    {
        string? initialText = item.ResolveEditableTextPayload();
        if (initialText is null)
        {
#if DEBUG
            _logger?.LogWarning("Quick action Edit is not available for clipboard item type {ItemType}.", item.Type);
#endif
            return;
        }

        EditTextWindow editWindow = new(initialText);
        editWindow.TextCommitted += async (_, args) => await ApplyEditedTextAsync(item, args.EditedText);
        editWindow.Activate();
    }

    private async Task ApplyEditedTextAsync(ClipboardItem sourceItem, string editedText)
    {
        Result result = await _quickActionsService.EditTextAsync(sourceItem, editedText);
        if (result.IsFailure)
        {
#if DEBUG
            _logger?.LogWarning(result.Error?.Exception, "Quick action Edit failed. Code {ErrorCode}: {Message}", result.Error?.Code, result.Error?.Message);
#endif
        }
    }

    private async Task CopyAsBase64Async(ClipboardItem item)
    {
        Result result = await _quickActionsService.CopyAsBase64Async(item);
        if (result.IsFailure)
        {
#if DEBUG
            _logger?.LogWarning(result.Error?.Exception, "Quick action Copy as Base64 failed. Code {ErrorCode}: {Message}", result.Error?.Code, result.Error?.Message);
#endif
        }
    }

    private async Task UrlEncodeAsync(ClipboardItem item)
    {
        Result result = await _quickActionsService.UrlEncodeAsync(item);
        if (result.IsFailure)
        {
#if DEBUG
            _logger?.LogWarning(result.Error?.Exception, "Quick action URL Encode failed. Code {ErrorCode}: {Message}", result.Error?.Code, result.Error?.Message);
#endif
        }
    }

    private async Task UrlDecodeAsync(ClipboardItem item)
    {
        Result result = await _quickActionsService.UrlDecodeAsync(item);
        if (result.IsFailure)
        {
#if DEBUG
            _logger?.LogWarning(result.Error?.Exception, "Quick action URL Decode failed. Code {ErrorCode}: {Message}", result.Error?.Code, result.Error?.Message);
#endif
        }
    }

    private async Task TogglePinAsync(ClipboardItem item)
    {
        Result toggleResult = await _clipboardStateService.TogglePinAsync(item);
        if (toggleResult.IsFailure)
        {
#if DEBUG
            _logger?.LogWarning(toggleResult.Error?.Exception, "Failed to toggle pin. Code {ErrorCode}: {Message}", toggleResult.Error?.Code, toggleResult.Error?.Message);
#endif
        }
    }

    private async Task DeleteClipboardItemAsync(Guid itemId)
    {
        Result deleteResult = await _clipboardStateService.DeleteClipboardItemAsync(itemId);
        if (deleteResult.IsFailure)
        {
#if DEBUG
            _logger?.LogWarning(deleteResult.Error?.Exception, "Failed to delete clipboard item. Code {ErrorCode}: {Message}", deleteResult.Error?.Code, deleteResult.Error?.Message);
#endif
        }
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
#if DEBUG
            _logger?.LogWarning(clearResult.Error?.Exception, "Failed to clear clipboard history. Code {ErrorCode}: {Message}", clearResult.Error?.Code, clearResult.Error?.Message);
#endif
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
}
