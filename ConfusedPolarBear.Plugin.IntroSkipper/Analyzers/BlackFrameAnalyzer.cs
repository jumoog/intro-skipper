using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using ConfusedPolarBear.Plugin.IntroSkipper.Configuration;
using Microsoft.Extensions.Logging;

namespace ConfusedPolarBear.Plugin.IntroSkipper;

/// <summary>
/// Media file analyzer used to detect end credits that consist of text overlaid on a black background.
/// Bisects the end of the video file to perform an efficient search.
/// </summary>
public class BlackFrameAnalyzer : IMediaFileAnalyzer
{
    private readonly TimeSpan _maximumError = new(0, 0, 4);

    private readonly ILogger<BlackFrameAnalyzer> _logger;

    private int minimumCreditsDuration;

    private int maximumCreditsDuration;

    private int blackFrameMinimumPercentage;

    /// <summary>
    /// Initializes a new instance of the <see cref="BlackFrameAnalyzer"/> class.
    /// </summary>
    /// <param name="logger">Logger.</param>
    public BlackFrameAnalyzer(ILogger<BlackFrameAnalyzer> logger)
    {
        var config = Plugin.Instance?.Configuration ?? new Configuration.PluginConfiguration();
        minimumCreditsDuration = config.MinimumCreditsDuration;
        maximumCreditsDuration = 2 * config.MaximumCreditsDuration;
        blackFrameMinimumPercentage = config.BlackFrameMinimumPercentage;

        _logger = logger;
    }

    /// <inheritdoc />
    public ReadOnlyCollection<QueuedEpisode> AnalyzeMediaFiles(
        ReadOnlyCollection<QueuedEpisode> analysisQueue,
        AnalysisMode mode,
        CancellationToken cancellationToken)
    {
        if (mode != AnalysisMode.Credits)
        {
            throw new NotImplementedException("mode must equal Credits");
        }

        var creditTimes = new Dictionary<Guid, XmlIntro>();

        bool isFirstEpisode = true;

        double searchStart = minimumCreditsDuration;

        var searchDistance = 2 * minimumCreditsDuration;

        foreach (var episode in analysisQueue)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            // Pre-check to find reasonable starting point.
            if (isFirstEpisode)
            {
                var scanTime = episode.Duration - searchStart;
                var tr = new TimeRange(scanTime - 0.5, scanTime); // Short search range since accuracy isn't important here.

                var frames = FFmpegWrapper.DetectBlackFrames(episode, tr, blackFrameMinimumPercentage);

                while (frames.Length > 0) // While black frames are found increase searchStart
                {
                    searchStart += searchDistance;

                    scanTime = episode.Duration - searchStart;
                    tr = new TimeRange(scanTime - 0.5, scanTime);

                    frames = FFmpegWrapper.DetectBlackFrames(episode, tr, blackFrameMinimumPercentage);

                    if (searchStart > maximumCreditsDuration)
                    {
                        searchStart = maximumCreditsDuration;
                        break;
                    }
                }

                if (searchStart == minimumCreditsDuration) // Skip if no black frames were found
                {
                    continue;
                }

                isFirstEpisode = false;
            }

            var intro = AnalyzeMediaFile(
                episode,
                searchStart,
                searchDistance,
                blackFrameMinimumPercentage);

            if (intro is null)
            {
                // If no credits were found, reset the first-episode search logic for the next episode in the sequence.
                searchStart = minimumCreditsDuration;
                isFirstEpisode = true;
                continue;
            }

            searchStart = episode.Duration - intro.IntroStart + (0.5 * searchDistance);

            creditTimes[episode.EpisodeId] = intro;
        }

        Plugin.Instance!.UpdateTimestamps(creditTimes, mode);

        return analysisQueue
            .Where(x => !creditTimes.ContainsKey(x.EpisodeId))
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Analyzes an individual media file. Only public because of unit tests.
    /// </summary>
    /// <param name="episode">Media file to analyze.</param>
    /// <param name="searchStart">Search Start Piont.</param>
    /// <param name="searchDistance">Search Distance.</param>
    /// <param name="minimum">Percentage of the frame that must be black.</param>
    /// <returns>Credits timestamp.</returns>
    public XmlIntro? AnalyzeMediaFile(QueuedEpisode episode, double searchStart, int searchDistance, int minimum)
    {
        // Start by analyzing the last N minutes of the file.
        var upperLimit = searchStart;
        var lowerLimit = Math.Max(searchStart - searchDistance, minimumCreditsDuration);
        var start = TimeSpan.FromSeconds(upperLimit);
        var end = TimeSpan.FromSeconds(lowerLimit);
        var firstFrameTime = 0.0;

        // Continue bisecting the end of the file until the range that contains the first black
        // frame is smaller than the maximum permitted error.
        while (start - end > _maximumError)
        {
            // Analyze the middle two seconds from the current bisected range
            var midpoint = (start + end) / 2;
            var scanTime = episode.Duration - midpoint.TotalSeconds;
            var tr = new TimeRange(scanTime, scanTime + 2);

            _logger.LogTrace(
                "{Episode}, dur {Duration}, bisect [{BStart}, {BEnd}], time [{Start}, {End}]",
                episode.Name,
                episode.Duration,
                start,
                end,
                tr.Start,
                tr.End);

            var frames = FFmpegWrapper.DetectBlackFrames(episode, tr, minimum);
            _logger.LogTrace(
                "{Episode} at {Start} has {Count} black frames",
                episode.Name,
                tr.Start,
                frames.Length);

            if (frames.Length == 0)
            {
                // Since no black frames were found, slide the range closer to the end
                start = midpoint - TimeSpan.FromSeconds(2);

                if (midpoint - TimeSpan.FromSeconds(lowerLimit) < _maximumError)
                {
                    lowerLimit = Math.Max(lowerLimit - (0.5 * searchDistance), minimumCreditsDuration);

                    // Reset end for a new search with the increased duration
                    end = TimeSpan.FromSeconds(lowerLimit);
                }
            }
            else
            {
                // Some black frames were found, slide the range closer to the start
                end = midpoint;
                firstFrameTime = frames[0].Time + scanTime;

                if (TimeSpan.FromSeconds(upperLimit) - midpoint < _maximumError)
                {
                    upperLimit = Math.Min(upperLimit + (0.5 * searchDistance), maximumCreditsDuration);

                    // Reset start for a new search with the increased duration
                    start = TimeSpan.FromSeconds(upperLimit);
                }
            }
        }

        if (firstFrameTime > 0)
        {
            return new(episode.EpisodeId, new TimeRange(firstFrameTime, episode.Duration));
        }

        return null;
    }
}
