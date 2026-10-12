Imports System.ComponentModel

Public Enum ToolMode
    [Select]
    Highlight
    Note
    Line
End Enum

''' <summary>A page click/drag reported to the main form, in display space (points).</summary>
Public Class PageEventArgs
    Inherits EventArgs

    Public Property PageIndex As Integer
    Public Property Point As PointF
    Public Property Rect As RectangleF
    Public Property EndPoint As PointF
    Public Property Quads As List(Of HighlightQuad)
    Public Property SelectedText As String
End Class

Public Class AnnotationEventArgs
    Inherits EventArgs

    Public Property Annotation As AnnotationInfo
End Class

''' <summary>A comment, line or note dragged to a new place, by an offset in display points.</summary>
Public Class AnnotationMoveEventArgs
    Inherits EventArgs

    Public Property Annotation As AnnotationInfo
    Public Property Offset As SizeF
End Class

''' <summary>A right-click on the pages: the annotation under it (if any) and where it was.</summary>
Public Class ViewMenuEventArgs
    Inherits EventArgs

    Public Property Annotation As AnnotationInfo
    Public Property PageIndex As Integer = -1
    Public Property Point As PointF
    Public Property ClientLocation As Point
End Class

''' <summary>
''' The main page area: all pages stacked vertically, rendered on demand,
''' with mouse tools for selecting, highlighting and adding comments.
''' </summary>
Public Class DocumentView
    Inherits Panel

    Private Const PageGap As Integer = 14
    Private Const PageMargin As Integer = 18
    Public Const MinZoom As Double = 0.1
    Public Const MaxZoom As Double = 4.0

    Private _editor As PdfEditor
    Private _renderer As PdfRenderer
    Private _zoom As Double = 1.0
    Private _tool As ToolMode = ToolMode.Select
    Private _pageRects As New List(Of Rectangle)
    Private ReadOnly _cache As New Dictionary(Of Integer, Bitmap)
    Private _currentPage As Integer
    Private ReadOnly _tip As New ToolTip With {.InitialDelay = 300, .ReshowDelay = 100}
    Private _tipAnnotation As AnnotationInfo

    ' Mouse state
    Private _dragPage As Integer = -1
    Private _dragStart As PointF          ' display space, points
    Private _dragEnd As PointF
    Private _dragStartClient As Point
    Private _selAnchor As Integer = -1     ' text selection (character indexes)
    Private _selEnd As Integer = -1
    Private _areaMode As Boolean
    Private _panning As Boolean
    Private _panOrigin As Point
    Private _panScroll As Point
    Private _dropIndex As Integer = -1
    Private _selected As AnnotationInfo
    Private _moving As AnnotationInfo        ' being dragged to a new place
    Private _moveStartClient As Point
    Private _moveOffset As SizeF             ' display points, already kept on the page
    Private _moveBackground As Bitmap        ' the page without the annotation being dragged

    Public Event CurrentPageChanged As EventHandler
    Public Event ZoomChanged As EventHandler
    Public Event HighlightRequested As EventHandler(Of PageEventArgs)
    Public Event NoteRequested As EventHandler(Of PageEventArgs)
    Public Event LineRequested As EventHandler(Of PageEventArgs)
    Public Event AnnotationActivated As EventHandler(Of AnnotationEventArgs)
    Public Event SelectedAnnotationChanged As EventHandler
    Public Event MenuRequested As EventHandler(Of ViewMenuEventArgs)
    Public Event AnnotationMoveRequested As EventHandler(Of AnnotationMoveEventArgs)
    Public Event CopyRequested As EventHandler
    Public Event PasteRequested As EventHandler
    Public Event DeleteAnnotationRequested As EventHandler

    Public Sub New()
        SetStyle(ControlStyles.OptimizedDoubleBuffer Or ControlStyles.AllPaintingInWmPaint Or
                 ControlStyles.UserPaint Or ControlStyles.ResizeRedraw Or ControlStyles.Selectable, True)
        AutoScroll = True
        BackColor = Color.FromArgb(82, 86, 89)
    End Sub

#Region "Public API"

    <Browsable(False), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property HighlightColor As Color = Color.Yellow
    Public Property LineColor As Color = Color.Red
    Public Property LineWidth As Double = 2

    ''' <summary>Shows a document. Pass keepPosition to stay where the user was after an edit.</summary>
    Public Sub SetDocument(editor As PdfEditor, renderer As PdfRenderer, keepPosition As Boolean)
        Dim scroll = New Point(-AutoScrollPosition.X, -AutoScrollPosition.Y)
        _editor = editor
        _renderer = renderer
        ClearCache()
        CancelDrag()
        If _editor Is Nothing Then
            _pageRects.Clear()
            AutoScrollMinSize = Size.Empty
            _currentPage = 0
        Else
            Layout_Pages()
            If keepPosition Then
                AutoScrollPosition = scroll
                _currentPage = Math.Min(_currentPage, _editor.PageCount - 1)
            Else
                AutoScrollPosition = Point.Empty
                _currentPage = 0
            End If
        End If
        ' Keep the same annotation selected after an edit, if it is still there.
        SelectedAnnotation = FindSame(_selected)
        Invalidate()
        RaiseEvent CurrentPageChanged(Me, EventArgs.Empty)
    End Sub

    ''' <summary>The comment, line or highlight picked with the Select tool (outlined in blue).</summary>
    <Browsable(False), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property SelectedAnnotation As AnnotationInfo
        Get
            Return _selected
        End Get
        Set(value As AnnotationInfo)
            If value Is _selected Then Return
            _selected = value
            Invalidate()
            RaiseEvent SelectedAnnotationChanged(Me, EventArgs.Empty)
        End Set
    End Property

    Private Function FindSame(a As AnnotationInfo) As AnnotationInfo
        If a Is Nothing OrElse _editor Is Nothing OrElse a.PageIndex >= _editor.PageCount Then Return Nothing
        Return _editor.Pages(a.PageIndex).Annotations.FirstOrDefault(Function(x) x.AnnotIndex = a.AnnotIndex AndAlso x.Kind = a.Kind)
    End Function

    ''' <summary>The page and point under the mouse pointer, or page -1 when it is not over a page.</summary>
    Public Function PointUnderMouse(ByRef pageIndex As Integer) As PointF
        Return ClientToPage(PointToClient(Cursor.Position), pageIndex)
    End Function

    <Browsable(False), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property Zoom As Double
        Get
            Return _zoom
        End Get
        Set(value As Double)
            value = Math.Max(MinZoom, Math.Min(MaxZoom, value))
            If Math.Abs(value - _zoom) < 0.0001 Then Return
            ' Keep the current page in view while zooming.
            Dim page = _currentPage
            Dim frac = PageScrollFraction(page)
            _zoom = value
            ClearCache()
            Layout_Pages()
            ScrollToPage(page, frac)
            Invalidate()
            RaiseEvent ZoomChanged(Me, EventArgs.Empty)
        End Set
    End Property

    <Browsable(False), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property Tool As ToolMode
        Get
            Return _tool
        End Get
        Set(value As ToolMode)
            _tool = value
            CancelDrag()
            Cursor = If(value = ToolMode.Select, Cursors.Default, If(value = ToolMode.Highlight, Cursors.IBeam, Cursors.Cross))
            Invalidate()
        End Set
    End Property

    Public ReadOnly Property CurrentPage As Integer
        Get
            Return _currentPage
        End Get
    End Property

    ''' <summary>Zoom that makes the widest page fit the window width.</summary>
    Public Function FitWidthZoom() As Double
        If _editor Is Nothing OrElse _editor.PageCount = 0 Then Return 1
        Dim widest = _editor.Pages.Max(Function(p) p.Geometry.DisplayWidth)
        Dim available = ClientSize.Width - 2 * PageMargin - SystemInformation.VerticalScrollBarWidth
        Return available / (widest * DeviceDpi / 72.0)
    End Function

    Public Sub ScrollToPage(index As Integer, Optional fraction As Double = 0)
        If index < 0 OrElse index >= _pageRects.Count Then Return
        Dim r = _pageRects(index)
        Dim y = r.Top - PageGap \ 2 + CInt(fraction * r.Height)
        AutoScrollPosition = New Point(-AutoScrollPosition.X, Math.Max(0, y))
        SetCurrentPage(index)
        Invalidate()
    End Sub

    ''' <summary>
    ''' Where dropped pages would go if the mouse were released here: before a
    ''' page when over its top half, after it when over its bottom half.
    ''' </summary>
    Public Function DropIndexAt(pt As Point) As Integer
        If _pageRects.Count = 0 Then Return -1
        For i = 0 To _pageRects.Count - 1
            Dim r = ClientPageRect(i)
            If pt.Y < r.Top + r.Height \ 2 Then Return i
        Next
        Return _pageRects.Count
    End Function

    ''' <summary>Shows the insert line before page <paramref name="index"/> (Count = after the last); -1 hides it.</summary>
    Public Sub ShowDropMarker(index As Integer)
        If index = _dropIndex Then Return
        _dropIndex = index
        Invalidate()
    End Sub

    ''' <summary>Scrolls while a drag hovers near the top or bottom edge.</summary>
    Public Sub ScrollForDrag(pt As Point)
        Dim edge = CInt(40 * DeviceDpi / 96.0)
        Dim stepPx = CInt(30 * DeviceDpi / 96.0)
        Dim y = -AutoScrollPosition.Y
        If pt.Y < edge Then
            y -= stepPx
        ElseIf pt.Y > ClientSize.Height - edge Then
            y += stepPx
        Else
            Return
        End If
        AutoScrollPosition = New Point(-AutoScrollPosition.X, Math.Max(0, y))
        UpdateCurrentPage()
        Invalidate()
    End Sub

#End Region

#Region "Layout and coordinates"

    Private ReadOnly Property PixelsPerPoint As Double
        Get
            Return _zoom * DeviceDpi / 72.0
        End Get
    End Property

    Private Sub Layout_Pages()
        _pageRects.Clear()
        If _editor Is Nothing Then Return
        Dim ppp = PixelsPerPoint
        Dim widest = 0
        For Each p In _editor.Pages
            widest = Math.Max(widest, CInt(Math.Round(p.Geometry.DisplayWidth * ppp)))
        Next
        Dim contentWidth = widest + 2 * PageMargin
        Dim y = PageMargin
        For Each p In _editor.Pages
            Dim w = CInt(Math.Round(p.Geometry.DisplayWidth * ppp))
            Dim h = CInt(Math.Round(p.Geometry.DisplayHeight * ppp))
            _pageRects.Add(New Rectangle((contentWidth - w) \ 2, y, w, h))
            y += h + PageGap
        Next
        AutoScrollMinSize = New Size(contentWidth, y + PageMargin - PageGap)
    End Sub

    ''' <summary>Horizontal offset that centres the pages when the window is wider than they are.</summary>
    Private ReadOnly Property CentreOffset As Integer
        Get
            Return Math.Max(0, (ClientSize.Width - AutoScrollMinSize.Width) \ 2)
        End Get
    End Property

    ''' <summary>Page rectangle in client coordinates.</summary>
    Private Function ClientPageRect(index As Integer) As Rectangle
        Dim r = _pageRects(index)
        r.Offset(AutoScrollPosition.X + CentreOffset, AutoScrollPosition.Y)
        Return r
    End Function

    Private Function ClientToPage(pt As Point, ByRef pageIndex As Integer) As PointF
        pageIndex = -1
        For i = 0 To _pageRects.Count - 1
            Dim r = ClientPageRect(i)
            If r.Contains(pt) Then
                pageIndex = i
                Return New PointF(CSng((pt.X - r.Left) / PixelsPerPoint), CSng((pt.Y - r.Top) / PixelsPerPoint))
            End If
        Next
        Return PointF.Empty
    End Function

    ''' <summary>Client point to page point for a known page, clamped to the page.</summary>
    Private Function ClientToPageClamped(pt As Point, pageIndex As Integer) As PointF
        Dim r = ClientPageRect(pageIndex)
        Dim geo = _editor.Pages(pageIndex).Geometry
        Dim x = Math.Max(0, Math.Min(geo.DisplayWidth, (pt.X - r.Left) / PixelsPerPoint))
        Dim y = Math.Max(0, Math.Min(geo.DisplayHeight, (pt.Y - r.Top) / PixelsPerPoint))
        Return New PointF(CSng(x), CSng(y))
    End Function

    Private Function PageToClient(pageIndex As Integer, r As RectangleF) As RectangleF
        Dim pr = ClientPageRect(pageIndex)
        Dim ppp = CSng(PixelsPerPoint)
        Return New RectangleF(pr.Left + r.X * ppp, pr.Top + r.Y * ppp, r.Width * ppp, r.Height * ppp)
    End Function

    Private Function PageScrollFraction(index As Integer) As Double
        If index < 0 OrElse index >= _pageRects.Count Then Return 0
        Dim r = _pageRects(index)
        Return Math.Max(0, Math.Min(1, (-AutoScrollPosition.Y - r.Top) / Math.Max(1.0, r.Height)))
    End Function

    Private Sub UpdateCurrentPage()
        If _pageRects.Count = 0 Then Return
        ' The current page is the one covering the middle of the window.
        Dim middle = -AutoScrollPosition.Y + ClientSize.Height \ 3
        Dim best = 0
        For i = 0 To _pageRects.Count - 1
            If _pageRects(i).Top - PageGap <= middle Then best = i Else Exit For
        Next
        SetCurrentPage(best)
    End Sub

    Private Sub SetCurrentPage(index As Integer)
        If index = _currentPage Then Return
        _currentPage = index
        RaiseEvent CurrentPageChanged(Me, EventArgs.Empty)
    End Sub

