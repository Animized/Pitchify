using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pitchify.Helper;

public static class UpdateValidation
{
    public static bool IsValidRepository(string? repository)
    {
        if (string.IsNullOrWhiteSpace(repository))
        {
            return false;
        }

        var parts = repository.Split('/');
        return parts.Length == 2
            && parts.All(part =>
                part.Length is > 0 and <= 100
                && part.All(character =>
                    char.IsAsciiLetterOrDigit(character)
                    || character is '-' or '_' or '.'));
    }

    public static bool IsNewerVersion(string tagName, string currentVersion)
    {
        return TryParseVersion(tagName, out var latest)
            && TryParseVersion(currentVersion, out var current)
            && latest > current;
    }

    public static bool TryParseSha256(string? digest, out byte[] hash)
    {
        hash = Array.Empty<byte>();
        const string prefix = "sha256:";
        if (digest is null
            || !digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var hex = digest[prefix.Length..];
        if (hex.Length != 64)
        {
            return false;
        }

        try
        {
            hash = Convert.FromHexString(hex);
            return hash.Length == 32;
        }
        catch (FormatException)
        {
            hash = Array.Empty<byte>();
            return false;
        }
    }

    public static string NormalizeVersion(string tagName) =>
        tagName.Trim().TrimStart('v', 'V');

    private static bool TryParseVersion(string value, out Version version) =>
        Version.TryParse(NormalizeVersion(value), out version!);
}

public sealed class UpdateService : IDisposable
{
    public const string ReleaseAssetName = "Pitchify-win-x64.zip";
    private const long MaximumReleaseBytes = 100 * 1024 * 1024;
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly object _gate = new();
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private readonly ConfigStore _configStore;
    private readonly FileLogger _logger;
    private readonly string _dataDirectory;
    private readonly string _currentVersion;
    private readonly HttpClient _httpClient;
    private readonly Timer _checkTimer;
    private UpdateDto _status;
    private GitHubReleaseAsset? _availableAsset;
    private bool _disposed;

    public UpdateService(
        ConfigStore configStore,
        FileLogger logger,
        string dataDirectory,
        string currentVersion,
        HttpMessageHandler? httpMessageHandler = null)
    {
        _configStore = configStore;
        _logger = logger;
        _dataDirectory = dataDirectory;
        _currentVersion = currentVersion;
        _httpClient = httpMessageHandler is null
            ? new HttpClient()
            : new HttpClient(httpMessageHandler);
        _httpClient.Timeout = TimeSpan.FromSeconds(45);
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("Pitchify", currentVersion));
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue(
                "application/vnd.github+json"));
        _httpClient.DefaultRequestHeaders.Add(
            "X-GitHub-Api-Version",
            "2022-11-28");

