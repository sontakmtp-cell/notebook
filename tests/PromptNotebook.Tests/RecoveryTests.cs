using System.IO;
using ICSharpCode.AvalonEdit.Document;
using PromptNotebook.Models;
using PromptNotebook.Services;

internal static class RecoveryTests
{
    public static async Task RunAsync()
    {
        var directory = Directory.CreateTempSubdirectory("notebook-recovery-test-");
        try
        {
            var root = Path.Combine(directory.FullName, "Recovery");
            var id = Guid.NewGuid();
            var deletedId = Guid.NewGuid();
            const string snapshotText = "# Tiếng Việt\r\n\nBản nháp 😀";
            const string editedText = "# Sửa lần nữa\r\n";
            var original = Path.Combine(directory.FullName, "note.md");
            await TextFiles.WriteAsync(original, "Nội dung đã lưu", TextEncoding.Utf8);
            using (var active = new RecoveryStore(root))
            {
                await active.KeepAsync(id, "note.md", original, FileFormat.Markdown,
                    TextEncoding.Utf8, new TextDocument(snapshotText).CreateSnapshot());
                await active.KeepAsync(deletedId, "deleted.txt", null, FileFormat.Txt,
                    TextEncoding.Utf8, new TextDocument("Discard").CreateSnapshot());
                await active.DiscardAsync(deletedId);

                using var otherInstance = new RecoveryStore(root);
                Require((await otherInstance.RecoverAsync()).Count == 0, "A running instance owns its drafts");
                // Releasing the lock without FinishAsync simulates process termination.
            }

            using (var firstRestart = new RecoveryStore(root))
            {
                var draft = (await firstRestart.RecoverAsync()).Single();
                Require(draft == new RecoveryDraft(id, "note.md", original, FileFormat.Markdown, TextEncoding.Utf8, snapshotText),
                    "Recovery restores the full Markdown source, encoding and original location");
                Require((await TextFiles.ReadAsync(original)).Text == "Nội dung đã lưu", "Autosave leaves original bytes intact");
                await firstRestart.KeepAsync(id, draft.Name, draft.OriginalPath, draft.Format, draft.Encoding,
                    new TextDocument(editedText).CreateSnapshot());
            }

            using (var secondRestart = new RecoveryStore(root))
            {
                var draft = (await secondRestart.RecoverAsync()).Single();
                Require(draft.Id == id && draft.Text == editedText, "A newer draft replaces its earlier snapshot");
                await secondRestart.FinishAsync();
            }
            Require(Directory.GetFileSystemEntries(root).Length == 0, "Clean shutdown removes adopted drafts");

            var stamp = FileStamp.Read(original);
            await TextFiles.WriteAsync(original, "Thay đổi từ ứng dụng khác", TextEncoding.Utf8);
            try
            {
                await TextFiles.WriteCheckedAsync(original, "Notebook stale edit", TextEncoding.Utf8, stamp);
                throw new InvalidOperationException("Concurrent file modification was overwritten");
            }
            catch (IOException)
            {
                Require((await TextFiles.ReadAsync(original)).Text == "Thay đổi từ ứng dụng khác",
                    "Save protects concurrent external changes");
            }
            Console.WriteLine("[PASS] Crash recovery, instance isolation, draft cleanup and guarded file save");
        }
        finally { directory.Delete(recursive: true); }
    }

    private static void Require(bool condition, string label)
    {
        if (!condition)
            throw new InvalidOperationException(label);
    }
}
