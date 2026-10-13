using Android.Graphics;
using Android.Graphics.Pdf;
using Android.Text;
using Android.Text.Style;
using FalconNotes.Core.Attachments;
using FalconNotes.Core.Pdf;
using FalconNotes.Core.Platform;
using Color = Android.Graphics.Color;
using Paint = Android.Graphics.Paint;
using Path = Android.Graphics.Path;
using RectF = Android.Graphics.RectF;
using Stream = System.IO.Stream;

namespace FalconNotes.App.Services;

/// <summary>
/// <see cref="IPdfWriter"/> on Android's own <c>PdfDocument</c> and text layout, with no third-party code (docs/02,
/// Dependencies; docs/12, PDF). <c>StaticLayout</c> breaks and shapes the lines, so Sinhala, Tamil, Arabic and emoji
/// come out as the device shows them, and the PDF holds text, not a picture of it. Latin text copies out of the PDF
/// exactly; scripts whose letters join into combined shapes look right but lose some signs when copied, which is how
/// <c>PdfDocument</c> records them.
/// </summary>
/// <remarks>
/// The page is A4, measured in PostScript points (1/72 inch), the unit <c>PdfDocument</c> draws in: text sizes below
/// are points, not pixels. A block that does not fit goes on between the lines, never through one; a table row and a
/// picture move to the next page whole.
/// </remarks>
/// <param name="media">Opens the note's pictures.</param>
public sealed class AndroidPdfWriter(IMediaSource media) : IPdfWriter
{
    private const int PageWidth = 595;
    private const int PageHeight = 842;
    private const int Margin = 54;
    private const int ContentWidth = PageWidth - (2 * Margin);
    private const int ContentHeight = PageHeight - (2 * Margin);
    private const int IndentStep = 18;
    private const int QuoteStep = 12;
    private const float CodePadding = 6;
    private const float CellPadding = 4;

    /// <summary>
    /// A picture is drawn from at most this many pixels on its longest side: 150 to the inch across the page. A PDF
    /// from <c>PdfDocument</c> holds pictures without JPEG compression, so a full-size photo would add tens of megabytes.
    /// </summary>
    private const int MaxPictureSide = 1240;

    /// <summary>The decoders take the whole file from memory, as in <see cref="AndroidImageCodec"/>.</summary>
    private const long MaxPictureBytes = 64L * 1024 * 1024;

    /// <summary>A picture's pixel is shown this many points wide, 96 to the inch as on a screen, unless the page is narrower.</summary>
    private const float PointsPerPixel = 0.75f;

    // The stone colours of the app's light theme (docs/06): a PDF is black on white whatever the theme.
    private static readonly Color Ink = Color.Rgb(0x1c, 0x19, 0x17);
    private static readonly Color QuoteInk = Color.Rgb(0x57, 0x53, 0x4e);
    private static readonly Color Grey = Color.Rgb(0x78, 0x71, 0x6c);
    private static readonly Color Line = Color.Rgb(0xd6, 0xd3, 0xd1);
    private static readonly Color Tint = Color.Rgb(0xf5, 0xf5, 0xf4);
    private static readonly Color LinkInk = Color.Rgb(0x1d, 0x4e, 0xd8);

    /// <inheritdoc />
    public Task WriteAsync(PdfNote note, Stream output, CancellationToken cancellationToken = default) =>
        Task.Run(() => Write(note, output, cancellationToken), cancellationToken);

    private async Task Write(PdfNote note, Stream output, CancellationToken cancellationToken)
    {
        using var pages = new Pages();
        PdfBlock? previous = null;
        foreach (var block in note.Blocks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (block)
            {
                case PdfText text:
                    DrawText(pages, text, previous);
                    break;
                case PdfRule:
                    DrawRule(pages);
                    break;
                case PdfTable table:
                    DrawTable(pages, table);
                    break;
                case PdfPicture picture:
                    await DrawPictureAsync(pages, picture, cancellationToken);
                    break;
            }

            previous = block;
        }

        cancellationToken.ThrowIfCancellationRequested();
        pages.WriteTo(output);
    }

