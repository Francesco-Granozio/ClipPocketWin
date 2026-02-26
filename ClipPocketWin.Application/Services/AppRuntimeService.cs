using ClipPocketWin.Application.Abstractions;
using ClipPocketWin.Domain.Models;
using ClipPocketWin.Shared.ResultPattern;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;

namespace ClipPocketWin.Application.Services;

public sealed class AppRuntimeService : IAppRuntimeService
{
    private const int OutsideClickPollIntervalMs = 45;
    private const int VkLButton = 0x01;
    private const int VkRButton = 0x02;

    private readonly IClipboardStateService _clipboardStateService;
    private readonly IGlobalHotkeyService _globalHotkeyService;
    private readonly ITrayService _trayService;
    private readonly IEdgeMonitorService _edgeMonitorService;
    private readonly IWindowPanelService _windowPanelService;
    private readonly ILogger<AppRuntimeService> _logger;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);

    private bool _started;
    private CancellationTokenSource? _outsideClickMonitorCts;
    private Task? _outsideClickMonitorTask;

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
            await StartOutsideClickMonitorDegradedAsync(cancellationToken);

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

            Result outsideClickStopResult = await StopOutsideClickMonitorAsync(cancellationToken);
            if (outsideClickStopResult.IsFailure)
            {
                _logger.LogWarning(outsideClickStopResult.Error?.Exception, "Outside-click monitor stop failed with code {ErrorCode}: {Message}", outsideClickStopResult.Error?.Code, outsideClickStopResult.Error?.Message);
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

    private async Task StartOutsideClickMonitorDegradedAsync(CancellationToken cancellationToken)
    {
        Result startResult = await StartOutsideClickMonitorAsync(cancellationToken);
        if (startResult.IsFailure)
        {
            _logger.LogWarning(startResult.Error?.Exception, "Outside-click monitor failed to start; continuing in degraded mode. Code {ErrorCode}: {Message}", startResult.Error?.Code, startResult.Error?.Message);
        }
    }

    private Task<Result> StartOutsideClickMonitorAsync(CancellationToken cancellationToken)
    {
        if (_outsideClickMonitorTask is not null)
        {
            return Task.FromResult(Result.Success());
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            _outsideClickMonitorCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _outsideClickMonitorTask = Task.Run(() => OutsideClickMonitorLoopAsync(_outsideClickMonitorCts.Token), CancellationToken.None);
            return Task.FromResult(Result.Success());
        }
        catch (Exception exception)
        {
            _outsideClickMonitorCts?.Dispose();
            _outsideClickMonitorCts = null;
            _outsideClickMonitorTask = null;
            return Task.FromResult(Result.Failure(new Error(
                ErrorCode.InvalidOperation,
                "Failed to start outside-click monitor.",
                exception)));
        }
    }

    private async Task<Result> StopOutsideClickMonitorAsync(CancellationToken cancellationToken)
    {
        Task? monitorTask = _outsideClickMonitorTask;
        CancellationTokenSource? monitorCts = _outsideClickMonitorCts;

        _outsideClickMonitorTask = null;
        _outsideClickMonitorCts = null;

        if (monitorTask is null)
        {
            return Result.Success();
        }

        try
        {
            monitorCts?.Cancel();
            await monitorTask.WaitAsync(cancellationToken);
            return Result.Success();
        }
        catch (OperationCanceledException) when (monitorCts?.IsCancellationRequested == true)
        {
            return Result.Success();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Result.Failure(new Error(
                ErrorCode.InvalidOperation,
                "Failed to stop outside-click monitor.",
                exception));
        }
        finally
        {
            monitorCts?.Dispose();
        }
    }

    private async Task OutsideClickMonitorLoopAsync(CancellationToken cancellationToken)
    {
        bool wasLeftPressed = false;
        bool wasRightPressed = false;

        try
        {
            using PeriodicTimer timer = new(TimeSpan.FromMilliseconds(OutsideClickPollIntervalMs));
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                bool leftPressed = IsKeyPressed(VkLButton);
                bool rightPressed = IsKeyPressed(VkRButton);
                bool clickStarted = (!wasLeftPressed && leftPressed) || (!wasRightPressed && rightPressed);

                wasLeftPressed = leftPressed;
                wasRightPressed = rightPressed;

                if (!clickStarted || !_windowPanelService.IsVisible || _windowPanelService.IsPointerOverPanel())
                {
                    continue;
                }

                Result hideResult = await _windowPanelService.HideAsync(cancellationToken);
                if (hideResult.IsFailure)
                {
                    _logger.LogWarning(hideResult.Error?.Exception, "Panel hide failed (outside click). Code {ErrorCode}: {Message}", hideResult.Error?.Code, hideResult.Error?.Message);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Outside-click monitor loop terminated unexpectedly.");
        }
    }

    private static bool IsKeyPressed(int virtualKey)
    {
        short state = GetAsyncKeyState(virtualKey);
        return (state & 0x8000) != 0;
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

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
