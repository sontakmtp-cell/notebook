using System.IO;
using System.Text.Json;
using PromptNotebook.Models;

namespace PromptNotebook.Services;

internal sealed record RecoveryDraft(Guid Id, string Name, string? OriginalPath, FileFormat Format,
    TextEncoding Encoding, string Text);

internal sealed class RecoveryStore(string root) : IDisposable
{
    private const string LockName = "active.lock";
    private readonly SemaphoreSlim _gate = new(1);
    private string? _session;
    private FileStream? _lock;

    public async Task<IReadOnlyList<RecoveryDraft>> RecoverAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Task.Run(Recover).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private List<RecoveryDraft> Recover()
    {
        var drafts = new List<RecoveryDraft>();
        if (!Directory.Exists(root))
            return drafts;

        foreach (var folder in Directory.GetDirectories(root))
        {
            if (folder == _session || !Guid.TryParseExact(Path.GetFileName(folder), "N", out _))
                continue;

            var previous = TryLock(folder);
            if (previous is null)
                continue;

            using (previous)
            {
                string[] paths;
                try { paths = DraftFiles(folder).ToArray(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
                foreach (var path in paths)
                {
                    RecoveryDraft? draft;
                    try
                    {
                        draft = JsonSerializer.Deserialize<RecoveryDraft>(File.ReadAllText(path));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                    {
                        continue;
                    }

                    if (draft is null || draft.Id.ToString("N") != Path.GetFileNameWithoutExtension(path) ||
                        string.IsNullOrWhiteSpace(draft.Name) || draft.Text is null ||
                        !Enum.IsDefined(draft.Format) || !Enum.IsDefined(draft.Encoding))
                        continue;

                    try
                    {
                        EnsureSession();
                        File.Move(path, DraftPath(draft.Id));
                        drafts.Add(draft);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                    {
                        // Leave an unclaimed draft available for a later recovery attempt.
                    }
                }
            }
            RemoveEmptySession(folder);
        }
        return drafts;
    }

    public async Task KeepAsync(Guid id, string name, string? originalPath, FileFormat format,
        TextEncoding encoding, ICSharpCode.AvalonEdit.Document.ITextSource snapshot)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await Task.Run(async () =>
            {
                EnsureSession();
                var draft = new RecoveryDraft(id, name, originalPath, format, encoding, snapshot.Text);
                await TextFiles.WriteAsync(DraftPath(id), JsonSerializer.Serialize(draft), TextEncoding.Utf8)
                    .ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task DiscardAsync(Guid id)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_session is not null)
                await Task.Run(() => File.Delete(DraftPath(id))).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task FinishAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_session is null)
                return;
            var folder = _session;
            await Task.Run(() =>
            {
                foreach (var draft in DraftFiles(folder))
                    File.Delete(draft);
                Dispose();
                RemoveEmptySession(folder);
            }).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private static FileStream? TryLock(string folder)
    {
        try
        {
            if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0)
                return null;
            return File.Open(Path.Combine(folder, LockName), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void EnsureSession()
    {
        if (_session is not null)
            return;
        var session = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(session);
        _lock = File.Open(Path.Combine(session, LockName), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        _session = session;
    }

    private string DraftPath(Guid id) => Path.Combine(_session!, $"{id:N}.json");

    private static IEnumerable<string> DraftFiles(string folder) =>
        Directory.EnumerateFiles(folder, "*.json").Where(path =>
            Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out _) &&
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0);

    private static void RemoveEmptySession(string folder)
    {
        try
        {
            var lockPath = Path.Combine(folder, LockName);
            if (Directory.EnumerateFileSystemEntries(folder).All(path => path == lockPath))
            {
                File.Delete(lockPath);
                Directory.Delete(folder);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A different process may be inspecting this folder. It can retry next launch.
        }
    }

    public void Dispose()
    {
        _lock?.Dispose();
        _lock = null;
        _session = null;
    }
}
