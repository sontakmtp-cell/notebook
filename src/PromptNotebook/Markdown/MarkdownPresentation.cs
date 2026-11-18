using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.TextFormatting;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using PromptNotebook.Models;

namespace PromptNotebook.Markdown;

public sealed class MarkdownPresentation : IDisposable
{
    private readonly TextEditor _editor;
    private readonly TextView _view;
    private readonly DispatcherTimer _parseTimer;
    private readonly MarkdownColorizer _colorizer;
    private readonly MarkdownImages _images;
    private EditorTab? _tab;
    private string? _filePath;
    private MarkdownLayout? _layout;
    private ITextSourceVersion? _layoutVersion;
    private DocumentLine? _editingLine;
    private bool _caretQueued;
    private bool _parsing;
    private bool _pending;
    private bool _disposed;

    public MarkdownPresentation(TextEditor editor)
    {
        _editor = editor;
        _view = editor.TextArea.TextView;
        _images = new MarkdownImages(RefreshImages);
        _colorizer = new MarkdownColorizer(this);
        _view.ElementGenerators.Add(new ProjectionGenerator(this));
        _view.LineTransformers.Add(_colorizer);
        _view.BackgroundRenderers.Add(new MarkdownBackground(this));
        _parseTimer = new DispatcherTimer(DispatcherPriority.Background, editor.Dispatcher);
        _parseTimer.Tick += ParseTick;
        _editor.TextArea.Caret.PositionChanged += CaretChanged;
        _editor.TextChanged += TextChanged;
    }

    public Brush Brush(string resource) => (Brush)_editor.FindResource(resource);

    public void Apply(EditorTab? tab, bool dark)
    {
        _colorizer.SetTheme(dark);
        if (ReferenceEquals(tab, _tab))
        {
            if (_filePath != (tab?.FilePath ?? tab?.RecoveredFrom))
            {
                _filePath = tab?.FilePath ?? tab?.RecoveredFrom;
                _images.Reset(_filePath);
            }

            if (tab is not null)
                _view.Redraw();
            return;
        }

        _tab = tab;
        _filePath = tab?.FilePath ?? tab?.RecoveredFrom;
        _images.Reset(_filePath);
        _layout = null;
        _layoutVersion = null;
        _colorizer.ResetFences();
        _editingLine = tab is null ? null : tab.Document.GetLineByOffset(_editor.CaretOffset);
        _parseTimer.Stop();
        _pending = false;
        if (tab is not null)
            ScheduleParse(1);
        _view.Redraw();
    }

    public MarkdownLine? LayoutFor(DocumentLine line)
    {
        if (_tab is null || !ReferenceEquals(_view.Document, _tab.Document) ||
            _layout?.GetLine(line.LineNumber) is not { } style || line.Length != style.Source.Length)
            return null;

        // Use the previous presentation only for lines whose source is still an exact match.
        if (!ReferenceEquals(_layoutVersion, _tab.Document.Version) && _tab.Document.GetText(line) != style.Source)
            return null;
        return style;
    }

    public bool IsEditing(DocumentLine line) => ReferenceEquals(line, _editingLine);

    public string? CodeAtCaret() => _editingLine is { IsDeleted: false } line ? LayoutFor(line)?.Fence?.Code : null;

    public string? LinkAtOffset(int offset)
    {
        if (_tab is null)
            return null;
        var line = _tab.Document.GetLineByOffset(offset);
        var column = offset - line.Offset;
        return LayoutFor(line)?.Links.FirstOrDefault(link => column >= link.Start && column <= link.End).Address;
    }

    private void TextChanged(object? sender, EventArgs e)
    {
        if (_tab is not null && ReferenceEquals(_editor.Document, _tab.Document))
            ScheduleParse(165);
    }

    private void ScheduleParse(int delayMs)
    {
        _pending = true;
        _parseTimer.Stop();
        _parseTimer.Interval = TimeSpan.FromMilliseconds(delayMs);
        _parseTimer.Start();
    }

