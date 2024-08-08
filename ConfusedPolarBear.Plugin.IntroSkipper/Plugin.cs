using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using ConfusedPolarBear.Plugin.IntroSkipper.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;

namespace ConfusedPolarBear.Plugin.IntroSkipper;

/// <summary>
/// Intro skipper plugin. Uses audio analysis to find common sequences of audio shared between episodes.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    private readonly object _serializationLock = new();
    private readonly object _introsLock = new();
    private ILibraryManager _libraryManager;
    private IItemRepository _itemRepository;
    private ILogger<Plugin> _logger;
    private string _introPath;
    private string _creditsPath;
    private string _repcapPath;

    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
    /// <param name="serverConfiguration">Server configuration manager.</param>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="itemRepository">Item repository.</param>
    /// <param name="logger">Logger.</param>
    public Plugin(
        IApplicationPaths applicationPaths,
        IXmlSerializer xmlSerializer,
        IServerConfigurationManager serverConfiguration,
        ILibraryManager libraryManager,
        IItemRepository itemRepository,
        ILogger<Plugin> logger)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;

        _libraryManager = libraryManager;
        _itemRepository = itemRepository;
        _logger = logger;

        FFmpegPath = serverConfiguration.GetEncodingOptions().EncoderAppPathDisplay;

        ArgumentNullException.ThrowIfNull(applicationPaths);

        var pluginDirName = "introskipper";
        var pluginCachePath = "chromaprints";

        var introsDirectory = Path.Join(applicationPaths.DataPath, pluginDirName);
        FingerprintCachePath = Path.Join(introsDirectory, pluginCachePath);
        _introPath = Path.Join(applicationPaths.DataPath, pluginDirName, "intros.xml");
        _creditsPath = Path.Join(applicationPaths.DataPath, pluginDirName, "credits.xml");
        _repcapPath = Path.Join(applicationPaths.DataPath, pluginDirName, "recaps.xml");

        var cacheRoot = applicationPaths.CachePath;
        var oldIntrosDirectory = Path.Join(cacheRoot, pluginDirName);
        if (!Directory.Exists(oldIntrosDirectory))
        {
            pluginDirName = "intros";
            pluginCachePath = "cache";
            cacheRoot = applicationPaths.PluginConfigurationsPath;
            oldIntrosDirectory = Path.Join(cacheRoot, pluginDirName);
        }

        var oldFingerprintCachePath = Path.Join(oldIntrosDirectory, pluginCachePath);
        var oldIntroPath = Path.Join(cacheRoot, pluginDirName, "intros.xml");
        var oldCreditsPath = Path.Join(cacheRoot, pluginDirName, "credits.xml");

        // Create the base & cache directories (if needed).
        if (!Directory.Exists(FingerprintCachePath))
        {
            Directory.CreateDirectory(FingerprintCachePath);

            // Check if the old cache directory exists
            if (Directory.Exists(oldFingerprintCachePath))
            {
                // move intro.xml if exists
                if (File.Exists(oldIntroPath))
                {
                    File.Move(oldIntroPath, _introPath);
                }

                // move credits.xml if exists
                if (File.Exists(oldCreditsPath))
                {
                    File.Move(oldCreditsPath, _creditsPath);
                }

                // Move the contents from old directory to new directory
                string[] files = Directory.GetFiles(oldFingerprintCachePath);
                foreach (string file in files)
                {
                    string fileName = Path.GetFileName(file);
                    string destFile = Path.Combine(FingerprintCachePath, fileName);
                    File.Move(file, destFile);
                }

                // Optionally, you may delete the old directory after moving its contents
                Directory.Delete(oldIntrosDirectory, true);
            }
        }

        // migrate from XMLSchema to DataContract
        XmlSerializationHelper.MigrateXML(_introPath);
        XmlSerializationHelper.MigrateXML(_creditsPath);
        XmlSerializationHelper.MigrateXML(_repcapPath);

        // TODO: remove when https://github.com/jellyfin/jellyfin-meta/discussions/30 is complete
        try
        {
            RestoreTimestamps();
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Unable to load introduction timestamps: {Exception}", ex);
        }

        // Inject the skip intro button code into the web interface.
        var indexPath = Path.Join(applicationPaths.WebPath, "index.html");
        try
        {
            InjectSkipButton(indexPath);
        }
        catch (Exception ex)
        {
            WarningManager.SetFlag(PluginWarning.UnableToAddSkipButton);

            _logger.LogError("Failed to add skip button to web interface. See https://github.com/jumoog/intro-skipper?tab=readme-ov-file#skip-button-is-not-visible for the most common issues. Error: {Error}", ex);
        }

        FFmpegWrapper.CheckFFmpegVersion();
    }

    /// <summary>
    /// Gets the results of fingerprinting all episodes.
    /// </summary>
    public ConcurrentDictionary<Guid, Intro> Intros { get; } = new();

    /// <summary>
    /// Gets all discovered ending credits.
    /// </summary>
    public ConcurrentDictionary<Guid, Intro> Credits { get; } = new();

    /// <summary>
    /// Gets all discovered ending credits.
    /// </summary>
    public ConcurrentDictionary<Guid, Intro> Recaps { get; } = new();

    /// <summary>
    /// Gets the most recent media item queue.
    /// </summary>
    public ConcurrentDictionary<Guid, List<QueuedEpisode>> QueuedMediaItems { get; } = new();

    /// <summary>
    /// Gets all episode states.
    /// </summary>
    public ConcurrentDictionary<Guid, EpisodeState> EpisodeStates { get; } = new();

    /// <summary>
    /// Gets or sets the total number of episodes in the queue.
    /// </summary>
    public int TotalQueued { get; set; }

    /// <summary>
    /// Gets or sets the number of seasons in the queue.
    /// </summary>
    public int TotalSeasons { get; set; }

    /// <summary>
    /// Gets the directory to cache fingerprints in.
    /// </summary>
    public string FingerprintCachePath { get; private set; }

    /// <summary>
    /// Gets the full path to FFmpeg.
    /// </summary>
    public string FFmpegPath { get; private set; }

    /// <inheritdoc />
    public override string Name => "Intro Skipper";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("c83d86bb-a1e0-4c35-a113-e2101cf4ee6b");

    /// <summary>
    /// Gets the plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <summary>
    /// Save timestamps to disk.
    /// </summary>
    /// <param name="mode">Mode.</param>
    public void SaveTimestamps(AnalysisMode mode)
    {
        List<Intro> introList = new List<Intro>();
        var filePath = string.Empty;
        switch (mode)
        {
            case AnalysisMode.Introduction:
                {
                    filePath = _introPath;
                    lock (_introsLock)
                    {
                        introList.AddRange(Instance!.Intros.Values);
                    }

                    break;
                }

            case AnalysisMode.Credits:
                {
                    filePath = _creditsPath;
                    lock (_introsLock)
                    {
                        introList.AddRange(Instance!.Credits.Values);
                    }

                    break;
                }

            case AnalysisMode.Recaps:
                {
                    filePath = _repcapPath;
                    lock (_introsLock)
                    {
                        introList.AddRange(Instance!.Recaps.Values);
                    }

                    break;
                }

            default:
                break;
        }

        lock (_serializationLock)
        {
            try
            {
                XmlSerializationHelper.SerializeToXml(introList, filePath);
            }
            catch (Exception e)
            {
                _logger.LogError("SaveTimestamps {Message}", e.Message);
            }
        }
    }

    /// <summary>
    /// Restore previous analysis results from disk.
    /// </summary>
    public void RestoreTimestamps()
    {
        if (File.Exists(_introPath))
        {
            // Since dictionaries can't be easily serialized, analysis results are stored on disk as a list.
            var introList = XmlSerializationHelper.DeserializeFromXml(_introPath);

            foreach (var intro in introList)
            {
                Instance!.Intros.TryAdd(intro.EpisodeId, intro);
            }
        }

        if (File.Exists(_creditsPath))
        {
            var creditList = XmlSerializationHelper.DeserializeFromXml(_creditsPath);

            foreach (var credit in creditList)
            {
                Instance!.Credits.TryAdd(credit.EpisodeId, credit);
            }
        }

        if (File.Exists(_repcapPath))
        {
            var recapList = XmlSerializationHelper.DeserializeFromXml(_repcapPath);

            foreach (var recap in recapList)
            {
                Instance!.Recaps.TryAdd(recap.EpisodeId, recap);
            }
        }
    }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new[]
        {
            new PluginPageInfo
            {
                Name = this.Name,
                EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html"
            },
            new PluginPageInfo
            {
                Name = "visualizer.js",
                EmbeddedResourcePath = GetType().Namespace + ".Configuration.visualizer.js"
            },
            new PluginPageInfo
            {
                Name = "skip-intro-button.js",
                EmbeddedResourcePath = GetType().Namespace + ".Configuration.inject.js"
            }
        };
    }

    /// <summary>
    /// Gets the commit used to build the plugin.
    /// </summary>
    /// <returns>Commit.</returns>
    public string GetCommit()
    {
        var commit = string.Empty;

        var path = GetType().Namespace + ".Configuration.version.txt";
        using var stream = GetType().Assembly.GetManifestResourceStream(path);
        if (stream is null)
        {
            _logger.LogWarning("Unable to read embedded version information");
            return commit;
        }

        using var reader = new StreamReader(stream);
        commit = reader.ReadToEnd().TrimEnd();

        if (commit == "unknown")
        {
            _logger.LogTrace("Embedded version information was not valid, ignoring");
            return string.Empty;
        }

        _logger.LogInformation("Unstable plugin version built from commit {Commit}", commit);
        return commit;
    }

    /// <summary>
    /// Gets the Intro for this item.
    /// </summary>
    /// <param name="id">Item id.</param>
    /// <param name="mode">Mode.</param>
    /// <returns>Intro.</returns>
    internal Intro GetIntroByMode(Guid id, AnalysisMode mode)
    {
        switch (mode)
        {
            case AnalysisMode.Introduction:
                return Instance!.Intros[id];
            case AnalysisMode.Credits:
                return Instance!.Credits[id];
            case AnalysisMode.Recaps:
                return Instance!.Recaps[id];
        }

        return new Intro();
    }

    internal BaseItem? GetItem(Guid id)
    {
        return _libraryManager.GetItemById(id);
    }

    /// <summary>
    /// Gets the full path for an item.
    /// </summary>
    /// <param name="id">Item id.</param>
    /// <returns>Full path to item.</returns>
    internal string GetItemPath(Guid id)
    {
        var item = GetItem(id);
        if (item == null)
        {
            // Handle the case where the item is not found
            _logger.LogWarning("Item with ID {Id} not found.", id);
            return string.Empty;
        }

        return item.Path;
    }

    /// <summary>
    /// Gets all chapters for this item.
    /// </summary>
    /// <param name="id">Item id.</param>
    /// <returns>List of chapters.</returns>
    internal List<ChapterInfo> GetChapters(Guid id)
    {
        var item = GetItem(id);
        if (item == null)
        {
            // Handle the case where the item is not found
            _logger.LogWarning("Item with ID {Id} not found.", id);
            return new List<ChapterInfo>();
        }

        return _itemRepository.GetChapters(item);
    }

    /// <summary>
    /// Gets the state for this item.
    /// </summary>
    /// <param name="id">Item ID.</param>
    /// <returns>State of this item.</returns>
    internal EpisodeState GetState(Guid id) => EpisodeStates.GetOrAdd(id, _ => new EpisodeState());

    internal void UpdateTimestamps(Dictionary<Guid, Intro> newTimestamps, AnalysisMode mode)
    {
        foreach (var intro in newTimestamps)
        {
            if (mode == AnalysisMode.Introduction)
            {
                Instance!.Intros.AddOrUpdate(intro.Key, intro.Value, (key, oldValue) => intro.Value);
            }
            else if (mode == AnalysisMode.Credits)
            {
                 Instance!.Credits.AddOrUpdate(intro.Key, intro.Value, (key, oldValue) => intro.Value);
            }
            else if (mode == AnalysisMode.Recaps)
            {
                Instance!.Recaps.AddOrUpdate(intro.Key, intro.Value, (key, oldValue) => intro.Value);
            }
        }

        SaveTimestamps(mode);
    }

    internal void CleanTimestamps(HashSet<Guid> validEpisodeIds)
    {
        var allKeys = new HashSet<Guid>(Instance!.Intros.Keys);
        allKeys.UnionWith(Instance!.Credits.Keys);

        foreach (var key in allKeys)
        {
            if (!validEpisodeIds.Contains(key))
            {
                Instance!.Intros.TryRemove(key, out _);
                Instance!.Credits.TryRemove(key, out _);
                Instance!.Recaps.TryRemove(key, out _);
            }
        }

        SaveTimestamps(AnalysisMode.Introduction);
        SaveTimestamps(AnalysisMode.Credits);
        SaveTimestamps(AnalysisMode.Recaps);
    }

    /// <summary>
    /// Inject the skip button script into the web interface.
    /// </summary>
    /// <param name="indexPath">Full path to index.html.</param>
    private void InjectSkipButton(string indexPath)
    {
        // Parts of this code are based off of JellyScrub's script injection code.
        // https://github.com/nicknsy/jellyscrub/blob/main/Nick.Plugin.Jellyscrub/JellyscrubPlugin.cs#L38

        _logger.LogDebug("Reading index.html from {Path}", indexPath);
        var contents = File.ReadAllText(indexPath);

        // change URL with every relase to prevent the Browers from caching
        var scriptTag = "<script src=\"configurationpage?name=skip-intro-button.js&release=" + GetType().Assembly.GetName().Version + "\"></script>";

        // Only inject the script tag once
        if (contents.Contains(scriptTag, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Skip button already added");
            return;
        }

        // remove old version if necessary
        string pattern = @"<script src=""configurationpage\?name=skip-intro-button\.js.*<\/script>";
        contents = Regex.Replace(contents, pattern, string.Empty, RegexOptions.IgnoreCase);

        // Inject a link to the script at the end of the <head> section.
        // A regex is used here to ensure the replacement is only done once.
        var headEnd = new Regex("</head>", RegexOptions.IgnoreCase);
        contents = headEnd.Replace(contents, scriptTag + "</head>", 1);

        // Write the modified file contents
        File.WriteAllText(indexPath, contents);

        _logger.LogInformation("Skip intro button successfully added");
    }
}
