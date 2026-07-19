namespace Pitchify.Helper.Tests;

public sealed class UpdateValidationTests
{
    [Theory]
    [InlineData("owner/Pitchify", true)]
    [InlineData("owner-name/pitchify.mod", true)]
    [InlineData("", false)]
    [InlineData("owner", false)]
    [InlineData("owner/repo/extra", false)]
    [InlineData("owner name/repo", false)]
    [InlineData("__PITCHIFY_GITHUB_REPOSITORY__", false)]
    public void ValidatesGitHubRepositoryNames(
        string repository,
        bool expected)
    {
        Assert.Equal(
            expected,
            UpdateValidation.IsValidRepository(repository));
    }

    [Theory]
    [InlineData("v1.2.1", "1.2.0", true)]
    [InlineData("2.0.0", "1.9.9", true)]
    [InlineData("v1.2.0", "1.2.0", false)]
    [InlineData("v1.1.9", "1.2.0", false)]
    [InlineData("not-a-version", "1.2.0", false)]
    public void ComparesReleaseVersions(
        string latest,
        string current,
        bool expected)
    {
        Assert.Equal(
            expected,
            UpdateValidation.IsNewerVersion(latest, current));
    }

    [Fact]
    public void ParsesGitHubSha256Digest()
    {
        const string digest =
            "sha256:2151b604e3429bff440b9fbc03eb3617bc2603cda96c95b9bb05277f9ddba255";

        Assert.True(UpdateValidation.TryParseSha256(digest, out var hash));
        Assert.Equal(32, hash.Length);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("md5:2151")]
    [InlineData("sha256:xyz")]
    public void RejectsInvalidAssetDigests(string? digest)
    {
        Assert.False(UpdateValidation.TryParseSha256(digest, out _));
    }
}
