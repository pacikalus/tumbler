Imports System.Drawing
Imports System.Runtime.InteropServices
Imports Docnet.Core
Imports Docnet.Core.Models
Imports Docnet.Core.Readers

''' <summary>
''' Renders pages with PDFium, and extracts character positions through Docnet.Core.
''' One renderer belongs to one version of the document bytes; make a new one after an edit.
''' Safe to use from any thread: calls are taken one at a time, and a call made
''' after (or while) the renderer is disposed throws ObjectDisposedException.
''' </summary>
Public NotInheritable Class PdfRenderer
    Implements IDisposable

    ' Text positions come back as whole pixels, so extract them at 4x for sub-point accuracy.
    Private Const TextScale As Double = 4

    Private ReadOnly _bytes As Byte()
    Private ReadOnly _readers As New Dictionary(Of Integer, IDocReader)
    Private ReadOnly _readerOrder As New List(Of Integer)
    Private ReadOnly _chars As New Dictionary(Of Integer, IReadOnlyList(Of TextChar))
    Private _disposed As Boolean
    Private _nativeDoc As IntPtr
    Private _nativeData As IntPtr

    Public Sub New(bytes As Byte())
        _bytes = bytes
    End Sub

    ''' <summary>
    ''' Renders a page. <paramref name="scale"/> is pixels per point
    ''' (1.0 = 72 dpi; 96 dpi screens at 100% zoom use 96/72).
    ''' <paramref name="hideAnnotIndex"/> leaves out one annotation (its position in
    ''' the page's /Annots), so the page can be drawn behind a comment being dragged.
    ''' </summary>
    Public Function Render(pageIndex As Integer, scale As Double, Optional hideAnnotIndex As Integer = -1) As RenderedPage
        SyncLock PdfiumNative.Gate
            Return RenderLocked(pageIndex, scale, hideAnnotIndex)
        End SyncLock
    End Function

    Private Function RenderLocked(pageIndex As Integer, scale As Double, hideAnnotIndex As Integer) As RenderedPage
        If _disposed Then Throw New ObjectDisposedException(NameOf(PdfRenderer))
        EnsureNativeDocument()
        Dim page = PdfiumNative.FPDF_LoadPage(_nativeDoc, pageIndex)
        If page = IntPtr.Zero Then Throw New InvalidOperationException($"Could not load page {pageIndex + 1}.")
        Try
            Dim w = Math.Max(1, CInt(Math.Truncate(PdfiumNative.FPDF_GetPageWidth(page) * scale)))
            Dim h = Math.Max(1, CInt(Math.Truncate(PdfiumNative.FPDF_GetPageHeight(page) * scale)))
            Dim bmp = PdfiumNative.FPDFBitmap_Create(w, h, 0)
            If bmp = IntPtr.Zero Then Throw New OutOfMemoryException($"Not enough memory to draw page {pageIndex + 1} at this zoom.")
            Try
                PdfiumNative.FPDFBitmap_FillRect(bmp, 0, 0, w, h, &HFFFFFFFFUI)   ' white, opaque
                Dim hidden = If(hideAnnotIndex >= 0, PdfiumNative.FPDFPage_GetAnnot(page, hideAnnotIndex), IntPtr.Zero)
                Dim oldFlags = 0
                If hidden <> IntPtr.Zero Then
                    oldFlags = PdfiumNative.FPDFAnnot_GetFlags(hidden)
                    PdfiumNative.FPDFAnnot_SetFlags(hidden, oldFlags Or PdfiumNative.FPDF_ANNOT_FLAG_HIDDEN)
                End If
                Try
                    PdfiumNative.FPDF_RenderPageBitmap(bmp, page, 0, 0, w, h, 0, PdfiumNative.FPDF_ANNOT)
                Finally
                    ' Only this renderer's in-memory copy changed, but put the flag back anyway.
                    If hidden <> IntPtr.Zero Then
                        PdfiumNative.FPDFAnnot_SetFlags(hidden, oldFlags)
                        PdfiumNative.FPDFPage_CloseAnnot(hidden)
                    End If
                End Try
                Dim stride = PdfiumNative.FPDFBitmap_GetStride(bmp)
                Dim buffer = PdfiumNative.FPDFBitmap_GetBuffer(bmp)
                Dim bgra(w * h * 4 - 1) As Byte
                For y = 0 To h - 1
                    Marshal.Copy(buffer + y * stride, bgra, y * w * 4, w * 4)
                Next
                ' Bitmaps without alpha leave the 4th byte undefined; make every pixel opaque.
                For i = 3 To bgra.Length - 1 Step 4
                    bgra(i) = 255
                Next
                Return New RenderedPage With {.Width = w, .Height = h, .Bgra = bgra}
            Finally
                PdfiumNative.FPDFBitmap_Destroy(bmp)
            End Try
        Finally
            PdfiumNative.FPDF_ClosePage(page)
        End Try
    End Function

    Private Sub EnsureNativeDocument()
        If _nativeDoc <> IntPtr.Zero Then Return
        Dim init = DocLib.Instance   ' makes sure PDFium is initialised
        _nativeData = Marshal.AllocHGlobal(_bytes.Length)
        Marshal.Copy(_bytes, 0, _nativeData, _bytes.Length)
        _nativeDoc = PdfiumNative.FPDF_LoadMemDocument(_nativeData, _bytes.Length, Nothing)
        If _nativeDoc = IntPtr.Zero Then
            Marshal.FreeHGlobal(_nativeData)
            _nativeData = IntPtr.Zero
            Throw New InvalidOperationException("PDFium could not read this document.")
        End If
    End Sub

    ''' <summary>Characters on a page in reading order, in display space (points).</summary>
    Public Function GetCharacters(pageIndex As Integer) As IReadOnlyList(Of TextChar)
        SyncLock PdfiumNative.Gate
            If _disposed Then Throw New ObjectDisposedException(NameOf(PdfRenderer))
            Return GetCharactersLocked(pageIndex)
        End SyncLock
    End Function

    Private Function GetCharactersLocked(pageIndex As Integer) As IReadOnlyList(Of TextChar)
        Dim cached As IReadOnlyList(Of TextChar) = Nothing
        If _chars.TryGetValue(pageIndex, cached) Then Return cached

        Dim list As New List(Of TextChar)
        Using page = GetReader(TextScale).GetPageReader(pageIndex)
            For Each c In page.GetCharacters()
                Dim b = c.Box
                Dim rect = RectangleF.FromLTRB(
                    CSng(Math.Min(b.Left, b.Right) / TextScale), CSng(Math.Min(b.Top, b.Bottom) / TextScale),
                    CSng(Math.Max(b.Left, b.Right) / TextScale), CSng(Math.Max(b.Top, b.Bottom) / TextScale))
                list.Add(New TextChar With {.Ch = c.Char, .Box = rect})
            Next
        End Using
        _chars(pageIndex) = list
        Return list
    End Function

    Private Function GetReader(scale As Double) As IDocReader
        Dim key = CInt(Math.Round(scale * 1000))
        Dim reader As IDocReader = Nothing
        If _readers.TryGetValue(key, reader) Then
            _readerOrder.Remove(key)
            _readerOrder.Add(key)
            Return reader
        End If
        ' Keep a few scales open (main view, thumbnails, text) and drop the oldest.
        If _readerOrder.Count >= 4 Then
            Dim oldest = _readerOrder(0)
            _readerOrder.RemoveAt(0)
            _readers(oldest).Dispose()
            _readers.Remove(oldest)
        End If
        reader = DocLib.Instance.GetDocReader(_bytes, New PageDimensions(scale))
        _readers(key) = reader
        _readerOrder.Add(key)
        Return reader
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        ' Waits for a page being drawn on another thread to finish first.
        SyncLock PdfiumNative.Gate
            DisposeLocked()
        End SyncLock
    End Sub

    Private Sub DisposeLocked()
        If _disposed Then Return
        _disposed = True
        For Each r In _readers.Values
            r.Dispose()
        Next
        _readers.Clear()
        If _nativeDoc <> IntPtr.Zero Then
            PdfiumNative.FPDF_CloseDocument(_nativeDoc)
            _nativeDoc = IntPtr.Zero
        End If
        If _nativeData <> IntPtr.Zero Then
            Marshal.FreeHGlobal(_nativeData)
            _nativeData = IntPtr.Zero
        End If
    End Sub

End Class
