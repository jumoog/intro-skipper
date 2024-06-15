using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using ConfusedPolarBear.Plugin.IntroSkipper.Configuration;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace ConfusedPolarBear.Plugin.IntroSkipper;

/// <summary>
/// Chapter name analyzer.
/// </summary>
public class ChapterAnalyzer : IMediaFileAnalyzer
{
    private ILogger<ChapterAnalyzer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChapterAnalyzer"/> class.
    /// </summary>
    /// <param name="logger">Logger.</param>
    public ChapterAnalyzer(ILogger<ChapterAnalyzer> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public ReadOnlyCollection<QueuedEpisode> AnalyzeMediaFiles(
        ReadOnlyCollection<QueuedEpisode> analysisQueue,
        AnalysisMode mode,
        CancellationToken cancellationToken)
    {
        var skippableRanges = new Dictionary<Guid, XmlIntro>();

        var expression = mode == AnalysisMode.Introduction ?
            Plugin.Instance!.Configuration.ChapterAnalyzerIntroductionPattern :
            Plugin.Instance!.Configuration.ChapterAnalyzerEndCreditsPattern;

        if (string.IsNullOrWhiteSpace(expression))
        {
            return analysisQueue;
        }

        foreach (var episode in analysisQueue)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var skipRange = FindMatchingChapter(
                episode,
                new(Plugin.Instance.GetChapters(episode.EpisodeId)),
                expression,
                mode);

            if (skipRange is null)
            {
                continue;
            }

            skippableRanges.Add(episode.EpisodeId, skipRange);
        }

        Plugin.Instance.UpdateTimestamps(skippableRanges, mode);

        return analysisQueue
            .Where(x => !skippableRanges.ContainsKey(x.EpisodeId))
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Searches a list of chapter names for one that matches the provided regular expression.
    /// Only public to allow for unit testing.
    /// </summary>
    /// <param name="episode">Episode.</param>
    /// <param name="chapters">Media item chapters.</param>
    /// <param name="expression">Regular expression pattern.</param>
    /// <param name="mode">Analysis mode.</param>
    /// <returns>Intro object containing skippable time range, or null if no chapter matched.</returns>
    public XmlIntro? FindMatchingChapter(
        QueuedEpisode episode,
        Collection<ChapterInfo> chapters,
        string expression,
        AnalysisMode mode)
    {
        XmlIntro? matchingChapter = null;

        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();

        var minDuration = config.MinimumIntroDuration;
        int maxDuration = mode == AnalysisMode.Introduction ?
            config.MaximumIntroDuration :
            config.MaximumCreditsDuration;

        if (chapters.Count == 0)
        {
            return null;
        }

        if (mode == AnalysisMode.Credits)
        {
            // Since the ending credits chapter may be the last chapter in the file, append a virtual
            // chapter at the very end of the file.
            chapters.Add(new()
            {
                StartPositionTicks = TimeSpan.FromSeconds(episode.Duration).Ticks
            });

            // Check all chapters in reverse order, skipping the virtual chapter
            for (int i = chapters.Count - 2; i > 0; i--)
            {
                var current = chapters[i];
                var previous = chapters[i - 1];

                if (string.IsNullOrWhiteSpace(current.Name))
                {
                    continue;
                }

                var currentRange = new TimeRange(
                    TimeSpan.FromTicks(current.StartPositionTicks).TotalSeconds,
                    TimeSpan.FromTicks(chapters[i + 1].StartPositionTicks).TotalSeconds);

                var baseMessage = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}: Chapter \"{1}\" ({2} - {3})",
                    episode.Path,
                    current.Name,
                    currentRange.Start,
                    currentRange.End);

                if (currentRange.Duration < minDuration || currentRange.Duration > maxDuration)
                {
                    _logger.LogTrace("{Base}: ignoring (invalid duration)", baseMessage);
                    continue;
                }

                // Regex.IsMatch() is used here in order to allow the runtime to cache the compiled regex
                // between function invocations.
                var match = Regex.IsMatch(
                    current.Name,
                    expression,
                    RegexOptions.None,
                    TimeSpan.FromSeconds(1));

                if (!match)
                {
                    _logger.LogTrace("{Base}: ignoring (does not match regular expression)", baseMessage);
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(previous.Name))
                {
                    // Check for possibility of overlapping keywords
                    var overlap = Regex.IsMatch(
                        previous.Name,
                        expression,
                        RegexOptions.None,
                        TimeSpan.FromSeconds(1));

                    if (overlap)
                    {
                        continue;
                    }
                }

                matchingChapter = new(episode.EpisodeId, currentRange);
                _logger.LogTrace("{Base}: okay", baseMessage);
                break;
            }
        }
        else
        {
            // Check all chapters
            for (int i = 0; i < chapters.Count - 1; i++)
            {
                var current = chapters[i];
                var next = chapters[i + 1];

                if (string.IsNullOrWhiteSpace(current.Name))
                {
                    continue;
                }

                var currentRange = new TimeRange(
                    TimeSpan.FromTicks(current.StartPositionTicks).TotalSeconds,
                    TimeSpan.FromTicks(next.StartPositionTicks).TotalSeconds);

                var baseMessage = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}: Chapter \"{1}\" ({2} - {3})",
                    episode.Path,
                    current.Name,
                    currentRange.Start,
                    currentRange.End);

                if (currentRange.Duration < minDuration || currentRange.Duration > maxDuration)
                {
                    _logger.LogTrace("{Base}: ignoring (invalid duration)", baseMessage);
                    continue;
                }

                // Regex.IsMatch() is used here in order to allow the runtime to cache the compiled regex
                // between function invocations.
                var match = Regex.IsMatch(
                    current.Name,
                    expression,
                    RegexOptions.None,
                    TimeSpan.FromSeconds(1));

                if (!match)
                {
                    _logger.LogTrace("{Base}: ignoring (does not match regular expression)", baseMessage);
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(next.Name))
                {
                    // Check for possibility of overlapping keywords
                    var overlap = Regex.IsMatch(
                        next.Name,
                        expression,
                        RegexOptions.None,
                        TimeSpan.FromSeconds(1));

                    if (overlap)
                    {
                        continue;
                    }
                }

                matchingChapter = new(episode.EpisodeId, currentRange);
                _logger.LogTrace("{Base}: okay", baseMessage);
                break;
            }
        }

        return matchingChapter;
    }
}
