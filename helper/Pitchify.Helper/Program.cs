using System.Text.Json;
using System.Text.Json.Serialization;
using Pitchify.Helper;

static string? GetOption(string[] arguments, string optionName)
{
    for (var index = 0; index < arguments.Length - 1; index++)
    {
        if (string.Equals(
                arguments[index],
                optionName,
                StringComparison.OrdinalIgnoreCase))
        {
            return arguments[index + 1];
        }
    }

    return null;
}

var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
var dataDirectory = GetOption(args, "--data-dir")
    ?? Environment.GetEnvironmentVariable("PITCHIFY_DATA_DIR");
if (string.IsNullOrWhiteSpace(dataDirectory))
{
    dataDirectory = Path.Combine(appData, "Pitchify");
}
Directory.CreateDirectory(dataDirectory);

using var singleInstance = new Mutex(
    initiallyOwned: true,
    name: @"Local\Pitchify.Helper.SingleInstance",
    createdNew: out var ownsMutex);
if (!ownsMutex)
{
    return;
}

var logger = new FileLogger(Path.Combine(dataDirectory, "logs", "pitchify.log"));
var configStore = new ConfigStore(Path.Combine(dataDirectory, "config.json"));
using var deviceService = new DeviceService();
using var audioEngine = new AudioEngine(configStore, deviceService, logger);
using var updateService = new UpdateService(
    configStore,
    logger,
    dataDirectory,
    currentVersion: "1.2.0");

var builder = WebApplication.CreateSlimBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:38123");
builder.Logging.ClearProviders();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.Converters.Add(
        new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower));
});

var app = builder.Build();
var token = configStore.Snapshot().ApiToken;

app.Use(async (context, next) =>
{
    var origin = context.Request.Headers.Origin.ToString();
    if (!RequestSecurity.IsAllowedOrigin(origin))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new { error = "Origin is not allowed." });
        return;
    }

    if (!string.IsNullOrEmpty(origin))
    {
        context.Response.Headers.AccessControlAllowOrigin = RequestSecurity.SpotifyOrigin;
        context.Response.Headers.Vary = "Origin";
        context.Response.Headers.AccessControlAllowHeaders =
            "Authorization, Content-Type";
        context.Response.Headers.AccessControlAllowMethods =
            "GET, PUT, POST, OPTIONS";
    }

    if (HttpMethods.IsOptions(context.Request.Method))
    {
        context.Response.StatusCode = StatusCodes.Status204NoContent;
        return;
    }

    if (!RequestSecurity.IsAuthorized(
            context.Request.Headers.Authorization.ToString(),
            token))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = "Invalid Pitchify token." });
        return;
    }

    await next();
});

StatusDto WithUpdate(StatusDto status) =>
    status with { Update = updateService.GetStatus() };

app.MapGet(
    "/v1/status",
    () => Results.Ok(WithUpdate(audioEngine.GetStatus())));

app.MapPut("/v1/pitch", (PitchRequest request) =>
{
    if (!PitchValidator.IsValid(request.Semitones))
    {
        return Results.BadRequest(new
        {
            error =
                $"Semitones must be between {PitchValidator.Minimum} and {PitchValidator.Maximum}.",
        });
    }

    return Results.Ok(WithUpdate(audioEngine.SetSemitones(request.Semitones)));
});

app.MapPut("/v1/output", (OutputRequest request) =>
{
    try
    {
        return Results.Ok(WithUpdate(audioEngine.SetOutputDevice(request.DeviceId)));
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.MapPost(
    "/v1/restart",
    () => Results.Ok(WithUpdate(audioEngine.RestartAndGetStatus())));

app.MapPost("/v1/update/check", async (CancellationToken cancellationToken) =>
    Results.Ok(
        await updateService.CheckForUpdatesAsync(cancellationToken)));

app.MapPost("/v1/update", async (CancellationToken cancellationToken) =>
{
    try
    {
        var update = await updateService.InstallAvailableUpdateAsync(
            cancellationToken);
        return Results.Accepted(value: update);
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.Lifetime.ApplicationStarted.Register(() =>
{
    logger.Info("Pitchify Helper 1.2.0 started on http://127.0.0.1:38123.");
    audioEngine.Start();
    updateService.Start();
});
app.Lifetime.ApplicationStopping.Register(() =>
{
    logger.Info("Pitchify Helper is stopping.");
});

try
{
    await app.RunAsync();
}
catch (Exception exception)
{
    logger.Error("Pitchify Helper terminated unexpectedly.", exception);
    throw;
}