    private static void DrawText(Pages pages, PdfText block, PdfBlock? previous)
    {
        var left = Margin + (block.Quote * QuoteStep) + (block.Indent * IndentStep);
        var code = block.Style == PdfTextStyle.Code;
        var inset = code ? CodePadding : 0;
        var width = Math.Max(IndentStep, PageWidth - Margin - left - (int)(2 * inset));
        using var paint = TextPaintFor(block.Style, block.Quote > 0);
        using var styled = Styled(block.Spans);
        using var layout = Wrap(styled, paint, width);

        var gap = pages.AtTop ? 0 : SpaceBefore(block, previous);

        // A heading is kept with what it heads: it is not left alone at the foot of a page.
        var needed = layout.Height + (IsHeading(block.Style) ? 3 * paint.TextSize : 0);
        if (!pages.AtTop && gap + Math.Min(needed, ContentHeight) > pages.Remaining && IsHeading(block.Style))
        {
            pages.NewPage();
            gap = 0;
        }

        var joinsQuote = gap > 0 && previous is PdfText before ? Math.Min(before.Quote, block.Quote) : 0;
        pages.Y += gap;

        var line = 0;
        var first = true;
        do
        {
            // The lines that still fit on this page; at the top of a page one line is drawn even if it is too tall.
            var top = line < layout.LineCount ? layout.GetLineTop(line) : 0;
            var end = line;
            while (end < layout.LineCount && layout.GetLineBottom(end) - top + (2 * inset) <= pages.Remaining)
            {
                end++;
            }

            if (end == line && layout.LineCount > 0)
            {
                if (!pages.AtTop)
                {
                    pages.NewPage();
                    joinsQuote = 0; // the quote's bar starts again with the page
                    continue;
                }

                end = line + 1;
            }

            var height = layout.LineCount == 0 ? paint.TextSize * 1.3f : layout.GetLineBottom(end - 1) - top;
            var canvas = pages.Canvas;
            var y = pages.Y;

            if (code)
            {
                using var tint = Fill(Tint);
                canvas.DrawRect(left, y, PageWidth - Margin, y + height + (2 * inset), tint);
            }

            using (var bar = Fill(Line))
            {
                for (var depth = 0; depth < block.Quote; depth++)
                {
                    var x = Margin + (depth * QuoteStep);
                    var from = first && depth < joinsQuote ? y - gap : y;
                    canvas.DrawRect(x, from, x + 2.5f, y + height + (2 * inset), bar);
                }
            }

            if (first && layout.LineCount > 0)
            {
                DrawMarker(canvas, block, layout, paint, left, y + inset);
            }

            if (layout.LineCount > 0)
            {
                canvas.Save();
                canvas.ClipRect(left + inset, y + inset, left + inset + width, y + inset + height);
                canvas.Translate(left + inset, y + inset - top);
                layout.Draw(canvas);
                canvas.Restore();
            }

            pages.Y = y + height + (2 * inset);
            line = end;
            first = false;
            if (line < layout.LineCount)
            {
                pages.NewPage();
            }
        }
        while (line < layout.LineCount);
    }

    /// <summary>A list item's bullet or number, or a task item's box, in the gutter to the left of its first line.</summary>
    private static void DrawMarker(Canvas canvas, PdfText block, StaticLayout layout, TextPaint paint, float left, float y)
    {
        if (block.Checked is { } ticked)
        {
            var size = paint.TextSize * 0.8f;
            var middle = y + layout.GetLineBaseline(0) - (paint.TextSize * 0.36f);
            var box = new RectF(left - 6 - size, middle - (size / 2), left - 6, middle + (size / 2));
            using var stroke = new Paint(PaintFlags.AntiAlias) { Color = Grey, StrokeWidth = 0.9f };
            stroke.SetStyle(Paint.Style.Stroke);
            canvas.DrawRoundRect(box, 1.5f, 1.5f, stroke);
            if (ticked)
            {
                stroke.Color = Ink;
                stroke.StrokeWidth = 1.3f;
                stroke.StrokeCap = Paint.Cap.Round;
                stroke.StrokeJoin = Paint.Join.Round;
                using var tick = new Path();
                tick.MoveTo(box.Left + (size * 0.2f), box.Top + (size * 0.52f));
                tick.LineTo(box.Left + (size * 0.42f), box.Top + (size * 0.74f));
                tick.LineTo(box.Left + (size * 0.8f), box.Top + (size * 0.26f));
                canvas.DrawPath(tick, stroke);
            }
        }
        else if (block.Marker is { } marker)
        {
            using var markerPaint = new TextPaint(paint) { TextAlign = Paint.Align.Right };
            markerPaint.SetTypeface(Typeface.Default);
            markerPaint.TextSize = TextSize(PdfTextStyle.Body);
            canvas.DrawText(marker, left - 6, y + layout.GetLineBaseline(0), markerPaint);
        }
    }

