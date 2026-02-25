using ClipPocketWin.Application.Abstractions;
using ClipPocketWin.Domain.Models;
using ClipPocketWin.Shared.ResultPattern;
using Microsoft.Extensions.Logging;

namespace ClipPocketWin.Application.Services;

public sealed class AppRuntimeService : IAppRuntimeService
{
    private readonly IClipboardStateService _clipboardStateService;
    private readonly IGlobalHotkeyService _globalHotkeyService;
    private readonly ITrayService _trayService;
    private readonly IEdgeMonitorService _edgeMonitorService;
    private readonly IWindowPanelService _windowPanelService;
    private readonly ILogger<AppRuntimeService> _logger;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);

    private bool _started;

    public AppRuntimeService(
        IClipboardStateService clipboardStateService,
        IGlobalHotkeyService globalHotkeyService,
        ITrayService trayService,
        IEdgeMonitorService edgeMonitorService,
        IWindowPanelService windowPanelService,
        ILogger<AppRuntimeService> logger)
    {
        _clipboardStateService = clipboardStateService;
        _globalHotkeyService = globalHotkeyService;
        _trayService = trayService;
        _edgeMonitorService = edgeMonitorService;
        _windowPanelService = windowPanelService;
        _logger = logger;
    }

    public event EventHandler? ExitRequested;

    public async Task<Result> StartAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            if (_started)
            {
                return Result.Success();
            }

            Result stateRuntimeResult = await _clipboardStateService.StartRuntimeAsync(cancellationToken);
            if (stateRuntimeResult.IsFailure)
            {
                return Result.Failure(new Error(
                    ErrorCode.RuntimeStartFailed,
                    "Failed to start clipboard runtime from app runtime service.",
                    stateRuntimeResult.Error?.Exception));
            }

            SubscribeRuntimeEvents();

            await StartTrayDegradedAsync(cancellationToken);
            await StartHotkeyDegradedAsync(_clipboardStateService.Settings.KeyboardShortcut, cancellationToken);
            await StartEdgeMonitorDegradedAsync(_clipboardStateService.Settings, cancellationToken);

            _started = true;
            _logger.LogInformation("Application runtime services started.");
            return Result.Success();
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task<Result> StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            if (!_started)
            {
                return Result.Success();
            }

            UnsubscribeRuntimeEvents();

            Result trayResult = await _trayService.StopAsync(cancellationToken);
            if (trayResult.IsFailure)
            {
                _logger.LogWarning(trayResult.Error?.Exception, "Tray stop failed with code {ErrorCode}: {Message}", trayResult.Error?.Code, trayResult.Error?.Message);
            }

            Result hotkeyResult = await _globalHotkeyService.StopAsync(cancellationToken);
            if (hotkeyResult.IsFailure)
            {
                _logger.LogWarning(hotkeyResult.Error?.Exception, "Hotkey stop failed with code {ErrorCode}: {Message}", hotkeyResult.Error?.Code, hotkeyResult.Error?.Message);
            }

            Result edgeResult = await _edgeMonitorService.StopAsync(cancellationToken);
            if (edgeResult.IsFailure)
            {
                _logger.LogWarning(edgeResult.Error?.Exception, "Edge monitor stop failed with code {ErrorCode}: {Message}", edgeResult.Error?.Code, edgeResult.Error?.Message);
            }

            Result stateRuntimeResult = await _clipboardStateService.StopRuntimeAsync(cancellationToken);
            if (stateRuntimeResult.IsFailure)
            {
                return Result.Failure(new Error(
                    ErrorCode.RuntimeStopFailed,
                    "Failed to stop clipboard runtime from app runtime service.",
                    stateRuntimeResult.Error?.Exception));
            }

            _started = false;
            _logger.LogInformation("Application runtime services stopped.");
            return Result.Success();
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    private async Task StartTrayDegradedAsync(CancellationToken cancellationToken)
    {
        Result trayResult = await _trayService.StartAsync(cancellationToken);
        if (trayResult.IsFailure)
        {
            _logger.LogWarning(trayResult.Error?.Exception, "Tray failed to start; continuing in degraded mode. Code {ErrorCode}: {Message}", trayResult.Error?.Code, trayResult.Error?.Message);
        }
    }

    private async Task StartHotkeyDegradedAsync(KeyboardShortcut shortcut, CancellationToken cancellationToken)
    {
        Result hotkeyResult = await _globalHotkeyService.StartAsync(shortcut, cancellationToken);
        if (hotkeyResult.IsFailure)
        {
            _logger.LogWarning(hotkeyResult.Error?.Exception, "Hotkey failed to start; continuing in degraded mode. Code {ErrorCode}: {Message}", hotkeyResult.Error?.Code, hotkeyResult.Error?.Message);
        }
    }

    private async Task StartEdgeMonitorDegradedAsync(ClipPocketSettings settings, CancellationToken cancellationToken)
    {
        if (!settings.AutoShowOnEdge)
        {
            return;
        }

        Result edgeResult = await _edgeMonitorService.StartAsync(settings.AutoShowDelay, settings.AutoHideDelay, cancellationToken);
        if (edgeResult.IsFailure)
        {
            _logger.LogWarning(edgeResult.Error?.Exception, "Edge monitor failed to start; continuing in degraded mode. Code {ErrorCode}: {Message}", edgeResult.Error?.Code, edgeResult.Error?.Message);
        }
    }

    private void SubscribeRuntimeEvents()
    {
        _globalHotkeyService.HotkeyPressed += GlobalHotkeyService_HotkeyPressed;
        _trayService.ToggleRequested += TrayService_ToggleRequested;
        _trayService.ShowRequested += TrayService_ShowRequested;
        _trayService.HideRequested += TrayService_HideRequested;
        _trayService.ExitRequested += TrayService_ExitRequested;
        _edgeMonitorService.EdgeEntered += EdgeMonitorService_EdgeEntered;
        _edgeMonitorService.EdgeExited += EdgeMonitorService_EdgeExited;
    }

    private void UnsubscribeRuntimeEvents()
    {
        _globalHotkeyService.HotkeyPressed -= GlobalHotkeyService_HotkeyPressed;
        _trayService.ToggleRequested -= TrayService_ToggleRequested;
        _trayService.ShowRequested -= TrayService_ShowRequested;
        _trayService.HideRequested -= TrayService_HideRequested;
        _trayService.ExitRequested -= TrayService_ExitRequested;
        _edgeMonitorService.EdgeEntered -= EdgeMonitorService_EdgeEntered;
        _edgeMonitorService.EdgeExited -= EdgeMonitorService_EdgeExited;
    }

    private async void GlobalHotkeyService_HotkeyPressed(object? sender, EventArgs e)
    {
        await SafeToggleAsync("hotkey");
    }

    private async void TrayService_ToggleRequested(object? sender, EventArgs e)
    {
        await SafeToggleAsync("tray toggle");
    }

    private async void TrayService_ShowRequested(object? sender, EventArgs e)
    {
        await SafeShowAsync("tray show");
    }

    private async void TrayService_HideRequested(object? sender, EventArgs e)
    {
        await SafeHideAsync("tray hide");
    }

    private void TrayService_ExitRequested(object? sender, EventArgs e)
    {
        ExitRequested?.Invoke(this, EventArgs.Empty);
    }

    private async void EdgeMonitorService_EdgeEntered(object? sender, EventArgs e)
    {
        await SafeShowAsync("edge entered");
    }

    private async void EdgeMonitorService_EdgeExited(object? sender, EventArgs e)
    {
        if (_windowPanelService.IsPointerOverPanel())
        {
            return;
        }

        await SafeHideAsync("edge exited");
    }

    private async Task SafeToggleAsync(string source)
    {
        Result toggleResult = await _windowPanelService.ToggleAsync();
        if (toggleResult.IsFailure)
        {
            _logger.LogWarning(toggleResult.Error?.Exception, "Panel toggle failed ({Source}). Code {ErrorCode}: {Message}", source, toggleResult.Error?.Code, toggleResult.Error?.Message);
        }
    }

    private async Task SafeShowAsync(string source)
    {
        Result showResult = await _windowPanelService.ShowAsync();
        if (showResult.IsFailure)
        {
            _logger.LogWarning(showResult.Error?.Exception, "Panel show failed ({Source}). Code {ErrorCode}: {Message}", source, showResult.Error?.Code, showResult.Error?.Message);
        }
    }

    private async Task SafeHideAsync(string source)
    {
        Result hideResult = await _windowPanelService.HideAsync();
        if (hideResult.IsFailure)
        {
            _logger.LogWarning(hideResult.Error?.Exception, "Panel hide failed ({Source}). Code {ErrorCode}: {Message}", source, hideResult.Error?.Code, hideResult.Error?.Message);
        }
    }
}
