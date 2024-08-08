using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Mime;
using ConfusedPolarBear.Plugin.IntroSkipper.Configuration;
using ConfusedPolarBear.Plugin.IntroSkipper.Data;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Entities.TV;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ConfusedPolarBear.Plugin.IntroSkipper.Controllers;

/// <summary>
/// Skip intro controller.
/// </summary>
[Authorize]
[ApiController]
[Produces(MediaTypeNames.Application.Json)]
public class SkipIntroController : ControllerBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SkipIntroController"/> class.
    /// </summary>
    public SkipIntroController()
    {
    }

    /// <summary>
    /// Returns the timestamps of the introduction in a television episode. Responses are in API version 1 format.
    /// </summary>
    /// <param name="id">ID of the episode. Required.</param>
    /// <param name="mode">Timestamps to return. Optional. Defaults to Introduction for backwards compatibility.</param>
    /// <response code="200">Episode contains an intro.</response>
    /// <response code="404">Failed to find an intro in the provided episode.</response>
    /// <returns>Detected intro.</returns>
    [HttpGet("Episode/{id}/IntroTimestamps")]
    [HttpGet("Episode/{id}/IntroTimestamps/v1")]
    public ActionResult<Intro> GetIntroTimestamps(
        [FromRoute] Guid id,
        [FromQuery] AnalysisMode mode = AnalysisMode.Introduction)
    {
        var intro = GetIntro(id, mode);

        if (intro is null || !intro.Valid)
        {
            return NotFound();
        }

        return intro;
    }

    /// <summary>
    /// Updates the timestamps for the provided episode.
    /// </summary>
    /// <param name="id">Episode ID to update timestamps for.</param>
    /// <param name="timestamps">New timestamps Introduction/Credits start and end times.</param>
    /// <response code="204">New timestamps saved.</response>
    /// <response code="404">Given ID is not an Episode.</response>
    /// <returns>No content.</returns>
    [Authorize(Policy = Policies.RequiresElevation)]
    [HttpPost("Episode/{Id}/Timestamps")]
    public ActionResult UpdateTimestamps([FromRoute] Guid id, [FromBody] TimeStamps timestamps)
    {
        // only update existing episodes
        var rawItem = Plugin.Instance!.GetItem(id);
        if (rawItem == null || rawItem is not Episode episode)
        {
            return NotFound();
        }

        if (timestamps?.Introduction.IntroEnd > 0.0)
        {
            var tr = new TimeRange(timestamps.Introduction.IntroStart, timestamps.Introduction.IntroEnd);
            Plugin.Instance!.Intros[id] = new Intro(id, tr);
        }

        if (timestamps?.Credits.IntroEnd > 0.0)
        {
            var cr = new TimeRange(timestamps.Credits.IntroStart, timestamps.Credits.IntroEnd);
            Plugin.Instance!.Credits[id] = new Intro(id, cr);
        }

        if (timestamps?.Recap.IntroEnd > 0.0)
        {
            var cr = new TimeRange(timestamps.Recap.IntroStart, timestamps.Recap.IntroEnd);
            Plugin.Instance!.Credits[id] = new Intro(id, cr);
        }

        Plugin.Instance!.SaveTimestamps(AnalysisMode.Introduction);
        Plugin.Instance!.SaveTimestamps(AnalysisMode.Credits);

        return NoContent();
    }

    /// <summary>
    /// Gets the timestamps for the provided episode.
    /// </summary>
    /// <param name="id">Episode ID.</param>
    /// <response code="200">Sucess.</response>
    /// <response code="404">Given ID is not an Episode.</response>
    /// <returns>Episode Timestamps.</returns>
    [HttpGet("Episode/{Id}/Timestamps")]
    [ActionName("UpdateTimestamps")]
    public ActionResult<TimeStamps> GetTimestamps([FromRoute] Guid id)
    {
        // only get return content for episodes
        var rawItem = Plugin.Instance!.GetItem(id);
        if (rawItem == null || rawItem is not Episode episode)
        {
            return NotFound();
        }

        var times = new TimeStamps();
        if (Plugin.Instance!.Intros.TryGetValue(id, out var introValue))
        {
            times.Introduction = introValue;
        }

        if (Plugin.Instance!.Credits.TryGetValue(id, out var creditValue))
        {
            times.Credits = creditValue;
        }

        return times;
    }

    /// <summary>
    /// Gets a dictionary of all skippable segments.
    /// </summary>
    /// <param name="id">Media ID.</param>
    /// <response code="200">Skippable segments dictionary.</response>
    /// <returns>Dictionary of skippable segments.</returns>
    [HttpGet("Episode/{id}/IntroSkipperSegments")]
    public ActionResult<Dictionary<AnalysisMode, Intro>> GetSkippableSegments([FromRoute] Guid id)
    {
        var segments = new Dictionary<AnalysisMode, Intro>();

        if (GetIntro(id, AnalysisMode.Introduction) is Intro intro)
        {
            segments[AnalysisMode.Introduction] = intro;
        }

        if (GetIntro(id, AnalysisMode.Credits) is Intro credits)
        {
            segments[AnalysisMode.Credits] = credits;
        }

        if (GetIntro(id, AnalysisMode.Recaps) is Intro recaps)
        {
            segments[AnalysisMode.Recaps] = recaps;
        }

        return segments;
    }

    /// <summary>Lookup and return the skippable timestamps for the provided item.</summary>
    /// <param name="id">Unique identifier of this episode.</param>
    /// <param name="mode">Mode.</param>
    /// <returns>Intro object if the provided item has an intro, null otherwise.</returns>
    private Intro? GetIntro(Guid id, AnalysisMode mode)
    {
        try
        {
            var timestamp = Plugin.Instance!.GetIntroByMode(id, mode);

            // Operate on a copy to avoid mutating the original Intro object stored in the dictionary.
            var segment = new Intro(timestamp);

            var config = Plugin.Instance.Configuration;
            segment.IntroEnd -= config.RemainingSecondsOfIntro;
            if (config.PersistSkipButton)
            {
                segment.ShowSkipPromptAt = segment.IntroStart;
                segment.HideSkipPromptAt = segment.IntroEnd;
            }
            else
            {
                segment.ShowSkipPromptAt = Math.Max(0, segment.IntroStart - config.ShowPromptAdjustment);
                segment.HideSkipPromptAt = Math.Min(
                    segment.IntroStart + config.HidePromptAdjustment,
                    segment.IntroEnd);
            }

            return segment;
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// Erases all previously discovered introduction timestamps.
    /// </summary>
    /// <param name="mode">Mode.</param>
    /// <param name="eraseCache">Erase cache.</param>
    /// <response code="204">Operation successful.</response>
    /// <returns>No content.</returns>
    [Authorize(Policy = Policies.RequiresElevation)]
    [HttpPost("Intros/EraseTimestamps")]
    public ActionResult ResetIntroTimestamps([FromQuery] AnalysisMode mode, [FromQuery] bool eraseCache = false)
    {
        if (mode == AnalysisMode.Introduction)
        {
            Plugin.Instance!.Intros.Clear();
        }
        else if (mode == AnalysisMode.Credits)
        {
            Plugin.Instance!.Credits.Clear();
        }
        else if (mode == AnalysisMode.Recaps)
        {
            Plugin.Instance!.Recaps.Clear();
        }

        if (eraseCache)
        {
            FFmpegWrapper.DeleteCacheFiles(mode);
        }

        Plugin.Instance!.EpisodeStates.Clear();
        Plugin.Instance!.SaveTimestamps(mode);
        return NoContent();
    }

    /// <summary>
    /// Get all introductions or credits. Only used by the end to end testing script.
    /// </summary>
    /// <param name="mode">Mode.</param>
    /// <response code="200">All timestamps have been returned.</response>
    /// <returns>List of IntroWithMetadata objects.</returns>
    [Authorize(Policy = Policies.RequiresElevation)]
    [HttpGet("Intros/All")]
    public ActionResult<List<IntroWithMetadata>> GetAllTimestamps(
        [FromQuery] AnalysisMode mode = AnalysisMode.Introduction)
    {
        List<IntroWithMetadata> intros = new();

        ConcurrentDictionary<Guid, Intro> timestamps = [];

        switch (mode)
        {
            case AnalysisMode.Introduction:
                timestamps = Plugin.Instance!.Intros;
                break;
            case AnalysisMode.Credits:
                timestamps = Plugin.Instance!.Credits;
                break;
            case AnalysisMode.Recaps:
                timestamps = Plugin.Instance!.Recaps;
                break;
        }

        // Get metadata for all intros
        foreach (var intro in timestamps)
        {
            // Get the details of the item from Jellyfin
            var rawItem = Plugin.Instance!.GetItem(intro.Key);
            if (rawItem == null || rawItem is not Episode episode)
            {
                throw new InvalidCastException("Unable to cast item id " + intro.Key + " to an Episode");
            }

            // Associate the metadata with the intro
            intros.Add(
                new IntroWithMetadata(
                episode.SeriesName,
                episode.AiredSeasonNumber ?? 0,
                episode.Name,
                intro.Value));
        }

        return intros;
    }

    /// <summary>
    /// Gets the user interface configuration.
    /// </summary>
    /// <response code="200">UserInterfaceConfiguration returned.</response>
    /// <returns>UserInterfaceConfiguration.</returns>
    [HttpGet]
    [Route("Intros/UserInterfaceConfiguration")]
    public ActionResult<UserInterfaceConfiguration> GetUserInterfaceConfiguration()
    {
        var config = Plugin.Instance!.Configuration;
        return new UserInterfaceConfiguration(
            config.SkipButtonVisible,
            config.SkipButtonIntroText,
            config.SkipButtonEndCreditsText);
    }
}
