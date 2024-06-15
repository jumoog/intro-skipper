namespace ConfusedPolarBear.Plugin.IntroSkipper.Tests;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;
using Xunit;

public class TestChapterAnalyzer
{
    [Theory]
    [InlineData("Opening")]
    [InlineData("OP")]
    [InlineData("Intro")]
    [InlineData("Intro Start")]
    [InlineData("Introduction")]
    public void TestIntroductionExpression(string chapterName)
    {
        var chapters = CreateChapters(chapterName, AnalysisMode.Introduction);
        var introChapter = FindChapter(chapters, AnalysisMode.Introduction);

        Assert.NotNull(introChapter);
        Assert.Equal(60, introChapter.IntroStart);
        Assert.Equal(90, introChapter.IntroEnd);
    }

    [Theory]
    [InlineData("End Credits")]
    [InlineData("Ending")]
    [InlineData("Credit start")]
    [InlineData("Closing Credits")]
    [InlineData("Credits")]
    public void TestEndCreditsExpression(string chapterName)
    {
        var chapters = CreateChapters(chapterName, AnalysisMode.Credits);
        var creditsChapter = FindChapter(chapters, AnalysisMode.Credits);

        Assert.NotNull(creditsChapter);
        Assert.Equal(1890, creditsChapter.IntroStart);
        Assert.Equal(2000, creditsChapter.IntroEnd);
    }

    private XmlIntro? FindChapter(Collection<ChapterInfo> chapters, AnalysisMode mode)
    {
        var logger = new LoggerFactory().CreateLogger<ChapterAnalyzer>();
        var analyzer = new ChapterAnalyzer(logger);

        var config = new Configuration.PluginConfiguration();
        var expression = mode == AnalysisMode.Introduction ?
            config.ChapterAnalyzerIntroductionPattern :
            config.ChapterAnalyzerEndCreditsPattern;

        return analyzer.FindMatchingChapter(new() { Duration = 2000 }, chapters, expression, mode);
    }

    private Collection<ChapterInfo> CreateChapters(string name, AnalysisMode mode)
    {
        var chapters = new[]{
            CreateChapter("Cold Open", 0),
            CreateChapter(mode == AnalysisMode.Introduction ? name : "Introduction", 60),
            CreateChapter("Main Episode", 90),
            CreateChapter(mode == AnalysisMode.Credits ? name : "Credits", 1890)
        };

        return new(new List<ChapterInfo>(chapters));
    }

    /// <summary>
    /// Create a ChapterInfo object.
    /// </summary>
    /// <param name="name">Chapter name.</param>
    /// <param name="position">Chapter position (in seconds).</param>
    /// <returns>ChapterInfo.</returns>
    private ChapterInfo CreateChapter(string name, int position)
    {
        return new()
        {
            Name = name,
            StartPositionTicks = TimeSpan.FromSeconds(position).Ticks
        };
    }
}