    private static void DrawRule(Pages pages)
    {
        const float Space = 12;
        if (!pages.AtTop && Space + 1 > pages.Remaining)
        {
            pages.NewPage();
        }

        pages.Y += pages.AtTop ? 0 : Space;
        using var paint = Fill(Line);
        pages.Canvas.DrawRect(Margin, pages.Y, PageWidth - Margin, pages.Y + 0.75f, paint);
        pages.Y += 0.75f + 3;
    }

    private static void DrawTable(Pages pages, PdfTable table)
    {
        var columns = table.Rows.Count == 0 ? 0 : table.Rows.Max(row => row.Count);
        if (columns == 0)
        {
            return;
        }

        var cellWidth = (float)ContentWidth / columns;
        var textWidth = Math.Max(1, (int)(cellWidth - (2 * CellPadding)));
        using var paint = TextPaintFor(PdfTextStyle.Body, quoted: false);
        paint.TextSize = 10;
        using var border = new Paint(PaintFlags.AntiAlias) { Color = Line, StrokeWidth = 0.6f };
        border.SetStyle(Paint.Style.Stroke);
        using var tint = Fill(Tint);

        pages.Y += pages.AtTop ? 0 : 10;
        for (var r = 0; r < table.Rows.Count; r++)
        {
            var header = r == 0;
            var cells = new List<(SpannableStringBuilder Text, StaticLayout Layout)>();
            try
            {
                foreach (var cell in table.Rows[r])
                {
                    var styled = Styled(cell, bold: header);
                    cells.Add((styled, Wrap(styled, paint, textWidth)));
                }

                var height = cells.Max(cell => cell.Layout.Height) + (2 * CellPadding);
                if (!pages.AtTop && height > pages.Remaining)
                {
                    pages.NewPage();
                }

                var canvas = pages.Canvas;
                var y = pages.Y;
                if (header)
                {
                    canvas.DrawRect(Margin, y, PageWidth - Margin, y + height, tint);
                }

                for (var c = 0; c < columns; c++)
                {
                    var x = Margin + (c * cellWidth);
                    canvas.DrawRect(x, y, x + cellWidth, y + height, border);
                    if (c < cells.Count)
                    {
                        canvas.Save();
                        canvas.ClipRect(x, y, x + cellWidth, Math.Min(y + height, PageHeight - Margin));
                        canvas.Translate(x + CellPadding, y + CellPadding);
                        cells[c].Layout.Draw(canvas);
                        canvas.Restore();
                    }
                }

                pages.Y = y + height;
            }
            finally
            {
                foreach (var (text, layout) in cells)
                {
                    layout.Dispose();
                    text.Dispose();
                }
            }
        }
    }

