Imports System.Threading

''' <summary>
''' Draws pages on a background thread so scrolling and zooming never wait for PDFium.
''' Quick low-detail requests go first, then sharp ones, newest first (the pages just
''' scrolled to). Finished pictures come back on the UI thread through
''' <see cref="Rendered"/>. <see cref="Reset"/> drops everything still waiting, for when
''' the zoom or the document changes; pictures that finish afterwards are thrown away.
''' </summary>
Public NotInheritable Class PageRenderQueue
    Implements IDisposable

    Public Class RenderedEventArgs
        Inherits EventArgs

        Public Property PageIndex As Integer
        Public Property Scale As Double
        Public Property LowDetail As Boolean
        ''' <summary>Belongs to the receiver, who must dispose it.</summary>
        Public Property Bitmap As Bitmap
    End Class

    Private NotInheritable Class Job
        Public Renderer As PdfRenderer
        Public PageIndex As Integer
        Public Scale As Double
        Public LowDetail As Boolean
        Public Generation As Integer
        Public Key As String
    End Class

    Private ReadOnly _owner As Control
    Private ReadOnly _gate As New Object()
    Private ReadOnly _waiting As New List(Of Job)
    Private ReadOnly _known As New HashSet(Of String)   ' waiting or being drawn, this generation
    Private ReadOnly _signal As New SemaphoreSlim(0)
    Private ReadOnly _stop As New CancellationTokenSource()
    Private _generation As Integer

    ''' <summary>Raised on the UI thread with a finished picture.</summary>
    Public Event Rendered As EventHandler(Of RenderedEventArgs)

    Public Sub New(owner As Control)
        _owner = owner
        Dim worker As New Thread(AddressOf Work) With {.IsBackground = True, .Name = "Tumbler page drawing"}
        worker.Start()
    End Sub

    ''' <summary>Asks for a page at a scale; asking again for one already waiting does nothing.</summary>
    Public Sub Request(renderer As PdfRenderer, pageIndex As Integer, scale As Double, lowDetail As Boolean)
        If renderer Is Nothing Then Return
        Dim key = $"{pageIndex}:{Math.Round(scale, 4)}:{lowDetail}"
        SyncLock _gate
            If Not _known.Add(key) Then Return
            _waiting.Add(New Job With {.Renderer = renderer, .PageIndex = pageIndex, .Scale = scale,
                                       .LowDetail = lowDetail, .Generation = _generation, .Key = key})
        End SyncLock
        _signal.Release()
    End Sub

    ''' <summary>Forgets every request; anything still being drawn is discarded when done.</summary>
    Public Sub Reset()
        SyncLock _gate
            _generation += 1
            _waiting.Clear()
            _known.Clear()
        End SyncLock
    End Sub

    ''' <summary>Drops waiting requests for pages outside first..last (scrolled away from).</summary>
    Public Sub Retain(first As Integer, last As Integer)
        SyncLock _gate
            For Each j In _waiting.Where(Function(x) x.PageIndex < first OrElse x.PageIndex > last).ToList()
                _waiting.Remove(j)
                _known.Remove(j.Key)
            Next
        End SyncLock
    End Sub

    Private Function TakeNext() As Job
        SyncLock _gate
            If _waiting.Count = 0 Then Return Nothing
            ' Low detail first (it's quick and fills a blank page); otherwise the newest.
            Dim pick = _waiting.LastOrDefault(Function(j) j.LowDetail)
            If pick Is Nothing Then pick = _waiting(_waiting.Count - 1)
            _waiting.Remove(pick)
            Return pick
        End SyncLock
    End Function

    Private Sub Work()
        Try
            Do
                _signal.Wait(_stop.Token)
                Dim job = TakeNext()
                If job Is Nothing Then Continue Do
                Dim bmp As Bitmap = Nothing
                Try
                    If job.Generation = _generation Then
                        bmp = ImageUtil.ToBitmap(job.Renderer.Render(job.PageIndex, job.Scale))
                    End If
                Catch ex As Exception
                    ' Renderer replaced after an edit, or a damaged page: nothing to show.
                End Try
                Deliver(job, bmp)
            Loop
        Catch ex As OperationCanceledException
        End Try
    End Sub

    Private Sub Deliver(job As Job, bmp As Bitmap)
        If _stop.IsCancellationRequested OrElse Not _owner.IsHandleCreated Then
            bmp?.Dispose()
            Return
        End If
        Try
            _owner.BeginInvoke(Sub()
                                   Dim current As Boolean
                                   SyncLock _gate
                                       current = job.Generation = _generation
                                       If current Then _known.Remove(job.Key)
                                   End SyncLock
                                   If bmp Is Nothing Then Return
                                   If Not current OrElse _stop.IsCancellationRequested Then
                                       bmp.Dispose()
                                       Return
                                   End If
                                   RaiseEvent Rendered(Me, New RenderedEventArgs With {
                                       .PageIndex = job.PageIndex, .Scale = job.Scale, .LowDetail = job.LowDetail, .Bitmap = bmp})
                               End Sub)
        Catch ex As InvalidOperationException
            bmp?.Dispose()   ' the window is closing
        End Try
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        If _stop.IsCancellationRequested Then Return
        Reset()
        _stop.Cancel()
    End Sub

End Class
