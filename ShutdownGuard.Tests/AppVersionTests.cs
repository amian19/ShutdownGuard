using ShutdownGuard.Services;
using Xunit;

namespace ShutdownGuard.Tests;

public class AppVersionTests
{
    [Theory]
    [InlineData("1.2.3", 1, 2, 3)]
    [InlineData("v1.2.3", 1, 2, 3)]
    [InlineData("V1.2.3", 1, 2, 3)]
    [InlineData("1.2.3+abc", 1, 2, 3)]
    [InlineData("1.2.3-beta", 1, 2, 3)]
    public void TryParse_AcceptsCommonTags(string raw, int major, int minor, int build)
    {
        Assert.True(AppVersion.TryParse(raw, out var v));
        Assert.Equal(new Version(major, minor, build), v);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-version")]
    public void TryParse_RejectsGarbage(string raw)
    {
        Assert.False(AppVersion.TryParse(raw, out _));
    }

    [Fact]
    public void IsNewerThan_ComparesCorrectly()
    {
        Assert.True(AppVersion.IsNewerThan(new Version(1, 1, 0), new Version(1, 0, 1)));
        Assert.False(AppVersion.IsNewerThan(new Version(1, 0, 1), new Version(1, 1, 0)));
        Assert.False(AppVersion.IsNewerThan(new Version(1, 1, 0), new Version(1, 1, 0)));
    }
}
