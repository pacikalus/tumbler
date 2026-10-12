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
            ed.AddHighlight(0, quads, colors(round Mod 4), "Pacikalus")
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

    ''' <summary>
    ''' Mimics background page drawing: several threads draw pages while the main thread
    ''' makes edits, swaps in a new renderer and throws the old one away. Nothing may
    ''' crash; a renderer used after it was thrown away only reports that.
    ''' </summary>
    <Fact>
    Public Sub RenderingFromManyThreads_WhileRenderersAreReplaced()
        Dim ed = PdfEditor.Open(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample.pdf")))
        Dim current As New PdfRenderer(ed.Bytes)
        Dim unexpected As New Concurrent.ConcurrentQueue(Of Exception)
        Dim done = False
        Dim workers = Enumerable.Range(0, 3).Select(
            Function(w) Threading.Tasks.Task.Run(
                Sub()
                    Dim n = 0
                    Do Until Threading.Volatile.Read(done)
                        Dim r = Threading.Volatile.Read(current)
                        Try
                            Dim img = r.Render(0, If(n Mod 2 = 0, 0.5, 1.2))
                            Assert.True(img.Width > 0)
                            r.GetCharacters(0)
                        Catch ex As ObjectDisposedException
                            ' the main thread replaced it meanwhile: fine
                        Catch ex As Exception
                            unexpected.Enqueue(ex)
                        End Try
                        n += 1
                    Loop
                End Sub)).ToArray()
        For round = 1 To 15
            ed.AddComment(0, New PointF(60 + round * 5, 300), "round " & round, Color.Gold, 11, "Pacikalus")
            Dim old = current
            Threading.Volatile.Write(current, New PdfRenderer(ed.Bytes))
            old.Dispose()
            Threading.Thread.Sleep(20)
        Next
        Threading.Volatile.Write(done, True)
        Threading.Tasks.Task.WaitAll(workers)
        current.Dispose()
        Assert.Empty(unexpected)
    End Sub

End Class
