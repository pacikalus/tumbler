Imports System.Drawing
Imports System.IO
Imports Xunit

' PDFium is not thread-safe, and neither is the app's use of it, so run tests one at a time.
<Assembly: CollectionBehavior(DisableTestParallelization:=True)>

Public Class StressTests

    ''' <summary>
    ''' Mimics the app: after every highlight a new renderer is made, pages are
    ''' redrawn at two sizes and the old renderer is thrown away. This used to
    ''' crash the process on the third round.
    ''' </summary>
    <Fact>
    Public Sub ManyMultiLineHighlights_InARow()
        Dim ed = PdfEditor.Open(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample.pdf")))
        Dim renderer As New PdfRenderer(ed.Bytes)
        Dim colors = {Color.Yellow, Color.Green, Color.Pink, Color.Orange}
        For round = 1 To 25
            Dim chars = renderer.GetCharacters(0)
            ' Drag from somewhere on line 1 to somewhere on line 2, like a user would.
            Dim a = TextSelection.HitTest(chars, New PointF(72 + round Mod 10 * 3, 145))
            Dim b = TextSelection.HitTest(chars, New PointF(200 + round Mod 10 * 5, 165))
            Dim quads = TextSelection.LineQuads(chars, a, b)
            Assert.Equal(2, quads.Count)
            ed.AddHighlight(0, quads, colors(round Mod 4), "Mike")
            Dim old = renderer
            renderer = New PdfRenderer(ed.Bytes)
            renderer.Render(0, 1.33)
            renderer.Render(0, 0.18)
            renderer.GetCharacters(0)
            old.Dispose()
            GC.Collect()
            GC.WaitForPendingFinalizers()
            Assert.Equal(round, ed.Pages(0).Annotations.Count)
        Next
        renderer.Dispose()
    End Sub

End Class
