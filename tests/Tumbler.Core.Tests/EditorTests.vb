Imports System.Drawing
Imports System.IO
Imports System.IO.Compression
Imports Xunit

Public Class EditorTests

    Private Shared Function Fixture(name As String) As String
        Return Path.Combine(AppContext.BaseDirectory, "Fixtures", name)
    End Function

    Private Shared Function OpenSample(Optional name As String = "sample.pdf") As PdfEditor
        Return PdfEditor.Open(File.ReadAllBytes(Fixture(name)))
    End Function

    ''' <summary>Finds the characters of a phrase on a page.</summary>
    Private Shared Function FindPhrase(r As PdfRenderer, page As Integer, phrase As String) As (First As Integer, Last As Integer)
        Dim chars = r.GetCharacters(page)
        Dim text = New String(chars.Select(Function(c) c.Ch).ToArray())
        Dim i = text.IndexOf(phrase, StringComparison.Ordinal)
        Assert.True(i >= 0, $"'{phrase}' not found on page {page + 1}: {text}")
        Return (i, i + phrase.Length - 1)
    End Function

#Region "Geometry"

    <Theory>
    <InlineData(0)>
    <InlineData(90)>
    <InlineData(180)>
    <InlineData(270)>
    Public Sub Geometry_RoundTrips(rotation As Integer)
        Dim geo As New PageGeometry(10, 20, 622, 812, rotation)
        For Each pt In {New PointF(0, 0), New PointF(100, 50), New PointF(300, 400)}
            Dim pdf = geo.ToPdf(pt)
            Dim back = geo.ToDisplay(pdf.X, pdf.Y)
            Assert.Equal(pt.X, back.X, 3)
            Assert.Equal(pt.Y, back.Y, 3)
        Next
        ' Display origin is the top-left corner of the rotated page.
        Dim origin = geo.ToPdf(0, 0)
        Select Case rotation
            Case 0 : Assert.Equal(New PointF(10, 812), origin)
            Case 90 : Assert.Equal(New PointF(10, 20), origin)
            Case 180 : Assert.Equal(New PointF(622, 20), origin)
            Case 270 : Assert.Equal(New PointF(622, 812), origin)
        End Select
    End Sub

    <Fact>
    Public Sub Geometry_MatchesPdfiumTextPositions_OnEveryRotation()
        ' rotated.pdf: page 1 = 0, page 2 = 90, page 3 = 180, page 4 = 270 degrees.
        ' The text "The quick" starts at x=72, baseline y=650 in PDF space on every page.
        Dim ed = OpenSample("rotated.pdf")
        Using r As New PdfRenderer(ed.Bytes)
            For page = 0 To 3
                Dim geo = ed.Pages(page).Geometry
                Assert.Equal(page * 90, geo.Rotation)
                Dim hit = FindPhrase(r, page, "The quick")
                Dim box = r.GetCharacters(page)(hit.First).Box
                Dim pdf = geo.ToPdfRect(box)
                Assert.InRange(pdf.X1, 70, 75)          ' left edge of "T"
                Assert.InRange(pdf.Y1, 646, 652)        ' around the baseline
                Assert.InRange(pdf.Y2, 655, 662)        ' cap height of 12pt Helvetica
            Next
        End Using
    End Sub

#End Region

#Region "Pages"

    <Fact>
    Public Sub Open_ReadsPages()
        Dim ed = OpenSample()
        Assert.Equal(5, ed.PageCount)
        Assert.Equal(612, ed.Pages(0).Geometry.DisplayWidth, 1)
        Assert.Equal(792, ed.Pages(0).Geometry.DisplayHeight, 1)
        Assert.False(ed.IsDirty)
    End Sub

    <Fact>
    Public Sub DeletePages_RemovesTheRightPages()
        Dim ed = OpenSample()
        ed.DeletePages({1, 3})
        Assert.Equal(3, ed.PageCount)
        Using r As New PdfRenderer(ed.Bytes)
            Assert.Contains("page 1", PageText(r, 0))
            Assert.Contains("page 3", PageText(r, 1))
            Assert.Contains("page 5", PageText(r, 2))
        End Using
        Assert.True(ed.IsDirty)
    End Sub

    <Fact>
    Public Sub DeletePages_RefusesToDeleteEverything()
        Dim ed = OpenSample()
        Assert.Throws(Of InvalidOperationException)(Sub() ed.DeletePages({0, 1, 2, 3, 4}))
        Assert.Equal(5, ed.PageCount)
    End Sub

    <Fact>
    Public Sub Rotate_OnePage_And_AllPages()
        Dim ed = OpenSample()
        ed.RotatePages({2}, 90)
        Assert.Equal(90, ed.Pages(2).Geometry.Rotation)
        Assert.Equal(0, ed.Pages(1).Geometry.Rotation)
        Assert.Equal(792, ed.Pages(2).Geometry.DisplayWidth, 1)

        ed.RotateAllPages(-90)
        Assert.Equal(0, ed.Pages(2).Geometry.Rotation)
        Assert.Equal(270, ed.Pages(0).Geometry.Rotation)

        ed.RotateAllPages(180)
        Assert.Equal(90, ed.Pages(0).Geometry.Rotation)
        Assert.Equal(180, ed.Pages(2).Geometry.Rotation)
    End Sub

    <Fact>
    Public Sub InsertPagesFrom_OtherFile_AtPosition()
        Dim ed = OpenSample()
        ed.InsertPagesFrom(Fixture("other.pdf"), {2, 0}, 1)
        Assert.Equal(7, ed.PageCount)
        Using r As New PdfRenderer(ed.Bytes)
            Assert.Contains("Sample - page 1", PageText(r, 0))
            Assert.Contains("Other - page 3", PageText(r, 1))
            Assert.Contains("Other - page 1", PageText(r, 2))
            Assert.Contains("Sample - page 2", PageText(r, 3))
        End Using
        ' A4 page kept its own size.
        Assert.Equal(595.3, ed.Pages(1).Geometry.DisplayWidth, 0)
    End Sub

    <Fact>
    Public Sub InsertPagesFrom_AppendsAtEnd()
        Dim ed = OpenSample()
        ed.InsertPagesFrom(Fixture("other.pdf"), PageRanges.Parse("all", 3), ed.PageCount)
        Assert.Equal(8, ed.PageCount)
        Using r As New PdfRenderer(ed.Bytes)
            Assert.Contains("Other - page 3", PageText(r, 7))
        End Using
    End Sub

    <Fact>
    Public Sub UndoRedo_RestoresState()
        Dim ed = OpenSample()
        ed.DeletePages({0})
        ed.RotatePages({0}, 90)
        Assert.Equal(4, ed.PageCount)
        ed.Undo()
        Assert.Equal(0, ed.Pages(0).Geometry.Rotation)
        ed.Undo()
        Assert.Equal(5, ed.PageCount)
        Assert.False(ed.IsDirty)
        Assert.False(ed.CanUndo)
        ed.Redo()
        Assert.Equal(4, ed.PageCount)
        Assert.True(ed.CanRedo)
    End Sub

    <Fact>
    Public Sub EditLockedFile_OpensWithoutPassword_AndCanBeEdited()
        ' Many government forms open for anyone but are locked against changes.
        Dim ed = OpenSample("locked.pdf")
        Assert.Equal(5, ed.PageCount)
        ed.DeletePages({0})
        ed.InsertPagesFrom(Fixture("locked.pdf"), {0}, ed.PageCount)
        Assert.Equal(5, ed.PageCount)
        Assert.Equal(5, PdfEditor.CountPages(Fixture("locked.pdf")))
        Using r As New PdfRenderer(ed.Bytes)
            Assert.Contains("page 2", PageText(r, 0))
        End Using
    End Sub

    <Fact>
    Public Sub DamagedFile_IsRepairedOnOpen_AndWhenInserted()
        Dim ed = OpenSample("damaged.pdf")
        Assert.Equal(5, ed.PageCount)
        ed.InsertPagesFrom(Fixture("damaged.pdf"), {4}, 0)
        Assert.Equal(6, ed.PageCount)
        Using r As New PdfRenderer(ed.Bytes)
            Assert.Contains("page 5", PageText(r, 0))
        End Using
    End Sub

    <Fact>
    Public Sub NotAPdf_GivesAPlainMessage()
        Dim ex = Assert.Throws(Of InvalidDataException)(Function() PdfEditor.Open(Text.Encoding.ASCII.GetBytes("hello")))
        Assert.Contains("not a PDF", ex.Message)
    End Sub

    <Fact>
    Public Sub ProtectedFile_NeedsPassword_ThenOpens()
        Dim bytes = File.ReadAllBytes(Fixture("protected.pdf"))
        Assert.ThrowsAny(Of Exception)(Function() PdfEditor.Open(bytes))
        Dim ed = PdfEditor.Open(bytes, "secret")
        Assert.Equal(5, ed.PageCount)
        ' Re-opening the normalised bytes must not need a password any more.
        Dim again = PdfEditor.Open(ed.Bytes)
        Assert.Equal(5, again.PageCount)
        Using r As New PdfRenderer(ed.Bytes)
            Assert.Contains("page 1", PageText(r, 0))
        End Using
    End Sub

    <Fact>
    Public Sub PageRanges_Parse()
        Assert.Equal({0, 1, 2, 4}, PageRanges.Parse("1-3, 5", 6))
        Assert.Equal({3, 4, 5}, PageRanges.Parse("4-", 6))
        Assert.Equal({0, 1}, PageRanges.Parse("-2", 6))
        Assert.Equal({2, 1, 0}, PageRanges.Parse("3-1", 6))
        Assert.Equal(6, PageRanges.Parse("", 6).Count)
        Assert.Throws(Of FormatException)(Function() PageRanges.Parse("7", 6))
        Assert.Throws(Of FormatException)(Function() PageRanges.Parse("abc", 6))
    End Sub

#End Region

#Region "Comments and highlights"

    <Fact>
    Public Sub Note_AddEditDelete()
        Dim ed = OpenSample()
        ed.AddNote(0, New PointF(300, 200), "Check this número", Color.Red, "Mike")
        Dim a = ed.Pages(0).Annotations.Single()
        Assert.Equal(AnnotationKind.Note, a.Kind)
        Assert.Equal("Check this número", a.Contents)
        Assert.Equal("Mike", a.Author)
        Assert.Equal(Color.Red.ToArgb(), a.Color.ToArgb())
        Dim disp = ed.Pages(0).Geometry.ToDisplayRect(a.Rect)
        Assert.Equal(300, disp.Left, 1)
        Assert.Equal(200, disp.Top, 1)

        ed.UpdateAnnotation(a, "Changed", Color.Blue)
        a = ed.Pages(0).Annotations.Single()
        Assert.Equal("Changed", a.Contents)
        Assert.Equal(Color.Blue.ToArgb(), a.Color.ToArgb())

        ed.DeleteAnnotation(a)
        Assert.Empty(ed.Pages(0).Annotations)
    End Sub

    <Fact>
    Public Sub Comment_IsVisibleOnPage_AndResizesWhenEdited()
        Dim ed = OpenSample()
        ed.AddComment(0, New PointF(300, 250), "Please check this paragraph before sending.", Color.Blue, 11, "Mike")
        Dim a = ed.Pages(0).Annotations.Single()
        Assert.Equal(AnnotationKind.Comment, a.Kind)
        Assert.Equal(Color.Blue.ToArgb(), a.Color.ToArgb())
        Assert.Equal(300, a.DisplayRect.Left, 1)
        Assert.Equal(250, a.DisplayRect.Top, 1)
        Dim firstHeight = a.DisplayRect.Height

        ' Only the text is drawn, in blue: no fill and no border.
        Using r As New PdfRenderer(ed.Bytes)
            Dim img = r.Render(0, 1)
            Dim corner = PixelAt(img, CInt(a.DisplayRect.Right - 2), CInt(a.DisplayRect.Bottom - 2))
            Assert.True(corner.R > 245 AndAlso corner.G > 245 AndAlso corner.B > 245, $"expected white (no box), got {corner}")
            Dim bluePixels = 0
            For y = CInt(a.DisplayRect.Top) To CInt(a.DisplayRect.Bottom) - 1
                For x = CInt(a.DisplayRect.Left) To CInt(a.DisplayRect.Right) - 1
                    Dim c = PixelAt(img, x, y)
                    If c.B > 150 AndAlso c.R < 120 AndAlso c.G < 120 Then bluePixels += 1
                Next
            Next
            Assert.True(bluePixels > 50, $"expected blue text, found {bluePixels} blue pixels")
        End Using

        ' Longer text keeps the corner and width, and the box grows to fit.
        ed.UpdateAnnotation(a, "Please check this paragraph before sending. Also the totals on page 3 look wrong and the date is missing.", Color.Green, 11)
        a = ed.Pages(0).Annotations.Single()
        Assert.Equal(AnnotationKind.Comment, a.Kind)
        Assert.Equal(Color.Green.ToArgb(), a.Color.ToArgb())
        Assert.Equal(300, a.DisplayRect.Left, 1)
        Assert.Equal(250, a.DisplayRect.Top, 1)
        Assert.True(a.DisplayRect.Height > firstHeight)
        Using r As New PdfRenderer(ed.Bytes)
            Dump(r.Render(0, 1.5), "comment")
        End Using
    End Sub

    <Theory>
    <InlineData(0)>
    <InlineData(1)>
    <InlineData(2)>
    <InlineData(3)>
    Public Sub Line_IsDrawnWhereDragged_OnRotatedPages(page As Integer)
        Dim ed = OpenSample("rotated.pdf")
        Dim a As New PointF(100, 120), b As New PointF(260, 200)
        ed.AddLine(page, a, b, Color.Red, 4, "Mike")
        Dim line = ed.Pages(page).Annotations.Single()
        Assert.Equal(AnnotationKind.Line, line.Kind)
        Assert.Equal("Line", line.KindName)
        Assert.Equal(Color.Red.ToArgb(), line.Color.ToArgb())
        Assert.Equal(4, line.LineWidth, 2)
        Assert.Equal(a.X, line.LineStart.X, 1)
        Assert.Equal(a.Y, line.LineStart.Y, 1)
        Assert.Equal(b.X, line.LineEnd.X, 1)
        Assert.Equal(b.Y, line.LineEnd.Y, 1)

        Using r As New PdfRenderer(ed.Bytes)
            Dim img = r.Render(page, 1)
            Dim mid = PixelAt(img, CInt((a.X + b.X) / 2), CInt((a.Y + b.Y) / 2))
            Assert.True(mid.R > 200 AndAlso mid.G < 80 AndAlso mid.B < 80, $"expected red at the middle of the line, got {mid}")
            Dim off = PixelAt(img, CInt(a.X), CInt(b.Y))
            Assert.True(off.R > 245 AndAlso off.G > 245, $"expected nothing away from the line, got {off}")
        End Using

        ' Changing the colour keeps the line where it is.
        ed.UpdateAnnotation(line, "", Color.Blue)
        line = ed.Pages(page).Annotations.Single()
        Assert.Equal(Color.Blue.ToArgb(), line.Color.ToArgb())
        Using r As New PdfRenderer(ed.Bytes)
            Dim mid = PixelAt(r.Render(page, 1), CInt((a.X + b.X) / 2), CInt((a.Y + b.Y) / 2))
            Assert.True(mid.B > 200 AndAlso mid.R < 80, $"expected blue, got {mid}")
        End Using
    End Sub

    <Fact>
    Public Sub CopiedComment_PastesOnAnotherPage_WithSameTextAndColour()
        Dim ed = OpenSample("rotated.pdf")
        ed.AddComment(0, New PointF(100, 150), "Check the total", Color.Purple, 14, "Mike")
        Dim clip = AnnotationClip.From(ed.Pages(0).Annotations.Single())
        ed.PasteAnnotation(clip, 1, New PointF(50, 60), "Mike")   ' page 2 is turned 90°
        Dim copy = ed.Pages(1).Annotations.Single()
        Assert.Equal(AnnotationKind.Comment, copy.Kind)
        Assert.Equal("Check the total", copy.Contents)
        Assert.Equal(Color.Purple.ToArgb(), copy.Color.ToArgb())
        Assert.Equal(14, copy.FontSize, 1)
        Assert.Equal(50, copy.DisplayRect.Left, 1)
        Assert.Equal(60, copy.DisplayRect.Top, 1)
        Assert.Single(ed.Pages(0).Annotations)   ' the original stays
    End Sub

    <Fact>
    Public Sub CopiedLine_KeepsItsShape_AndStaysOnThePage()
        Dim ed = OpenSample()
        ed.AddLine(0, New PointF(100, 100), New PointF(300, 160), Color.Green, 4, "Mike")
        Dim clip = AnnotationClip.From(ed.Pages(0).Annotations.Single())
        Assert.Equal(New RectangleF(100, 100, 200, 60), clip.Bounds)

        ed.PasteAnnotation(clip, 0, New PointF(200, 400), "Mike")
        Dim copy = ed.Pages(0).Annotations.Last()
        Assert.Equal(AnnotationKind.Line, copy.Kind)
        Assert.Equal(4, copy.LineWidth, 2)
        Assert.Equal(200, copy.LineStart.X, 1)
        Assert.Equal(400, copy.LineStart.Y, 1)
        Assert.Equal(400, copy.LineEnd.X, 1)
        Assert.Equal(460, copy.LineEnd.Y, 1)

        ' Pasted past the right edge of a 612-point-wide page: slid back so all of it shows.
        ed.PasteAnnotation(clip, 0, New PointF(580, 100), "Mike")
        copy = ed.Pages(0).Annotations.Last()
        Assert.Equal(612, Math.Max(copy.LineStart.X, copy.LineEnd.X), 1)
    End Sub

    <Fact>
    Public Sub Highlights_CannotBeCopied()
        Dim ed = OpenSample()
        ed.AddHighlight(0, {TextSelection.AreaQuad(New RectangleF(72, 100, 100, 20))}, Color.Yellow, "Mike")
        Dim h = ed.Pages(0).Annotations.Single()
        Assert.False(AnnotationClip.CanCopy(h))
        Assert.Throws(Of InvalidOperationException)(Function() AnnotationClip.From(h))
    End Sub

    <Fact>
    Public Sub Comment_NearPageEdge_StaysOnPage()
        Dim ed = OpenSample("rotated.pdf")
        ed.AddComment(1, New PointF(780, 600), "Edge of a rotated page", Color.Red, 12, "Mike")
        Dim a = ed.Pages(1).Annotations.Single()
        Dim geo = ed.Pages(1).Geometry
        Assert.True(a.DisplayRect.Right <= geo.DisplayWidth + 0.5)
        Assert.True(a.DisplayRect.Bottom <= geo.DisplayHeight + 0.5)
    End Sub

    <Fact>
    Public Sub TextBox_KeepsColourAndFontSize()
        Dim ed = OpenSample()
        ed.AddTextBox(1, New RectangleF(72, 400, 200, 60), "Hello (world) \ text that wraps onto a second line", Color.Green, 14, "Mike")
        Dim a = ed.Pages(1).Annotations.Single()
        Assert.Equal(AnnotationKind.TextBox, a.Kind)
        Assert.Equal(14, a.FontSize, 1)
        Assert.Equal(Color.Green.ToArgb(), a.Color.ToArgb())
        ed.UpdateAnnotation(a, "Shorter", Color.Purple, 10)
        a = ed.Pages(1).Annotations.Single()
        Assert.Equal(10, a.FontSize, 1)
        Assert.Equal(Color.Purple.ToArgb(), a.Color.ToArgb())
    End Sub

    <Fact>
    Public Sub Highlight_FromTextSelection_CoversTheWords()
        Dim ed = OpenSample()
        Dim quads As List(Of HighlightQuad)
        Using r As New PdfRenderer(ed.Bytes)
            Dim hit = FindPhrase(r, 0, "quick brown fox")
            Assert.Equal("quick brown fox", TextSelection.GetText(r.GetCharacters(0), hit.First, hit.Last))
            quads = TextSelection.LineQuads(r.GetCharacters(0), hit.First, hit.Last)
        End Using
        Assert.Single(quads)
        ed.AddHighlight(0, quads, Color.Yellow, "Mike")
        Dim a = ed.Pages(0).Annotations.Single()
        Assert.Equal(AnnotationKind.Highlight, a.Kind)
        Assert.InRange(a.Rect.Y1, 644, 650)
        Assert.InRange(a.Rect.Y2, 656, 664)

        ' The rendered page is yellow under the highlight and white beside it.
        Using r As New PdfRenderer(ed.Bytes)
            Dim img = r.Render(0, 1)
            Dim inside = PixelAt(img, CInt((a.Rect.X1 + 3)), CInt(792 - (a.Rect.Y1 + 1)))
            Assert.True(inside.R > 200 AndAlso inside.G > 200 AndAlso inside.B < 80, $"expected yellow, got {inside}")
            Dim outside = PixelAt(img, CInt(a.Rect.X2 + 40), CInt(792 - (a.Rect.Y1 + 1)))
            Assert.True(outside.R > 240 AndAlso outside.G > 240 AndAlso outside.B > 240, $"expected white, got {outside}")
            Dump(img, "highlight-page1")
        End Using
    End Sub

    <Fact>
    Public Sub Highlight_TwoLines_GivesTwoQuads()
        Dim ed = OpenSample()
        Using r As New PdfRenderer(ed.Bytes)
            Dim a = FindPhrase(r, 0, "lazy dog.")
            Dim b = FindPhrase(r, 0, "Second line")
            Dim quads = TextSelection.LineQuads(r.GetCharacters(0), a.First, b.Last)
            Assert.Equal(2, quads.Count)
        End Using
    End Sub

    <Theory>
    <InlineData(1)>
    <InlineData(2)>
    <InlineData(3)>
    Public Sub Highlight_OnRotatedPage_LandsOnTheText(page As Integer)
        Dim ed = OpenSample("rotated.pdf")
        Using r As New PdfRenderer(ed.Bytes)
            Dim hit = FindPhrase(r, page, "quick brown")
            Dim quads = TextSelection.LineQuads(r.GetCharacters(page), hit.First, hit.Last)
            Assert.Single(quads)
            ed.AddHighlight(page, quads, Color.Orange, "Mike")
            ed.AddNote(page, New PointF(50, 50), "Note on rotated page", Color.Blue, "Mike")
            ed.AddTextBox(page, New RectangleF(60, 120, 220, 50), "Upright text box", Color.Red, 12, "Mike")
        End Using
        Dim n = ed.Pages(page).Annotations.First(Function(x) x.Kind = AnnotationKind.Note)
        Assert.Equal(50, n.DisplayRect.Left, 1)
        Assert.Equal(50, n.DisplayRect.Top, 1)
        Dim tb = ed.Pages(page).Annotations.First(Function(x) x.Kind = AnnotationKind.TextBox)
        Assert.Equal(60, tb.DisplayRect.Left, 1)
        Assert.Equal(220, tb.DisplayRect.Width, 1)
        Dim h = ed.Pages(page).Annotations.First(Function(x) x.Kind = AnnotationKind.Highlight)
        ' In PDF space the text runs horizontally from x=72 on the baseline y=650.
        Assert.InRange(h.Rect.X1, 93, 99)     ' "quick" starts after "The " (72 + 24pt)
        Assert.InRange(h.Rect.Y1, 644, 650)
        Assert.InRange(h.Rect.Y2, 656, 664)
        Using r As New PdfRenderer(ed.Bytes)
            Dump(r.Render(page, 1), $"rotated-page{page + 1}")
        End Using
    End Sub

    <Fact>
    Public Sub SavedFile_ReopensWithAnnotations()
        Dim ed = OpenSample()
        ed.AddNote(0, New PointF(400, 100), "Saved note", Color.Magenta, "Mike")
        ed.RotatePages({0}, 90)
        ed.InsertPagesFrom(Fixture("other.pdf"), {0}, ed.PageCount)
        Dim outPath = Path.Combine(Path.GetTempPath(), $"tumbler-test-{Guid.NewGuid():N}.pdf")
        Try
            ed.SaveAs(outPath)
            Assert.False(ed.IsDirty)
            Dim again = PdfEditor.Open(outPath)
            Assert.Equal(6, again.PageCount)
            Assert.Equal(90, again.Pages(0).Geometry.Rotation)
            Assert.Equal("Saved note", again.Pages(0).Annotations.Single().Contents)
            ' Save over an existing file too.
            ed.DeletePages({5})
            ed.SaveAs(outPath)
            Assert.Equal(5, PdfEditor.Open(outPath).PageCount)
            If Environment.GetEnvironmentVariable("TUMBLER_DUMP") IsNot Nothing Then
                File.Copy(outPath, Path.Combine(Environment.GetEnvironmentVariable("TUMBLER_DUMP"), "saved.pdf"), True)
            End If
        Finally
            File.Delete(outPath)
        End Try
    End Sub

    <Fact>
    Public Sub ImportedPages_KeepTheirComments()
        ' Make a source file that has a note, then insert its page.
        Dim src = OpenSample("other.pdf")
        src.AddNote(0, New PointF(100, 100), "From the other file", Color.Green, "Mike")
        Dim srcPath = Path.Combine(Path.GetTempPath(), $"tumbler-src-{Guid.NewGuid():N}.pdf")
        Try
            src.SaveAs(srcPath)
            Dim ed = OpenSample()
            ed.InsertPagesFrom(srcPath, {0}, 0)
            Assert.Equal("From the other file", ed.Pages(0).Annotations.Single().Contents)
        Finally
            File.Delete(srcPath)
        End Try
    End Sub

#End Region

#Region "Helpers"

    Private Shared Function PageText(r As PdfRenderer, page As Integer) As String
        Return New String(r.GetCharacters(page).Select(Function(c) c.Ch).ToArray())
    End Function

    Private Shared Function PixelAt(img As RenderedPage, x As Integer, y As Integer) As Color
        Dim i = (y * img.Width + x) * 4
        Return Color.FromArgb(img.Bgra(i + 3), img.Bgra(i + 2), img.Bgra(i + 1), img.Bgra(i))
    End Function

    ''' <summary>Writes a PNG when TUMBLER_DUMP names a folder, for eyeballing renders.</summary>
    Private Shared Sub Dump(img As RenderedPage, name As String)
        Dim folder = Environment.GetEnvironmentVariable("TUMBLER_DUMP")
        If String.IsNullOrEmpty(folder) Then Return
        Directory.CreateDirectory(folder)
        File.WriteAllBytes(Path.Combine(folder, name & ".png"), ToPng(img))
    End Sub

    Private Shared Function ToPng(img As RenderedPage) As Byte()
        Dim raw As New MemoryStream()
        For y = 0 To img.Height - 1
            raw.WriteByte(0)
            For x = 0 To img.Width - 1
                Dim i = (y * img.Width + x) * 4
                raw.WriteByte(img.Bgra(i + 2)) : raw.WriteByte(img.Bgra(i + 1)) : raw.WriteByte(img.Bgra(i))
            Next
        Next
        Dim z As New MemoryStream()
        Using zs As New ZLibStream(z, CompressionLevel.Fastest, True)
            raw.Position = 0 : raw.CopyTo(zs)
        End Using
        Dim png As New MemoryStream()
        png.Write({&H89, &H50, &H4E, &H47, &HD, &HA, &H1A, &HA})
        Dim ihdr As New MemoryStream()
        WriteBE(ihdr, img.Width) : WriteBE(ihdr, img.Height)
        ihdr.Write({8, 2, 0, 0, 0})
        Chunk(png, "IHDR", ihdr.ToArray())
        Chunk(png, "IDAT", z.ToArray())
        Chunk(png, "IEND", Array.Empty(Of Byte)())
        Return png.ToArray()
    End Function

    Private Shared Sub Chunk(s As Stream, type As String, data As Byte())
        WriteBE(s, data.Length)
        Dim td = Text.Encoding.ASCII.GetBytes(type).Concat(data).ToArray()
        s.Write(td)
        WriteBE(s, BitConverter.ToInt32(BitConverter.GetBytes(Crc32(td)), 0))
    End Sub

    Private Shared Sub WriteBE(s As Stream, v As Integer)
        s.Write({CByte((v >> 24) And 255), CByte((v >> 16) And 255), CByte((v >> 8) And 255), CByte(v And 255)})
    End Sub

    Private Shared Function Crc32(data As Byte()) As UInteger
        Dim c = &HFFFFFFFFUI
        For Each b In data
            c = c Xor b
            For k = 0 To 7
                c = If((c And 1UI) <> 0, &HEDB88320UI Xor (c >> 1), c >> 1)
            Next
        Next
        Return c Xor &HFFFFFFFFUI
    End Function

#End Region

End Class
