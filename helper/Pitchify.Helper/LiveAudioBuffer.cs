using NAudio.Wave;

namespace Pitchify.Helper;

/// <summary>
/// A bounded live-audio queue that always keeps the newest captured samples.
/// NAudio's BufferedWaveProvider drops newly arriving data when full, which is
/// the wrong behavior for a live monitor because it replays stale audio.
/// </summary>
public sealed class LiveAudioBuffer : IWaveProvider
{
    private readonly object _gate = new();
    private readonly byte[] _buffer;
    private int _readPosition;
    private int _writePosition;
    private int _count;
    private long _discardedBytes;

    public LiveAudioBuffer(WaveFormat waveFormat, TimeSpan capacity)
    {
        WaveFormat = waveFormat ?? throw new ArgumentNullException(nameof(waveFormat));
        var requestedBytes = (int)Math.Ceiling(
            waveFormat.AverageBytesPerSecond * capacity.TotalSeconds);
        var alignedBytes = requestedBytes - (requestedBytes % waveFormat.BlockAlign);
        _buffer = new byte[Math.Max(alignedBytes, waveFormat.BlockAlign)];
    }

    public WaveFormat WaveFormat { get; }

    public int CapacityBytes => _buffer.Length;

    public int BufferedBytes
    {
        get
        {
            lock (_gate)
            {
                return _count;
            }
        }
    }

    public TimeSpan BufferedDuration =>
        TimeSpan.FromSeconds(
            BufferedBytes / (double)WaveFormat.AverageBytesPerSecond);

    public long DiscardedBytes
    {
        get
        {
            lock (_gate)
            {
                return _discardedBytes;
            }
        }
    }

    public void AddSamples(byte[] source, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (offset + count > source.Length)
        {
            throw new ArgumentException("The source range exceeds the buffer.");
        }

        count -= count % WaveFormat.BlockAlign;
        if (count == 0)
        {
            return;
        }

        lock (_gate)
        {
            if (count >= _buffer.Length)
            {
                var bytesSkipped = count - _buffer.Length;
                bytesSkipped -= bytesSkipped % WaveFormat.BlockAlign;
                offset += bytesSkipped;
                count = _buffer.Length;
                _discardedBytes += _count + bytesSkipped;
                _readPosition = 0;
                _writePosition = 0;
                _count = 0;
            }

            var overflow = _count + count - _buffer.Length;
            if (overflow > 0)
            {
                overflow += WaveFormat.BlockAlign - 1;
                overflow -= overflow % WaveFormat.BlockAlign;
                overflow = Math.Min(overflow, _count);
                AdvanceReadLocked(overflow);
                _discardedBytes += overflow;
            }

            var firstCopy = Math.Min(count, _buffer.Length - _writePosition);
            Buffer.BlockCopy(source, offset, _buffer, _writePosition, firstCopy);
            var remaining = count - firstCopy;
            if (remaining > 0)
            {
                Buffer.BlockCopy(source, offset + firstCopy, _buffer, 0, remaining);
            }

            _writePosition = (_writePosition + count) % _buffer.Length;
            _count += count;
        }
    }

    public int Read(byte[] destination, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (offset + count > destination.Length)
        {
            throw new ArgumentException("The destination range exceeds the buffer.");
        }

        lock (_gate)
        {
            var bytesToRead = Math.Min(count, _count);
            bytesToRead -= bytesToRead % WaveFormat.BlockAlign;
            if (bytesToRead == 0)
            {
                return 0;
            }

            var firstCopy = Math.Min(bytesToRead, _buffer.Length - _readPosition);
            Buffer.BlockCopy(_buffer, _readPosition, destination, offset, firstCopy);
            var remaining = bytesToRead - firstCopy;
            if (remaining > 0)
            {
                Buffer.BlockCopy(_buffer, 0, destination, offset + firstCopy, remaining);
            }

            AdvanceReadLocked(bytesToRead);
            return bytesToRead;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _readPosition = 0;
            _writePosition = 0;
            _count = 0;
        }
    }

    private void AdvanceReadLocked(int count)
    {
        _readPosition = (_readPosition + count) % _buffer.Length;
        _count -= count;
    }
}