#End Region

#Region "Painting"

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        MyBase.OnPaint(e)
        If _editor Is Nothing Then
            TextRenderer.DrawText(e.Graphics, "Open a PDF (File > Open, or drag a file here).", Font,
                                  ClientRectangle, Color.Gainsboro, TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            Return
        End If

        Dim g = e.Graphics
        Dim visible As New List(Of Integer)
        For i = 0 To _pageRects.Count - 1
            Dim r = ClientPageRect(i)
            If r.Bottom < e.ClipRectangle.Top OrElse r.Top > e.ClipRectangle.Bottom Then Continue For
            visible.Add(i)
            Using shadow As New SolidBrush(Color.FromArgb(60, 0, 0, 0))
                g.FillRectangle(shadow, r.Left + 3, r.Top + 3, r.Width, r.Height)
            End Using
            g.FillRectangle(Brushes.White, r)
            Dim bmp = If(_moving IsNot Nothing AndAlso i = _moving.PageIndex, GetMoveBackground(), GetPageBitmap(i))
            If bmp IsNot Nothing Then
                g.InterpolationMode = Drawing2D.InterpolationMode.Low
                g.DrawImage(bmp, r)
            End If
        Next

        PaintOverlay(g)
        If _moving IsNot Nothing Then PaintMovePreview(g) Else PaintSelection(g)
        PaintDropMarker(g)
        EvictCache(visible)
    End Sub

    ''' <summary>While dragging: a copy of the annotation, cut from the page image, at the new place.</summary>
    Private Sub PaintMovePreview(g As Graphics)
        Dim a = _moving
        If a.PageIndex >= _pageRects.Count Then Return
        Dim bounds = PdfEditor.MoveBounds(a)
        Dim pad = CSng(Math.Max(2, a.LineWidth / 2 + 1))
        bounds.Inflate(pad, pad)
        Dim src = PageToClient(a.PageIndex, bounds)
        Dim moved = bounds
        moved.Offset(_moveOffset.Width, _moveOffset.Height)
        Dim dest = PageToClient(a.PageIndex, moved)
        Dim bmp = GetPageBitmap(a.PageIndex)
        If bmp IsNot Nothing Then
            Dim page = ClientPageRect(a.PageIndex)
            Dim sx = bmp.Width / CSng(page.Width), sy = bmp.Height / CSng(page.Height)
            Dim srcInBitmap As New RectangleF((src.X - page.X) * sx, (src.Y - page.Y) * sy, src.Width * sx, src.Height * sy)
            g.DrawImage(bmp, dest, srcInBitmap, GraphicsUnit.Pixel)
        End If
        Using p As New Pen(Color.DodgerBlue, 1.5F) With {.DashStyle = Drawing2D.DashStyle.Dash}
            g.DrawRectangle(p, dest.X, dest.Y, dest.Width, dest.Height)
        End Using
    End Sub

    Private Sub PaintSelection(g As Graphics)
        Dim a = _selected
        If a Is Nothing OrElse a.PageIndex >= _pageRects.Count Then Return
        Using p As New Pen(Color.DodgerBlue, 1.5F) With {.DashStyle = Drawing2D.DashStyle.Dash}
            If a.Kind = AnnotationKind.Line Then
                ' Round handles on both ends.
                Dim ppp = CSng(PixelsPerPoint)
                Dim r = CSng(Math.Max(4, a.LineWidth * ppp / 2 + 3))
                For Each pt In {a.LineStart, a.LineEnd}
                    Dim c = PageToClient(a.PageIndex, New RectangleF(pt, SizeF.Empty)).Location
                    g.FillEllipse(Brushes.White, c.X - r, c.Y - r, 2 * r, 2 * r)
                    g.DrawEllipse(p, c.X - r, c.Y - r, 2 * r, 2 * r)
                Next
            Else
                Dim r = PageToClient(a.PageIndex, a.DisplayRect)
                r.Inflate(3, 3)
                g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height)
            End If
        End Using
    End Sub

    Private Sub PaintDropMarker(g As Graphics)
        If _dropIndex < 0 OrElse _pageRects.Count = 0 Then Return
        Dim count = _pageRects.Count
        Dim nearest = ClientPageRect(Math.Min(_dropIndex, count - 1))
        Dim y As Integer
        If _dropIndex = 0 Then
            y = nearest.Top - PageMargin \ 2
        ElseIf _dropIndex >= count Then
            y = nearest.Bottom + PageGap \ 2 + 2
        Else
            y = (ClientPageRect(_dropIndex - 1).Bottom + nearest.Top) \ 2
        End If
        Dim label = DropMarkerPainter.Describe(_dropIndex, count)
        If _dropIndex = 0 Then
            ' Not enough room above the first page for the label; put it on the page.
            Dim size = TextRenderer.MeasureText(label, Font)
            y = Math.Max(y, size.Height + 12)
        End If
        DropMarkerPainter.Draw(g, y, nearest.Left - 10, nearest.Right + 10, DeviceDpi, label, Font)
    End Sub

    Private Sub PaintOverlay(g As Graphics)
        If _dragPage < 0 Then Return
        Select Case _tool
            Case ToolMode.Highlight
                Using b As New SolidBrush(Color.FromArgb(110, HighlightColor))
                    For Each q In CurrentQuads()
                        g.FillRectangle(b, PageToClient(_dragPage, q.Bounds))
                    Next
                End Using
            Case ToolMode.Line
                Dim a = PageToClient(_dragPage, New RectangleF(_dragStart, SizeF.Empty)).Location
                Dim b = PageToClient(_dragPage, New RectangleF(_dragEnd, SizeF.Empty)).Location
                Dim oldMode = g.SmoothingMode
                g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
                Using p As New Pen(LineColor, CSng(Math.Max(1, LineWidth * PixelsPerPoint))) With {
                        .StartCap = Drawing2D.LineCap.Round, .EndCap = Drawing2D.LineCap.Round}
                    g.DrawLine(p, a, b)
                End Using
                g.SmoothingMode = oldMode
        End Select
    End Sub

    ''' <summary>
    ''' While a comment or line is dragged, its page is drawn without it, so it doesn't
    ''' stay behind at the old place. Made once the mouse actually moves.
    ''' </summary>
    Private Function GetMoveBackground() As Bitmap
        Dim a = _moving
        If _moveOffset.IsEmpty AndAlso _moveBackground Is Nothing Then Return GetPageBitmap(a.PageIndex)
        If _moveBackground Is Nothing Then
            Try
                _moveBackground = ImageUtil.ToBitmap(_renderer.Render(a.PageIndex, PixelsPerPoint, a.AnnotIndex))
            Catch ex As Exception
                Return GetPageBitmap(a.PageIndex)
            End Try
        End If
        Return _moveBackground
    End Function

    Private Sub EndMove()
        _moving = Nothing
        If _moveBackground IsNot Nothing Then
            _moveBackground.Dispose()
            _moveBackground = Nothing
        End If
    End Sub

    Private Function GetPageBitmap(index As Integer) As Bitmap
        Dim bmp As Bitmap = Nothing
        If _cache.TryGetValue(index, bmp) Then Return bmp
        Try
            bmp = ImageUtil.ToBitmap(_renderer.Render(index, PixelsPerPoint))
        Catch ex As Exception
            Return Nothing   ' leave the page blank rather than failing the whole paint
        End Try
        _cache(index) = bmp
        Return bmp
    End Function

    ''' <summary>Keeps bitmaps for visible pages and their neighbours; frees the rest.</summary>
    Private Sub EvictCache(visible As List(Of Integer))
        If visible.Count = 0 Then Return
        Dim lo = visible.Min() - 2, hi = visible.Max() + 2
        For Each key In _cache.Keys.ToList()
            If key < lo OrElse key > hi Then
                _cache(key).Dispose()
                _cache.Remove(key)
            End If
        Next
    End Sub

    Private Sub ClearCache()
        For Each b In _cache.Values
            b.Dispose()
        Next
        _cache.Clear()
    End Sub

#End Region

#Region "Scrolling and zoom"

    Protected Overrides Sub OnScroll(se As ScrollEventArgs)
        MyBase.OnScroll(se)
        UpdateCurrentPage()
        Invalidate()
    End Sub

    Protected Overrides Sub OnMouseWheel(e As MouseEventArgs)
        If (ModifierKeys And Keys.Control) = Keys.Control Then
            Zoom = If(e.Delta > 0, _zoom * 1.1, _zoom / 1.1)
            Return
        End If
        MyBase.OnMouseWheel(e)
        UpdateCurrentPage()
        Invalidate()
    End Sub

    Protected Overrides Sub OnResize(e As EventArgs)
        MyBase.OnResize(e)
        Invalidate()
    End Sub

    Protected Overrides Function IsInputKey(keyData As Keys) As Boolean
        Select Case keyData And Keys.KeyCode
            Case Keys.Up, Keys.Down, Keys.Left, Keys.Right, Keys.PageUp, Keys.PageDown, Keys.Home, Keys.End
                Return True
        End Select
        Return MyBase.IsInputKey(keyData)
    End Function

    Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
        MyBase.OnKeyDown(e)
        If e.Control AndAlso e.KeyCode = Keys.C Then
            RaiseEvent CopyRequested(Me, EventArgs.Empty)
            e.Handled = True
            Return
        End If
        If e.Control AndAlso e.KeyCode = Keys.V Then
            RaiseEvent PasteRequested(Me, EventArgs.Empty)
            e.Handled = True
            Return
        End If
        If _selected IsNot Nothing AndAlso e.KeyCode = Keys.Delete Then
            RaiseEvent DeleteAnnotationRequested(Me, EventArgs.Empty)
            e.Handled = True
            Return
        End If
        If _selected IsNot Nothing AndAlso e.KeyCode = Keys.Enter Then
            RaiseEvent AnnotationActivated(Me, New AnnotationEventArgs With {.Annotation = _selected})
            e.Handled = True
            Return
        End If
        Dim pos = New Point(-AutoScrollPosition.X, -AutoScrollPosition.Y)
        Dim lineStep = CInt(40 * DeviceDpi / 96.0)
        Select Case e.KeyCode
            Case Keys.Down : pos.Y += lineStep
            Case Keys.Up : pos.Y -= lineStep
            Case Keys.Right : pos.X += lineStep
            Case Keys.Left : pos.X -= lineStep
            Case Keys.PageDown : pos.Y += ClientSize.Height - lineStep
            Case Keys.PageUp : pos.Y -= ClientSize.Height - lineStep
            Case Keys.Home : pos.Y = 0
            Case Keys.End : pos.Y = AutoScrollMinSize.Height
            Case Keys.Escape
                CancelDrag()
                SelectedAnnotation = Nothing
                Invalidate()
                Return
            Case Else : Return
        End Select
        AutoScrollPosition = New Point(Math.Max(0, pos.X), Math.Max(0, pos.Y))
        UpdateCurrentPage()
        Invalidate()
        e.Handled = True
    End Sub

#End Region

#Region "Mouse tools"

    Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
        MyBase.OnMouseDown(e)
        Focus()
        If _editor Is Nothing Then Return

        Dim page As Integer
        Dim pt = ClientToPage(e.Location, page)

        If e.Button = MouseButtons.Right Then
            Dim under = If(page >= 0, AnnotationAt(page, pt), Nothing)
            If under IsNot Nothing Then SelectedAnnotation = under
            If page >= 0 Then SetCurrentPage(page)
            RaiseEvent MenuRequested(Me, New ViewMenuEventArgs With {
                .Annotation = under, .PageIndex = page, .Point = pt, .ClientLocation = e.Location})
            Return
        End If
        If e.Button <> MouseButtons.Left Then Return

        ' With any tool but Highlight, pressing on a comment, line or note picks it up:
        ' drag to move it, click to select it, double-click to edit it.
        If page >= 0 AndAlso _tool <> ToolMode.Highlight Then
            Dim grab = AnnotationAt(page, pt)
            If PdfEditor.CanMove(grab) Then
                SelectedAnnotation = grab
                SetCurrentPage(page)
                If e.Clicks >= 2 Then
                    RaiseEvent AnnotationActivated(Me, New AnnotationEventArgs With {.Annotation = grab})
                    Return
                End If
                _moving = grab
                _moveStartClient = e.Location
                _moveOffset = SizeF.Empty
                Capture = True
                Return
            End If
        End If

        If _tool = ToolMode.Select Then
            Dim hit = If(page >= 0, AnnotationAt(page, pt), Nothing)
            ' One click selects (for copying or deleting); a double-click opens it for editing.
            SelectedAnnotation = hit
            If hit IsNot Nothing Then
                If e.Clicks >= 2 Then RaiseEvent AnnotationActivated(Me, New AnnotationEventArgs With {.Annotation = hit})
                Return
            End If
            If page >= 0 Then SetCurrentPage(page)
            ' Drag the page around like a hand tool.
            _panning = True
            _panOrigin = e.Location
            _panScroll = New Point(-AutoScrollPosition.X, -AutoScrollPosition.Y)
            Cursor = Cursors.SizeAll
            Return
        End If

        If page < 0 Then Return
        SetCurrentPage(page)
        _dragPage = page
        _dragStart = pt
        _dragEnd = pt
        _dragStartClient = e.Location
        Capture = True

        If _tool = ToolMode.Highlight Then
            Dim chars = _renderer.GetCharacters(page)
            Dim idx = TextSelection.HitTest(chars, pt)
            ' Starting on (or right next to) text selects text; elsewhere draws a box.
            Dim nearText = idx >= 0 AndAlso RectangleF.Inflate(chars(idx).Box, 4, 4).Contains(pt)
            _areaMode = Not nearText
            _selAnchor = If(nearText, idx, -1)
            _selEnd = _selAnchor
        End If
        Invalidate()
    End Sub

    Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
        MyBase.OnMouseMove(e)
        If _editor Is Nothing Then Return

        If _moving IsNot Nothing Then
            Dim ppp = PixelsPerPoint
            _moveOffset = _editor.ClampMove(_moving, CSng((e.X - _moveStartClient.X) / ppp), CSng((e.Y - _moveStartClient.Y) / ppp))
            Cursor = Cursors.SizeAll
            Invalidate()
            Return
        End If

        If _panning Then
            AutoScrollPosition = New Point(Math.Max(0, _panScroll.X - (e.X - _panOrigin.X)),
                                           Math.Max(0, _panScroll.Y - (e.Y - _panOrigin.Y)))
            UpdateCurrentPage()
            Invalidate()
            Return
        End If

        If _dragPage >= 0 Then
            _dragEnd = ClientToPageClamped(e.Location, _dragPage)
            If _tool = ToolMode.Line AndAlso (ModifierKeys And Keys.Shift) = Keys.Shift Then _dragEnd = SnapLine(_dragStart, _dragEnd)
            If _tool = ToolMode.Highlight AndAlso Not _areaMode Then
                Dim idx = TextSelection.HitTest(_renderer.GetCharacters(_dragPage), _dragEnd)
                If idx >= 0 Then _selEnd = idx
            End If
            Invalidate()
            Return
        End If

        If _tool <> ToolMode.Highlight AndAlso _tool <> ToolMode.Select Then
            Dim page As Integer
            Dim pt = ClientToPage(e.Location, page)
            Cursor = If(page >= 0 AndAlso PdfEditor.CanMove(AnnotationAt(page, pt)), Cursors.SizeAll, Cursors.Default)
        End If

        If _tool = ToolMode.Select Then
            Dim page As Integer
            Dim pt = ClientToPage(e.Location, page)
            Dim hit = If(page >= 0, AnnotationAt(page, pt), Nothing)
            Cursor = If(PdfEditor.CanMove(hit), Cursors.SizeAll, If(hit IsNot Nothing, Cursors.Hand, Cursors.Default))
            If hit IsNot _tipAnnotation Then
                _tipAnnotation = hit
                If hit Is Nothing Then
                    _tip.Hide(Me)
                Else
                    Dim text = If(String.IsNullOrWhiteSpace(hit.Contents), "(no text)", hit.Contents)
                    _tip.Show($"{hit.KindName} by {If(hit.Author, "?")}" & vbCrLf & text & vbCrLf & "Drag to move, double-click to edit, right-click to copy", Me, e.X + 16, e.Y + 16)
                End If
            End If
        End If
    End Sub

    Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
        MyBase.OnMouseUp(e)
        If _moving IsNot Nothing Then
            Dim a = _moving, offset = _moveOffset
            EndMove()
            Capture = False
            Cursor = Cursors.Default
            Invalidate()
            If offset.Width <> 0 OrElse offset.Height <> 0 Then
                RaiseEvent AnnotationMoveRequested(Me, New AnnotationMoveEventArgs With {.Annotation = a, .Offset = offset})
            End If
            Return
        End If
        If _panning Then
            _panning = False
            Cursor = Cursors.Default
            Return
        End If
        If _dragPage < 0 Then Return
        Capture = False

        Dim page = _dragPage
        Dim moved = Math.Abs(e.X - _dragStartClient.X) + Math.Abs(e.Y - _dragStartClient.Y) > 4
        Dim args As New PageEventArgs With {.PageIndex = page, .Point = _dragStart}

        Select Case _tool
            Case ToolMode.Highlight
                args.Quads = CurrentQuads()
                If Not _areaMode Then
                    args.SelectedText = TextSelection.GetText(_renderer.GetCharacters(page), _selAnchor, _selEnd)
                End If
                CancelDrag()
                Invalidate()
                ' A plain click does nothing; the user has to drag across something.
                If args.Quads.Count > 0 AndAlso moved Then
                    RaiseEvent HighlightRequested(Me, args)
                End If

            Case ToolMode.Note
                CancelDrag()
                RaiseEvent NoteRequested(Me, args)

            Case ToolMode.Line
                args.EndPoint = _dragEnd
                CancelDrag()
                Invalidate()
                ' A plain click does nothing; the user has to drag the line out.
                If moved Then RaiseEvent LineRequested(Me, args)
        End Select
    End Sub

    Protected Overrides Sub OnMouseLeave(e As EventArgs)
        MyBase.OnMouseLeave(e)
        _tip.Hide(Me)
        _tipAnnotation = Nothing
    End Sub

    ''' <summary>Shift held: keeps the line horizontal, vertical or at 45 degrees.</summary>
    Private Shared Function SnapLine(a As PointF, b As PointF) As PointF
        Dim dx = b.X - a.X, dy = b.Y - a.Y
        Dim angle = Math.Round(Math.Atan2(dy, dx) / (Math.PI / 4)) * (Math.PI / 4)
        Dim length = Math.Sqrt(dx * dx + dy * dy)
        If Math.Abs(Math.Cos(angle)) > 0.9 OrElse Math.Abs(Math.Sin(angle)) > 0.9 Then
            ' Straight across or down: keep the mouse's own extent along that axis.
            Return If(Math.Abs(Math.Cos(angle)) > 0.9, New PointF(b.X, a.Y), New PointF(a.X, b.Y))
        End If
        Return New PointF(CSng(a.X + length * Math.Cos(angle)), CSng(a.Y + length * Math.Sin(angle)))
    End Function

    Private Function DragRect() As RectangleF
        Return RectangleF.FromLTRB(Math.Min(_dragStart.X, _dragEnd.X), Math.Min(_dragStart.Y, _dragEnd.Y),
                                   Math.Max(_dragStart.X, _dragEnd.X), Math.Max(_dragStart.Y, _dragEnd.Y))
    End Function

    Private Function CurrentQuads() As List(Of HighlightQuad)
        If _dragPage < 0 Then Return New List(Of HighlightQuad)
        If _areaMode Then
            Dim r = DragRect()
            If r.Width < 2 OrElse r.Height < 2 Then Return New List(Of HighlightQuad)
            Return New List(Of HighlightQuad) From {TextSelection.AreaQuad(r)}
        End If
        Return TextSelection.LineQuads(_renderer.GetCharacters(_dragPage), _selAnchor, _selEnd)
    End Function

    Private Sub CancelDrag()
        EndMove()
        _dragPage = -1
        _selAnchor = -1
        _selEnd = -1
        _areaMode = False
        Capture = False
    End Sub

    ''' <summary>The topmost annotation under a page point (notes first, since they sit on top).</summary>
    Private Function AnnotationAt(page As Integer, pt As PointF) As AnnotationInfo
        Dim annots = _editor.Pages(page).Annotations
        For i = annots.Count - 1 To 0 Step -1
            Dim a = annots(i)
            If a.Kind = AnnotationKind.Line Then
                ' Only near the line itself, not anywhere in its bounding box.
                If DistanceToSegment(pt, a.LineStart, a.LineEnd) <= Math.Max(4.0, a.LineWidth / 2 + 2) Then Return a
            ElseIf a.DisplayRect.Contains(pt) Then
                Return a
            End If
        Next
        Return Nothing
    End Function

    Private Shared Function DistanceToSegment(p As PointF, a As PointF, b As PointF) As Double
        Dim dx = b.X - a.X, dy = b.Y - a.Y
        Dim len2 = dx * dx + dy * dy
        Dim t = If(len2 = 0, 0.0, Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2)))
        Dim cx = a.X + t * dx, cy = a.Y + t * dy
        Return Math.Sqrt((p.X - cx) ^ 2 + (p.Y - cy) ^ 2)
    End Function

#End Region

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            ClearCache()
            _tip.Dispose()
        End If
        MyBase.Dispose(disposing)
    End Sub

End Class