    private async void ParseTick(object? sender, EventArgs e)
    {
        _parseTimer.Stop();
        if (_disposed || _parsing || _tab is not { } tab || !_pending)
            return;

        _pending = false;
        _parsing = true;
        var snapshot = tab.Document.CreateSnapshot();
        try
        {
            var layout = await Task.Run(() => MarkdownLayout.Parse(snapshot.Text));
            if (!_disposed && ReferenceEquals(tab, _tab) && ReferenceEquals(snapshot.Version, tab.Document.Version))
                Publish(layout, snapshot.Version!);
        }
        finally
        {
            _parsing = false;
            if (_pending && !_parseTimer.IsEnabled && !_disposed)
                ScheduleParse(1);
        }
    }

    private void Publish(MarkdownLayout layout, ITextSourceVersion version)
    {
        var changed = new List<VisualLine>();
        foreach (var visual in VisibleLines())
        {
            var line = visual.FirstDocumentLine;
            var previous = LayoutFor(line);
            var next = layout.GetLine(line.LineNumber);
            if (next is null ? previous is not null : !next.HasSamePresentation(previous))
                changed.Add(visual);
        }

        _layout = layout;
        _layoutVersion = version;
        _colorizer.ResetFences();
        foreach (var line in changed)
            _view.Redraw(line);
        _view.InvalidateLayer(KnownLayer.Background);
    }

    private IEnumerable<VisualLine> VisibleLines() =>
        _view.VisualLinesValid ? _view.VisualLines : Array.Empty<VisualLine>();

