using System;
using System.Collections.ObjectModel;
using System.IO;
using Microsoft.Extensions.Logging;

namespace ConfusedPolarBear.Plugin.IntroSkipper;

/// <summary>
/// Update EDL files associated with a list of episodes.
/// </summary>
public static class EdlManager
{
    private static ILogger? _logger;

    /// <summary>
    /// Initialize EDLManager with a logger.
    /// </summary>
    /// <param name="logger">ILogger.</param>
    public static void Initialize(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Logs the configuration that will be used during EDL file creation.
    /// </summary>
    public static void LogConfiguration()
    {
        if (_logger is null)
        {
            throw new InvalidOperationException("Logger must not be null");
        }

        var config = Plugin.Instance!.Configuration;

        if (config.EdlAction == EdlAction.None)
        {
            _logger.LogDebug("EDL action: None - taking no further action");
            return;
        }

        _logger.LogDebug("EDL action: {Action}", config.EdlAction);
        _logger.LogDebug("Regenerate EDL files: {Regenerate}", config.RegenerateEdlFiles);
    }

    /// <summary>
    /// If the EDL action is set to a value other than None, update EDL files for the provided episodes.
    /// </summary>
    /// <param name="episodes">Episodes to update EDL files for.</param>
    public static void UpdateEDLFiles(ReadOnlyCollection<QueuedEpisode> episodes)
    {
        var regenerate = Plugin.Instance!.Configuration.RegenerateEdlFiles;
        var action = Plugin.Instance.Configuration.EdlAction;
        if (action == EdlAction.None)
        {
            _logger?.LogDebug("EDL action is set to none, not updating EDL files");
            return;
        }

        _logger?.LogDebug("Updating EDL files with action {Action}", action);

        foreach (var episode in episodes)
        {
            var id = episode.EpisodeId;

            bool hasIntro = Plugin.Instance!.Intros.TryGetValue(id, out var intro) && intro.Valid;
            bool hasCredit = Plugin.Instance!.Credits.TryGetValue(id, out var credit) && credit.Valid;
            bool hasRecap = Plugin.Instance!.Recaps.TryGetValue(id, out var recap) && recap.Valid;

            if (!hasIntro && !hasCredit && !hasRecap)
            {
                _logger?.LogDebug("Episode {Id} has neither a valid intro nor credit nor recap, skipping", id);
                continue;
            }

            var edlPath = GetEdlPath(Plugin.Instance.GetItemPath(id));

            _logger?.LogTrace("Episode {Id} has EDL path {Path}", id, edlPath);

            if (!regenerate && File.Exists(edlPath))
            {
                _logger?.LogTrace("Refusing to overwrite existing EDL file {Path}", edlPath);
                continue;
            }

            var edlContent = string.Empty;

            if (hasIntro)
            {
                edlContent += intro?.ToEdl(action);
            }

            if (hasCredit)
            {
                if (edlContent.Length > 0)
                {
                    edlContent += Environment.NewLine;
                }

                if (action == EdlAction.Intro)
                {
                    edlContent += credit?.ToEdl(EdlAction.Credit);
                }
                else
                {
                    edlContent += credit?.ToEdl(action);
                }
            }

            // TODO
            if (hasRecap)
            {
                if (edlContent.Length > 0)
                {
                    edlContent += Environment.NewLine;
                }

                if (action == EdlAction.Intro)
                {
                    edlContent += credit?.ToEdl(EdlAction.Recap);
                }
                else
                {
                    edlContent += credit?.ToEdl(action);
                }
            }

            File.WriteAllText(edlPath, edlContent);
        }
    }

    /// <summary>
    /// Given the path to an episode, return the path to the associated EDL file.
    /// </summary>
    /// <param name="mediaPath">Full path to episode.</param>
    /// <returns>Full path to EDL file.</returns>
    public static string GetEdlPath(string mediaPath)
    {
        return Path.ChangeExtension(mediaPath, "edl");
    }
}
