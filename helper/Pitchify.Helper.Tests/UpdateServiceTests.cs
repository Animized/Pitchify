using System.Net;
using System.Text;

namespace Pitchify.Helper.Tests;

public sealed class UpdateServiceTests
{
    [Fact]
    public async Task FindsNewerVerifiedGitHubRelease()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"pitchify-update-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var configPath = Path.Combine(directory, "config.json");
            await File.WriteAllTextAsync(
                configPath,
                """
                {
                  "apiToken": "test-token",
                  "semitones": 0,
                  "outputDeviceId": null,
                  "updateRepository": "owner/Pitchify"
                }
                """);
            var store = new ConfigStore(configPath);
            var logger = new FileLogger(
                Path.Combine(directory, "logs", "pitchify.log"));
            using var service = new UpdateService(
                store,
                logger,
                directory,
                currentVersion: "1.2.0",
                new StubHttpMessageHandler(
                    """
                    {
                      "tag_name": "v1.3.0",
                      "assets": [
                        {
                          "name": "Pitchify-win-x64.zip",
                          "state": "uploaded",
                          "size": 12345,
                          "digest": "sha256:2151b604e3429bff440b9fbc03eb3617bc2603cda96c95b9bb05277f9ddba255",
                          "browser_download_url": "https://github.com/owner/Pitchify/releases/download/v1.3.0/Pitchify-win-x64.zip"
                        }
                      ]
                    }
                    """));

            var update = await service.CheckForUpdatesAsync();

            Assert.Equal(UpdateState.Available, update.State);
            Assert.Equal("1.2.0", update.CurrentVersion);
            Assert.Equal("1.3.0", update.LatestVersion);
            Assert.Null(update.Message);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _json;

        public StubHttpMessageHandler(string json)
        {
            _json = json;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.Equal(
                "https://api.github.com/repos/owner/Pitchify/releases/latest",
                request.RequestUri?.AbsoluteUri);
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        _json,
                        Encoding.UTF8,
                        "application/json"),
                });
        }
    }
}