    private async Task DrawPictureAsync(Pages pages, PdfPicture picture, CancellationToken cancellationToken)
    {
        var bitmap = await DecodeAsync(picture.AttachmentId, cancellationToken);
        if (bitmap is null)
        {
            // A picture this device cannot decode is named, so the page does not pass over it in silence.
            DrawText(pages, new PdfText(PdfTextStyle.Caption, [new PdfSpan($"Picture not shown: {picture.FileName}")]), null);
            return;
        }

        pages.Keep(bitmap);
        var scale = Math.Min(PointsPerPixel, Math.Min((float)ContentWidth / bitmap.Width, (float)ContentHeight / bitmap.Height));
        var width = bitmap.Width * scale;
        var height = bitmap.Height * scale;
        var gap = pages.AtTop ? 0 : 10;
        if (!pages.AtTop && gap + height > pages.Remaining)
        {
            pages.NewPage();
            gap = 0;
        }

        pages.Y += gap;
        using var paint = new Paint(PaintFlags.AntiAlias | PaintFlags.FilterBitmap);
        pages.Canvas.DrawBitmap(bitmap, null, new RectF(Margin, pages.Y, Margin + width, pages.Y + height), paint);
        pages.Y += height;
    }

    private async Task<Bitmap?> DecodeAsync(Guid attachmentId, CancellationToken cancellationToken)
    {
        try
        {
            if (await media.OpenAsync(attachmentId) is not { } file)
            {
                return null;
            }

            await using var content = file.Content;
            if (content.Length is <= 0 or > MaxPictureBytes)
            {
                return null;
            }

            var bytes = new byte[content.Length];
            await content.ReadExactlyAsync(bytes, cancellationToken);
            return AndroidImageCodec.DecodeUpright(
                bytes, (w, h) => PhotoShrinker.FitWithin(w, h, MaxPictureSide), firstFrameOfAnimations: true);
        }
        catch (Exception e) when (e is IOException or Java.Lang.Exception)
        {
            return null;
        }
    }

    private static float SpaceBefore(PdfText block, PdfBlock? previous) => block.Style switch
    {
        PdfTextStyle.Heading1 => 18,
        PdfTextStyle.Heading2 => 16,
        PdfTextStyle.Heading3 or PdfTextStyle.Heading4 => 13,
        PdfTextStyle.Caption => 6,
        PdfTextStyle.Code => 9,

        // The items of one list stand closer together than paragraphs do; the first one keeps a paragraph's distance.
        _ => previous is PdfText { Indent: > 0 } && (block.Marker is not null || block.Checked is not null) ? 4 : 9,
    };

    private static bool IsHeading(PdfTextStyle style) =>
        style is PdfTextStyle.Heading1 or PdfTextStyle.Heading2 or PdfTextStyle.Heading3 or PdfTextStyle.Heading4;

    private static float TextSize(PdfTextStyle style) => style switch
    {
        PdfTextStyle.Heading1 => 20,
        PdfTextStyle.Heading2 => 16,
        PdfTextStyle.Heading3 => 13.5f,
        PdfTextStyle.Heading4 => 12,
        PdfTextStyle.Code => 9.5f,
        PdfTextStyle.Caption => 9,
        _ => 11,
    };

    private static TextPaint TextPaintFor(PdfTextStyle style, bool quoted)
    {
        // Linear, subpixel text: the sizes are small numbers of points, and rounding each glyph to a whole one would
        // space the letters badly.
        var paint = new TextPaint(PaintFlags.AntiAlias | PaintFlags.SubpixelText | PaintFlags.LinearText)
        {
            TextSize = TextSize(style),
            Color = style == PdfTextStyle.Caption ? Grey : quoted ? QuoteInk : Ink,
        };
        paint.SetTypeface(
            style == PdfTextStyle.Code ? Typeface.Monospace
            : IsHeading(style) ? Typeface.Create(Typeface.SansSerif, TypefaceStyle.Bold)
            : Typeface.SansSerif);
        return paint;
    }

    private static Paint Fill(Color color)
    {
        var paint = new Paint(PaintFlags.AntiAlias) { Color = color };
        paint.SetStyle(Paint.Style.Fill);
        return paint;
    }

    private static StaticLayout Wrap(SpannableStringBuilder text, TextPaint paint, int width) =>
        StaticLayout.Builder.Obtain(text, 0, text.Length(), paint, width)
            .SetAlignment(Android.Text.Layout.Alignment.AlignNormal!)
            .SetLineSpacing(0, 1.3f)
            .SetIncludePad(false)
            .Build();

