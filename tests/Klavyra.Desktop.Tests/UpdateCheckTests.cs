using Xunit;

namespace Klavyra.Desktop.Tests;

public sealed class UpdateCheckTests
{
    private static string Latest(string tag, bool prerelease = false) =>
        $$"""{"tag_name":"{{tag}}","html_url":"https://github.com/J-Roque/Klavyra/releases/tag/{{tag}}","draft":false,"prerelease":{{(prerelease ? "true" : "false")}}}""";

    [Theory]
    [InlineData("v0.2.0", "0.2.0")]
    [InlineData("0.1.0+f1cdf64", "0.1.0")]
    [InlineData("1.0-beta", "1.0.0")]
    [InlineData("klavyra", null)]
    public void Tags_are_read_as_versions(string tag, string? expected) =>
        Assert.Equal(expected is null ? null : Version.Parse(expected), UpdateCheck.ParseVersion(tag));

    [Fact]
    public void Only_a_newer_final_release_is_announced()
    {
        var current = new Version(0, 1, 0);
        var newer = UpdateCheck.NewerRelease(Latest("v0.2.0"), current);
        Assert.Equal(new Version(0, 2, 0), newer!.Version);
        Assert.EndsWith("/releases/tag/v0.2.0", newer.PageUrl);

        Assert.Null(UpdateCheck.NewerRelease(Latest("v0.1.0"), current));
        Assert.Null(UpdateCheck.NewerRelease(Latest("v0.1"), current));
        Assert.Null(UpdateCheck.NewerRelease(Latest("v0.0.9"), current));
        Assert.Null(UpdateCheck.NewerRelease(Latest("v0.3.0", prerelease: true), current));
    }

    [Fact]
    public void The_running_version_comes_from_the_shared_build_props() =>
        Assert.Equal(new Version(0, 1, 0), UpdateCheck.Current);
}