    private void CaretChanged(object? sender, EventArgs e)
    {
        if (_caretQueued || _tab is null || _disposed)
            return;

        _caretQueued = true;
        _editor.Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            _caretQueued = false;
            if (_disposed || _tab is not { } tab || !ReferenceEquals(tab.Document, _editor.Document))
                return;
            var next = tab.Document.GetLineByOffset(_editor.CaretOffset);
            var previous = _editingLine;
            if (ReferenceEquals(next, previous))
                return;
            _editingLine = next;
            RedrawMarkup(previous);
            RedrawMarkup(next);
        });
    }

    private void RedrawMarkup(DocumentLine? line)
    {
        if (line is not { IsDeleted: false } || LayoutFor(line)?.Projections.Count is not > 0)
            return;
        var visual = _view.GetVisualLine(line.LineNumber);
        if (visual is not null)
            _view.Redraw(visual);
    }

    private void RefreshImages(string target)
    {
        if (_disposed)
            return;
        var redraw = VisibleLines().Where(visual =>
            LayoutFor(visual.FirstDocumentLine)?.Projections.Any(
                projection => projection.Kind == ProjectionKind.Image && projection.Target == target) == true).ToArray();
        foreach (var visual in redraw)
            _view.Redraw(visual);
    }

    private VisualLineElement ProjectImage(Projection projection)
    {
        if (_images.Get(projection.Target) is not BitmapSource source)
            return new FormattedTextElement($"▧ {(projection.Text.Length == 0 ? "Ảnh" : projection.Text)}", projection.Length);

        var scale = Math.Min(1, Math.Min(480d / source.PixelWidth, 280d / source.PixelHeight));
        var image = new Image
        {
            Source = source,
            Width = source.PixelWidth * scale,
            Height = source.PixelHeight * scale,
            Stretch = Stretch.Uniform,
            Margin = new Thickness(1, 3, 1, 3),
            IsHitTestVisible = false
        };
        System.Windows.Automation.AutomationProperties.SetName(image, projection.Text);
        return new InlineObjectElement(projection.Length, image);
    }

    private static VisualLineElement CopyButton(Projection projection, string code)
    {
        var label = projection.Text.Length == 0 ? "Sao chép code" : $"{projection.Text}  ·  Sao chép code";
        var button = new Button
        {
            Content = label, FontFamily = new FontFamily("Segoe UI"), FontSize = 12,
            MinHeight = 22, Padding = new Thickness(6, 1, 6, 1),
            ToolTip = "Sao chép khối code (Ctrl+Shift+C)", Focusable = false,
            IsEnabled = code.Length > 0
        };
        button.SetResourceReference(Control.ForegroundProperty, "AccentBrush");
        button.Click += (_, _) =>
        {
            if (CopyText(code))
                button.Content = "Đã sao chép";
        };
        return new InlineObjectElement(projection.Length, button);
    }

    public static bool CopyText(string code)
    {
        if (code.Length == 0)
            return false;
        try
        {
            Clipboard.SetText(code);
            return true;
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _parseTimer.Stop();
        _parseTimer.Tick -= ParseTick;
        _editor.TextChanged -= TextChanged;
        _editor.TextArea.Caret.PositionChanged -= CaretChanged;
        _colorizer.ResetFences();
        _images.Reset(null);
    }

    private sealed class HiddenElement(int length) : VisualLineElement(1, length)
    {
        public override TextRun CreateTextRun(int startVisualColumn, ITextRunConstructionContext context) =>
            new TextHidden(1);
    }

    private sealed class ProjectionGenerator(MarkdownPresentation presenter) : VisualLineElementGenerator
    {
        private int _offset;
        private int _next;
        private MarkdownLine? _layout;

        public override void StartGeneration(ITextRunConstructionContext context)
        {
            base.StartGeneration(context);
            var line = context.VisualLine.FirstDocumentLine;
            _offset = line.Offset;
            _next = 0;
            _layout = presenter.IsEditing(line) ? null : presenter.LayoutFor(line);
        }

        public override int GetFirstInterestedOffset(int startOffset)
        {
            if (_layout is null)
                return -1;
            var projections = _layout.Projections;
            while (_next < projections.Count && _offset + projections[_next].Start < startOffset)
                _next++;
            return _next < projections.Count ? _offset + projections[_next].Start : -1;
        }

        public override VisualLineElement ConstructElement(int offset)
        {
            var projection = _layout!.Projections[_next++];
            return projection.Kind switch
            {
                ProjectionKind.Hidden => new HiddenElement(projection.Length),
                ProjectionKind.Image => presenter.ProjectImage(projection),
                ProjectionKind.CopyCode => CopyButton(projection, _layout.Fence!.Code),
                _ => new FormattedTextElement(projection.Text, projection.Length)
                {
                    BreakBefore = LineBreakCondition.BreakRestrained
                }
            };
        }
    }

    private sealed class MarkdownBackground(MarkdownPresentation presenter) : IBackgroundRenderer
    {
        public KnownLayer Layer => KnownLayer.Background;

        public void Draw(TextView view, DrawingContext drawingContext)
        {
            var codeBrush = presenter.Brush("MarkdownCodeBrush");
            var quoteBrush = presenter.Brush("MarkdownQuoteBrush");
            var header = presenter.Brush("MarkdownTableBrush");
            var border = presenter.Brush("BorderSubtleBrush");
            foreach (var visual in presenter.VisibleLines())
            {
                var line = visual.FirstDocumentLine;
                if (presenter.LayoutFor(line) is not { } layout)
                    continue;

                var y = visual.VisualTop - view.ScrollOffset.Y;
                var width = view.ActualWidth;
                var height = visual.Height;
                var fill = layout.Fence is not null ? codeBrush : layout.IsTableHeader ? header :
                    layout.QuoteDepth > 0 ? quoteBrush : null;
                if (fill is not null)
                    drawingContext.DrawRectangle(fill, null, new Rect(0, y, width, height));

                if ((layout.IsRule || layout.IsTableSeparator) && !presenter.IsEditing(line) && width > 24)
                    drawingContext.DrawRectangle(border, null, new Rect(5, y + height / 2, width - 24, 1));
            }
        }
    }
}