    private static SpannableStringBuilder Styled(IReadOnlyList<PdfSpan> spans, bool bold = false)
    {
        var text = new SpannableStringBuilder();
        foreach (var span in spans)
        {
            var start = text.Length();
            text.Append(span.Text);
            var end = text.Length();
            if (span.Bold || bold)
            {
                text.SetSpan(new StyleSpan(TypefaceStyle.Bold), start, end, SpanTypes.ExclusiveExclusive);
            }

            if (span.Italic)
            {
                text.SetSpan(new StyleSpan(TypefaceStyle.Italic), start, end, SpanTypes.ExclusiveExclusive);
            }

            if (span.Strikethrough)
            {
                text.SetSpan(new StrikethroughSpan(), start, end, SpanTypes.ExclusiveExclusive);
            }

            if (span.Code)
            {
                text.SetSpan(new TypefaceSpan("monospace"), start, end, SpanTypes.ExclusiveExclusive);
                text.SetSpan(new RelativeSizeSpan(0.92f), start, end, SpanTypes.ExclusiveExclusive);
                text.SetSpan(new BackgroundColorSpan(Tint), start, end, SpanTypes.ExclusiveExclusive);
            }

            if (span.Link)
            {
                text.SetSpan(new ForegroundColorSpan(LinkInk), start, end, SpanTypes.ExclusiveExclusive);
                text.SetSpan(new UnderlineSpan(), start, end, SpanTypes.ExclusiveExclusive);
            }
        }

        return text;
    }

    /// <summary>The document and the page being drawn: where the next block goes, and when a new page starts.</summary>
    private sealed class Pages : IDisposable
    {
        private readonly PdfDocument _document = new();
        private readonly List<Bitmap> _pictures = [];
        private PdfDocument.Page? _page;
        private int _number;

        /// <summary>Where the next block starts, from the top of the page.</summary>
        public float Y { get; set; } = Margin;

        /// <summary>Whether nothing has been drawn on this page yet.</summary>
        public bool AtTop => Y <= Margin;

        /// <summary>The room left above the bottom margin.</summary>
        public float Remaining => PageHeight - Margin - Y;

        /// <summary>The page's canvas; the first use starts the page.</summary>
        public Canvas Canvas
        {
            get
            {
                if (_page is null)
                {
                    _number++;
                    using var info = new PdfDocument.PageInfo.Builder(PageWidth, PageHeight, _number).Create();
                    _page = _document.StartPage(info);
                }

                return _page!.Canvas!;
            }
        }

        /// <summary>Ends this page and starts the next.</summary>
        public void NewPage()
        {
            Finish(another: true);
            Y = Margin;
        }

        /// <summary>A drawn picture's pixels must stay until the document is written.</summary>
        /// <param name="bitmap">The picture.</param>
        public void Keep(Bitmap bitmap) => _pictures.Add(bitmap);

        /// <summary>Ends the last page and writes the PDF. A note with nothing in it still gets its one page.</summary>
        /// <param name="output">Where the PDF goes.</param>
        public void WriteTo(Stream output)
        {
            _ = Canvas;
            Finish(another: false);
            _document.WriteTo(output);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _document.Close();
            _document.Dispose();
            foreach (var bitmap in _pictures)
            {
                bitmap.Recycle();
                bitmap.Dispose();
            }
        }

        private void Finish(bool another)
        {
            if (_page is null)
            {
                return;
            }

            // Pages are numbered at the foot only when there is more than one.
            if (another || _number > 1)
            {
                using var paint = new TextPaint(PaintFlags.AntiAlias | PaintFlags.SubpixelText | PaintFlags.LinearText)
                {
                    TextSize = 9, Color = Grey, TextAlign = Paint.Align.Center,
                };
                _page.Canvas!.DrawText(_number.ToString(System.Globalization.CultureInfo.InvariantCulture), PageWidth / 2f, PageHeight - (Margin / 2f), paint);
            }

            _document.FinishPage(_page);
            _page = null;
        }
    }
}
