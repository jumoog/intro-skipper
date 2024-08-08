namespace ConfusedPolarBear.Plugin.IntroSkipper;

/// <summary>
/// Type of media file analysis to perform.
/// </summary>
public enum AnalysisMode
{
    /// <summary>
    /// Detect introduction sequences.
    /// </summary>
    Introduction,

    /// <summary>
    /// Detect credits.
    /// </summary>
    Credits,

    /// <summary>
    /// Detect recaps.
    /// </summary>
    Recaps,
}
