namespace Pitchify.Helper;

public sealed class FileLogger
{
    private const long MaximumLogBytes = 1_000_000;
    private const int RetainedLogCount = 3;

    private readonly object _gate = new();
    private readonly string _path;

    public FileLogger(string path)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    }

    public void Info(string message) => Write("INFO", message);

    public void Error(string message, Exception? exception = null)
    {
        Write("ERROR", exception is null ? message : $"{message}{Environment.NewLine}{exception}");
    }

    private void Write(string level, string message)
    {
        lock (_gate)
        {
            RotateIfNecessary();
            File.AppendAllText(
                _path,
                $"{DateTimeOffset.Now:O} [{level}] {message}{Environment.NewLine}");
        }
    }

    private void RotateIfNecessary()
    {
        if (!File.Exists(_path) || new FileInfo(_path).Length < MaximumLogBytes)
        {
            return;
        }

        for (var index = RetainedLogCount - 1; index >= 1; index--)
        {
            var source = $"{_path}.{index}";
            var destination = $"{_path}.{index + 1}";
            if (File.Exists(source))
            {
                File.Move(source, destination, overwrite: true);
            }
        }

        File.Move(_path, $"{_path}.1", overwrite: true);
    }
}

