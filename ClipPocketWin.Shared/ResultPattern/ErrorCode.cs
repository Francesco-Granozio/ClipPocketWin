namespace ClipPocketWin.Shared.ResultPattern;

public enum ErrorCode
{
    // Generic errors
    UnknownError = 0,
    ValidationError = 1,
    NotFound = 2,
    AlreadyExists = 3,
    InvalidOperation = 4,
    UnauthorizedAccess = 5,
    Conflict = 6,
    Canceled = 7,
    Timeout = 8,
    DependencyFailure = 9,

    // Domain errors (1000-1499)
    DomainInvariantViolation = 1000,
    DomainLimitExceeded = 1001,

    ClipboardItemInvalid = 1100,
    ClipboardItemUnsupportedType = 1101,
    ClipboardItemDuplicate = 1102,
    ClipboardImageTooLarge = 1103,
    ClipboardHistoryDisabled = 1104,
    ClipboardHistoryItemNotFound = 1105,

    PinnedItemDuplicate = 1200,
    PinnedItemNotFound = 1201,
    PinnedItemsLimitExceeded = 1202,

    SettingsInvalid = 1400,
    SettingsRangeInvalid = 1401,
    SettingsShortcutInvalid = 1402,

    // Application workflow errors (2000-2399)
    StateInitializationFailed = 2000,
    StatePersistenceFailed = 2001,
    ClipboardMonitorStartFailed = 2002,
    ClipboardMonitorReadFailed = 2003,
    RuntimeStartFailed = 2004,
    RuntimeStopFailed = 2005,
    HotkeyRegistrationFailed = 2006,
    TrayStartFailed = 2007,
    EdgeMonitorStartFailed = 2008,
    PanelOperationFailed = 2009,

    // Infrastructure storage/serialization errors (3000-3399)
    StoragePathUnavailable = 3000,
    StorageDirectoryCreateFailed = 3001,
    StorageReadFailed = 3002,
    StorageWriteFailed = 3003,
    StorageDeleteFailed = 3004,
    StorageAccessDenied = 3005,
    StorageQuotaExceeded = 3006,

    SerializationFailed = 3100,
    DeserializationFailed = 3101,
    DataFormatInvalid = 3102,

    // Host/startup errors (3400-3499)
    AppStartupFailed = 3400,
    DependencyResolutionFailed = 3401
}

