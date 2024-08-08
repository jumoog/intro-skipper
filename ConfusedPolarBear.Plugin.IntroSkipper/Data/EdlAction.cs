namespace ConfusedPolarBear.Plugin.IntroSkipper;

/// <summary>
/// Taken from https://kodi.wiki/view/Edit_decision_list#MPlayer_EDL.
/// </summary>
public enum EdlAction
{
    /// <summary>
    /// Do not create EDL files.
    /// </summary>
    None = -1,

    /// <summary>
    /// Completely remove the intro from playback as if it was never in the original video.
    /// </summary>
    Cut,

    /// <summary>
    /// Mute audio, continue playback.
    /// </summary>
    Mute,

    /// <summary>
    /// Inserts a new scene marker.
    /// </summary>
    SceneMarker,

    /// <summary>
    /// Automatically skip the intro once during playback.
    /// </summary>
    CommercialBreak,

    /// <summary>
    /// Show a skip button.
    /// </summary>
    Intro,

    /// <summary>
    /// Show a skip button.
    /// </summary>
    Credit,

    /// <summary>
    /// Show a skip button.
    /// </summary>
    Recap
}
