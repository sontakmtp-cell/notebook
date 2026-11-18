using System.IO;
using System.Windows.Media.Imaging;

namespace PromptNotebook.Markdown;

internal sealed class MarkdownImages(Action<string> onLoaded)
{
    private const int CacheSize = 20;
    private readonly Dictionary<string, BitmapSource?> _cache = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _decoders = new(2);
    private string? _directory;
    private int _generation;

    public void Reset(string? documentPath)
    {
        _generation++;
        _directory = documentPath is null ? null : Path.GetDirectoryName(documentPath);
        _cache.Clear();
    }

    public BitmapSource? Get(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
            return null;
        if (_cache.TryGetValue(target, out var existing))
            return existing;

        var path = LocalPath(target);
        if (path is null)
            return null;
        if (_cache.Count == CacheSize)
            _cache.Remove(_cache.Keys.First());
        _cache.Add(target, null);
        _ = DecodeAsync(target, path, _generation);
        return null;
    }

    private string? LocalPath(string target)
    {
        try
        {
            var path = Uri.UnescapeDataString(target);
            if (path.StartsWith(@"\\") || path.StartsWith("//") || path.Contains('\0'))
                return null;

            // Accept only drive-letter absolute paths or paths relative to the current document.
            if (Path.IsPathFullyQualified(path))
            {
                if (path.Length < 3 || !char.IsAsciiLetter(path[0]) || path[1] != ':')
                    return null;
            }
            else
            {
                if (_directory is null || Path.IsPathRooted(path) || path.Contains(':'))
                    return null;
                path = Path.Combine(_directory, path);
            }

            path = Path.GetFullPath(path);
            return path.Length > 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' &&
                !path.AsSpan(2).Contains(':') ? path : null;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UriFormatException or NotSupportedException)
        {
            return null;
        }
    }

    private async Task DecodeAsync(string target, string path, int generation)
    {
        await _decoders.WaitAsync();
        try
        {
            if (generation != _generation || !_cache.ContainsKey(target))
                return;
            var source = await Task.Run(() => Decode(path));
            if (source is not null && generation == _generation && _cache.ContainsKey(target))
            {
                _cache[target] = source;
                onLoaded(target);
            }
        }
        finally
        {
            _decoders.Release();
        }
    }

    private static BitmapSource? Decode(string path)
    {
        try
        {
            var root = Path.GetPathRoot(path)!;
            if (new DriveInfo(root).DriveType is not (DriveType.Fixed or DriveType.Removable or DriveType.CDRom or DriveType.Ram))
                return null;

            // Check each component so a link or junction cannot lead the image loader to a share.
            var current = root;
            foreach (var part in path[root.Length..].Split(Path.DirectorySeparatorChar))
            {
                current = Path.Combine(current, part);
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    return null;
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > 24 * 1024 * 1024)
                return null;
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            if (decoder.Frames.Count == 0)
                return null;
            var frame = decoder.Frames[0];
            if (frame.PixelWidth <= 0 || frame.PixelHeight <= 0 ||
                (long)frame.PixelWidth * frame.PixelHeight > 40_000_000)
                return null;

            stream.Position = 0;
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            if (frame.PixelWidth >= frame.PixelHeight)
                bitmap.DecodePixelWidth = Math.Min(480, frame.PixelWidth);
            else
                bitmap.DecodePixelHeight = Math.Min(280, frame.PixelHeight);
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }
}