        var repository = configStore.Snapshot().UpdateRepository;
        _status = UpdateValidation.IsValidRepository(repository)
            ? new UpdateDto(
                currentVersion,
                null,
                UpdateState.Checking,
                null)
            : new UpdateDto(
                currentVersion,
                null,
                UpdateState.Disabled,
                null);
        _checkTimer = new Timer(
            _ => _ = CheckForUpdatesAsync(),
            null,
            Timeout.Infinite,
            Timeout.Infinite);
    }

    public void Start()
    {
        if (_status.State == UpdateState.Disabled)
        {
            return;
        }

        _checkTimer.Change(TimeSpan.FromSeconds(3), CheckInterval);
    }

    public UpdateDto GetStatus()
    {
        lock (_gate)
        {
            return _status;
        }
    }

    public async Task<UpdateDto> CheckForUpdatesAsync(
        CancellationToken cancellationToken = default)
    {
        var repository = _configStore.Snapshot().UpdateRepository;
        if (!UpdateValidation.IsValidRepository(repository))
        {
            return SetStatus(
                new UpdateDto(
                    _currentVersion,
                    null,
                    UpdateState.Disabled,
                    null));
        }

        if (!await _operationLock.WaitAsync(0, cancellationToken))
        {
            return GetStatus();
        }

        try
        {
            SetStatus(
                GetStatus() with
                {
                    State = UpdateState.Checking,
                    Message = null,
                });

            var requestUrl =
                $"https://api.github.com/repos/{repository}/releases/latest";
            using var response = await _httpClient.GetAsync(
                requestUrl,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var responseStream =
                await response.Content.ReadAsStreamAsync(cancellationToken);
            var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(
                responseStream,
                JsonOptions,
                cancellationToken);
            var asset = release?.Assets.FirstOrDefault(candidate =>
                string.Equals(
                    candidate.Name,
                    ReleaseAssetName,
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    candidate.State,
                    "uploaded",
                    StringComparison.OrdinalIgnoreCase)
                && candidate.Size is > 0 and <= MaximumReleaseBytes
                && Uri.TryCreate(
                    candidate.BrowserDownloadUrl,
                    UriKind.Absolute,
                    out var downloadUri)
                && string.Equals(
                    downloadUri.Scheme,
                    Uri.UriSchemeHttps,
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    downloadUri.Host,
                    "github.com",
                    StringComparison.OrdinalIgnoreCase)
                && UpdateValidation.TryParseSha256(
                    candidate.Digest,
                    out _));

            if (release is null || asset is null)
            {
                throw new InvalidDataException(
                    $"The latest GitHub Release does not contain a verified {ReleaseAssetName} asset.");
            }

            if (UpdateValidation.IsNewerVersion(
                    release.TagName,
                    _currentVersion))
            {
                lock (_gate)
                {
                    _availableAsset = asset;
                }

                return SetStatus(
                    new UpdateDto(
                        _currentVersion,
                        UpdateValidation.NormalizeVersion(release.TagName),
                        UpdateState.Available,
                        null));
            }

            lock (_gate)
            {
                _availableAsset = null;
            }

            return SetStatus(
                new UpdateDto(
                    _currentVersion,
                    UpdateValidation.NormalizeVersion(release.TagName),
                    UpdateState.UpToDate,
                    null));
        }
        catch (Exception exception)
            when (exception is HttpRequestException
                or IOException
                or JsonException
                or TaskCanceledException)
        {
            _logger.Error("Update check failed.", exception);
            return SetStatus(
                new UpdateDto(
                    _currentVersion,
                    GetStatus().LatestVersion,
                    UpdateState.Error,
                    "Pitchify could not check GitHub for updates."));
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task<UpdateDto> InstallAvailableUpdateAsync(
        CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            GitHubReleaseAsset? asset;
            lock (_gate)
            {
                asset = _availableAsset;
            }

            if (asset is null || GetStatus().State != UpdateState.Available)
            {
                throw new InvalidOperationException(
                    "No verified Pitchify update is currently available.");
            }

            if (!UpdateValidation.TryParseSha256(
                    asset.Digest,
                    out var expectedHash))
            {
                throw new InvalidDataException(
                    "The GitHub Release asset has no valid SHA-256 digest.");
            }

            SetStatus(
                GetStatus() with
                {
                    State = UpdateState.Downloading,
                    Message = null,
                });

            var updateRoot = Path.Combine(
                _dataDirectory,
                "updates",
                $"v{GetStatus().LatestVersion}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(updateRoot);
            var archivePath = Path.Combine(updateRoot, ReleaseAssetName);
            var extractDirectory = Path.Combine(updateRoot, "release");

            await DownloadReleaseAsync(
                asset,
                archivePath,
                expectedHash,
                cancellationToken);
            ExtractReleaseSafely(archivePath, extractDirectory);

            var installerPath = Path.Combine(
                extractDirectory,
                "install.ps1");
            var helperPath = Path.Combine(
                extractDirectory,
                "helper",
                "Pitchify.Helper.exe");
            var extensionPath = Path.Combine(
                extractDirectory,
                "pitchify.template.js");
            if (!File.Exists(installerPath)
                || !File.Exists(helperPath)
                || !File.Exists(extensionPath))
            {
                throw new InvalidDataException(
                    "The verified release archive is incomplete.");
            }

            SetStatus(
                GetStatus() with
                {
                    State = UpdateState.Installing,
                    Message = "Pitchify is restarting to finish the update.",
                });

            var process = Process.Start(
                new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments =
                        $"-NoProfile -ExecutionPolicy Bypass -File \"{installerPath}\" -Silent -WaitForProcessId {Environment.ProcessId}",
                    WorkingDirectory = extractDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                });
            if (process is null)
            {
                throw new InvalidOperationException(
                    "Windows could not start the Pitchify updater.");
            }

            _logger.Info(
                $"Verified update {GetStatus().LatestVersion} is ready; handing off to the installer.");
            _ = Task.Run(async () =>
            {
                await Task.Delay(1000);
                Environment.Exit(0);
            });
            return GetStatus();
        }
        catch (Exception exception)
            when (exception is HttpRequestException
                or IOException
                or InvalidDataException
                or CryptographicException
                or TaskCanceledException)
        {
            _logger.Error("Automatic update failed.", exception);
            return SetStatus(
                GetStatus() with
                {
                    State = UpdateState.Error,
                    Message =
                        $"Automatic update failed: {exception.Message}",
                });
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private async Task DownloadReleaseAsync(
        GitHubReleaseAsset asset,
        string destination,
        byte[] expectedHash,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            asset.BrowserDownloadUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var contentLength = response.Content.Headers.ContentLength;
        if (contentLength is <= 0 or > MaximumReleaseBytes
            || (contentLength.HasValue
                && asset.Size > 0
                && contentLength.Value != asset.Size))
        {
            throw new InvalidDataException(
                "The downloaded release size does not match GitHub's asset metadata.");
        }

        await using (var source =
                     await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var destinationStream = new FileStream(
                         destination,
                         FileMode.CreateNew,
                         FileAccess.Write,
                         FileShare.None,
                         bufferSize: 81920,
                         useAsync: true))
        {
            var buffer = new byte[81920];
            long downloadedBytes = 0;
            int bytesRead;
            while ((bytesRead = await source.ReadAsync(
                       buffer,
                       cancellationToken)) > 0)
            {
                downloadedBytes += bytesRead;
                if (downloadedBytes > MaximumReleaseBytes)
                {
                    throw new InvalidDataException(
                        "The downloaded release exceeds Pitchify's size limit.");
                }

                await destinationStream.WriteAsync(
                    buffer.AsMemory(0, bytesRead),
                    cancellationToken);
            }

            if (downloadedBytes <= 0
                || (asset.Size > 0 && downloadedBytes != asset.Size))
            {
                throw new InvalidDataException(
                    "The downloaded release size does not match GitHub's asset metadata.");
            }
        }

        await using var verificationStream = File.OpenRead(destination);
        var actualHash = await SHA256.HashDataAsync(
            verificationStream,
            cancellationToken);
        if (!CryptographicOperations.FixedTimeEquals(
                actualHash,
                expectedHash))
        {
            throw new CryptographicException(
                "The release ZIP failed SHA-256 verification.");
        }
    }

    private static void ExtractReleaseSafely(
        string archivePath,
        string destination)
    {
        Directory.CreateDirectory(destination);
        var destinationRoot = Path.GetFullPath(destination)
            .TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            var normalizedName = entry.FullName.Replace(
                '/',
                Path.DirectorySeparatorChar);
            var destinationPath = Path.GetFullPath(
                Path.Combine(destination, normalizedName));
            if (!destinationPath.StartsWith(
                    destinationRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "The release ZIP contains an unsafe path.");
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            entry.ExtractToFile(destinationPath, overwrite: false);
        }
    }

    private UpdateDto SetStatus(UpdateDto status)
    {
        lock (_gate)
        {
            _status = status;
            return _status;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _checkTimer.Dispose();
        _operationLock.Dispose();
        _httpClient.Dispose();
    }

    internal sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("assets")] GitHubReleaseAsset[] Assets);

    internal sealed record GitHubReleaseAsset(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("state")] string State,
        [property: JsonPropertyName("size")] long Size,
        [property: JsonPropertyName("digest")] string? Digest,
        [property: JsonPropertyName("browser_download_url")]
        string BrowserDownloadUrl);
}
