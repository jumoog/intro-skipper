using System;
using Xunit;

namespace ConfusedPolarBear.Plugin.IntroSkipper.Tests;

public class TestEdl
{
    // Test data is from https://kodi.wiki/view/Edit_decision_list#MPlayer_EDL
    [Theory]
    [InlineData(5.3, 7.1, EdlAction.Cut, "5.3 7.1 0")]
    [InlineData(15, 16.7, EdlAction.Mute, "15 16.7 1")]
    [InlineData(420, 822, EdlAction.CommercialBreak, "420 822 3")]
    [InlineData(1, 255.3, EdlAction.SceneMarker, "1 255.3 2")]
    [InlineData(1.123456789, 5.654647987, EdlAction.CommercialBreak, "1.12 5.65 3")]
    public void TestEdlSerialization(double start, double end, EdlAction action, string expected)
    {
        var intro = MakeIntro(start, end);
        var actual = intro.ToEdl(action);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void TestEdlInvalidSerialization()
    {
        Assert.Throws<ArgumentException>(() => {
            var intro = MakeIntro(0, 5);
            intro.ToEdl(EdlAction.None);
        });
    }

    [Theory]
    [InlineData("Death Note - S01E12 - Love.mkv", "Death Note - S01E12 - Love.edl")]
    [InlineData("/full/path/to/file.rm", "/full/path/to/file.edl")]
    public void TestEdlPath(string mediaPath, string edlPath)
    {
        Assert.Equal(edlPath, EdlManager.GetEdlPath(mediaPath));
    }

    private XmlIntro MakeIntro(double start, double end)
    {
        return new XmlIntro(Guid.Empty, new TimeRange(start, end));
    }
}
