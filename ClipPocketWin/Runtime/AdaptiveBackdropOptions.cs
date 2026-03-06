namespace ClipPocketWin.Runtime;

public sealed record AdaptiveBackdropOptions
{
    public string DiagnosticName { get; init; } = "Window";
    public Windows.UI.Color BaseTintColor { get; init; } = Windows.UI.Color.FromArgb(255, 15, 20, 50);
    public Windows.UI.Color StrongProtectionTintColor { get; init; } = Windows.UI.Color.FromArgb(255, 1, 4, 12);
    public float MinTintOpacity { get; init; } = 0.12f;
    public float MaxTintOpacity { get; init; } = 0.90f;
    public float MinLuminosityOpacity { get; init; } = 0.00f;
    public float MaxLuminosityOpacity { get; init; } = 0.66f;
    public float MaxAdditionalContrastTint { get; init; } = 0.30f;
    public byte FallbackMinAlpha { get; init; } = 190;
    public byte FallbackMaxAlpha { get; init; } = 255;
    public double LuminanceProtectionThreshold { get; init; } = 0.89;
    public double LuminanceFallbackValue { get; init; } = 0.74;
    public double ProtectionCurveGamma { get; init; } = 1.95;
    public double LuminanceRiseSmoothing { get; init; } = 0.72;
    public double ReliableLuminanceDecaySmoothing { get; init; } = 0.20;
    public double UnreliableLuminanceDecaySmoothing { get; init; } = 0.05;
    public int MinimumReliableUnderWindowSamples { get; init; } = 45;
    public double PostMoveReadabilityDelaySeconds { get; init; } = 0.45;
    public double PostShowReadabilityDelaySeconds { get; init; } = 0.90;
    public double ContinuousReadabilityIntervalSeconds { get; init; } = 0.22;
    public double BackdropDiagnosticLogIntervalSeconds { get; init; } = 1.5;
    public double BackdropFallbackWarningIntervalSeconds { get; init; } = 6.0;
}
