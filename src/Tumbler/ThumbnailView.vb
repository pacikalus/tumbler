Imports System.ComponentModel

''' <summary>
''' The page strip on the left: a thumbnail per page. Click to go to a page,
''' Ctrl+click / Shift+click to select several pages for rotating or deleting.
''' Drag pages up or down to put them in a different order.
''' </summary>
Public Class ThumbnailView
    Inherits Panel

    Private Const ThumbWidth As Integer = 110     ' at 96 dpi
    Private Const Pad As Integer = 10
    Private Const LabelHeight As Integer = 18

    Private _editor As PdfEditor
    Private _renderer As PdfRenderer
    Private _rects As New List(Of Rectangle)
    Private _scale As Double
    Private ReadOnly _cache As New Dictionary(Of Integer, Bitmap)
    Private ReadOnly _selected As New SortedSet(Of Integer)
    Private _anchor As Integer = 0
    Private _current As Integer = 0
    Private _dropIndex As Integer = -1

    ' Dragging pages to reorder them
    Private _pressPage As Integer = -1
    Private _pressPoint As Point
    Private _clickPending As Boolean       ' plain click on a selected page: narrow the selection on release, unless it becomes a drag
    Private _dragPages As List(Of Integer) ' Nothing unless pages are being dragged
    Private ReadOnly _dragTimer As New Timer With {.Interval = 50}

    ''' <summary>The user clicked a page.</summary>
    Public Event PageClicked As EventHandler
    Public Event SelectionChanged As EventHandler
    ''' <summary>Delete key pressed with pages selected.</summary>
    Public Event DeleteRequested As EventHandler
    ''' <summary>Pages were dragged to a new place in the strip.</summary>
    Public Event PagesMoveRequested As EventHandler(Of PagesMoveEventArgs)

    Public Sub New()
        SetStyle(ControlStyles.OptimizedDoubleBuffer Or ControlStyles.AllPaintingInWmPaint Or
                 ControlStyles.UserPaint Or ControlStyles.ResizeRedraw Or ControlStyles.Selectable, True)
        AutoScroll = True
        BackColor = Color.FromArgb(60, 63, 65)
        ' Keeps scrolling while a dragged page is held near the top or bottom edge.
        AddHandler _dragTimer.Tick, Sub() UpdatePageDrag(PointToClient(Cursor.Position))
    End Sub

    Public Sub SetDocument(editor As PdfEditor, renderer As PdfRenderer, keepSelection As Boolean)
        _editor = editor
        _renderer = renderer
        ClearCache()
        CancelPageDrag()
        If Not keepSelection OrElse editor Is Nothing Then
            _selected.Clear()
            _current = 0
            _anchor = 0
        Else
            _selected.RemoveWhere(Function(i) i >= editor.PageCount)
            _current = Math.Min(_current, editor.PageCount - 1)
        End If
        DoLayout()
        Invalidate()
        RaiseEvent SelectionChanged(Me, EventArgs.Empty)
    End Sub

    ''' <summary>The page the main view is showing (drawn with an outline).</summary>
    <Browsable(False), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property CurrentPage As Integer
        Get
            Return _current
        End Get
        Set(value As Integer)
            If value = _current Then Return
            _current = value
            EnsureVisible(value)
            Invalidate()
        End Set
    End Property

    ''' <summary>Selected pages; if none are selected, the current page.</summary>
    Public Function SelectedPages() As List(Of Integer)
        If _selected.Count > 0 Then Return _selected.ToList()
        If _editor Is Nothing Then Return New List(Of Integer)
        Return New List(Of Integer) From {_current}
    End Function

    Public ReadOnly Property HasExplicitSelection As Boolean
        Get
            Return _selected.Count > 0
        End Get
    End Property

    Public Sub ClearSelection()
        _selected.Clear()
        Invalidate()
        RaiseEvent SelectionChanged(Me, EventArgs.Empty)
    End Sub

    ''' <summary>Selects these pages (one page just becomes the current page, with no selection).</summary>
    Public Sub SelectPages(pages As IList(Of Integer))
        If _editor Is Nothing OrElse pages.Count = 0 Then Return
        _selected.Clear()
        If pages.Count > 1 Then
            For Each p In pages
                If p >= 0 AndAlso p < _editor.PageCount Then _selected.Add(p)
            Next
        End If
        _anchor = pages(0)
        _current = pages(0)
        EnsureVisible(_current)
        Invalidate()
        RaiseEvent SelectionChanged(Me, EventArgs.Empty)
    End Sub

#Region "Layout and painting"

    Private Sub DoLayout()
        _rects.Clear()
        If _editor Is Nothing OrElse _editor.PageCount = 0 Then
            AutoScrollMinSize = Size.Empty
            Return
        End If
        Dim dpi = DeviceDpi / 96.0
        Dim thumbW = CInt(ThumbWidth * dpi)
        ' One scale for all pages, so the renderer can reuse a single PDFium document.
        Dim widest = _editor.Pages.Max(Function(p) p.Geometry.DisplayWidth)
        _scale = thumbW / widest
        Dim gap = CInt(Pad * dpi)
        Dim labelH = CInt(LabelHeight * dpi)
        Dim y = gap
        For Each p In _editor.Pages
            Dim w = CInt(p.Geometry.DisplayWidth * _scale)
            Dim h = CInt(p.Geometry.DisplayHeight * _scale)
            _rects.Add(New Rectangle(gap + (thumbW - w) \ 2, y, w, h))
            y += h + labelH + gap
        Next
        AutoScrollMinSize = New Size(thumbW + 2 * gap, y)
    End Sub

    Private Function ClientRect(i As Integer) As Rectangle
        Dim r = _rects(i)
        Dim offsetX = Math.Max(0, (ClientSize.Width - AutoScrollMinSize.Width) \ 2)
        r.Offset(AutoScrollPosition.X + offsetX, AutoScrollPosition.Y)
        Return r
    End Function

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        MyBase.OnPaint(e)
        If _editor Is Nothing Then Return
        Dim g = e.Graphics
        Dim labelH = CInt(LabelHeight * DeviceDpi / 96.0)
        Dim visible As New List(Of Integer)
        For i = 0 To _rects.Count - 1
            Dim r = ClientRect(i)
            If r.Bottom + labelH < e.ClipRectangle.Top OrElse r.Top > e.ClipRectangle.Bottom Then Continue For
            visible.Add(i)

            If _selected.Contains(i) Then
                Using b As New SolidBrush(Color.FromArgb(70, 130, 200))
                    g.FillRectangle(b, Rectangle.Inflate(New Rectangle(r.X, r.Y, r.Width, r.Height + labelH), 5, 5))
                End Using
            End If
            g.FillRectangle(Brushes.White, r)
            Dim bmp = GetThumb(i)
            If bmp IsNot Nothing Then g.DrawImage(bmp, r)
            If _dragPages IsNot Nothing AndAlso _dragPages.Contains(i) Then
                ' Faded while it is being moved.
                Using veil As New SolidBrush(Color.FromArgb(150, BackColor))
                    g.FillRectangle(veil, Rectangle.Inflate(New Rectangle(r.X, r.Y, r.Width, r.Height + labelH), 5, 5))
                End Using
            End If
            If i = _current Then
                Using p As New Pen(Color.Orange, 2)
                    g.DrawRectangle(p, Rectangle.Inflate(r, 1, 1))
                End Using
            End If
            TextRenderer.DrawText(g, (i + 1).ToString(), Font, New Rectangle(r.X - 20, r.Bottom + 2, r.Width + 40, labelH),
                                  Color.White, TextFormatFlags.HorizontalCenter)
        Next

        PaintDropMarker(g, labelH)

        If visible.Count > 0 Then
            Dim lo = visible.Min() - 4, hi = visible.Max() + 4
            For Each key In _cache.Keys.ToList()
                If key < lo OrElse key > hi Then
                    _cache(key).Dispose()
                    _cache.Remove(key)
                End If
            Next
        End If
    End Sub

    Private Sub PaintDropMarker(g As Graphics, labelH As Integer)
        If _dropIndex < 0 OrElse _rects.Count = 0 Then Return
        Dim gap = CInt(Pad * DeviceDpi / 96.0)
        Dim count = _rects.Count
        Dim nearest = ClientRect(Math.Min(_dropIndex, count - 1))
        Dim y As Integer
        If _dropIndex = 0 Then
            y = nearest.Top - gap \ 2
        ElseIf _dropIndex >= count Then
            y = nearest.Bottom + labelH + gap \ 2
        Else
            y = (ClientRect(_dropIndex - 1).Bottom + labelH + nearest.Top) \ 2
        End If
        y = Math.Max(3, y)
        Dim indent = CInt(12 * DeviceDpi / 96.0)
        DropMarkerPainter.Draw(g, y, indent, ClientSize.Width - indent, DeviceDpi)
    End Sub

    ''' <summary>Where dropped pages would go if the mouse were released here.</summary>
    Public Function DropIndexAt(pt As Point) As Integer
        If _rects.Count = 0 Then Return -1
        For i = 0 To _rects.Count - 1
            Dim r = ClientRect(i)
            If pt.Y < r.Top + r.Height \ 2 Then Return i
        Next
        Return _rects.Count
    End Function

    ''' <summary>Shows the insert line before page <paramref name="index"/> (Count = after the last); -1 hides it.</summary>
    Public Sub ShowDropMarker(index As Integer)
        If index = _dropIndex Then Return
        _dropIndex = index
        Invalidate()
    End Sub

    ''' <summary>Scrolls while a drag hovers near the top or bottom edge.</summary>
    Public Sub ScrollForDrag(pt As Point)
        Dim edge = CInt(30 * DeviceDpi / 96.0)
        Dim stepPx = CInt(25 * DeviceDpi / 96.0)
        Dim y = -AutoScrollPosition.Y
        If pt.Y < edge Then
            y -= stepPx
        ElseIf pt.Y > ClientSize.Height - edge Then
            y += stepPx
        Else
            Return
        End If
        AutoScrollPosition = New Point(-AutoScrollPosition.X, Math.Max(0, y))
        Invalidate()
    End Sub

    Private Function GetThumb(i As Integer) As Bitmap
        Dim bmp As Bitmap = Nothing
        If _cache.TryGetValue(i, bmp) Then Return bmp
        Try
            bmp = ImageUtil.ToBitmap(_renderer.Render(i, _scale))
        Catch
            Return Nothing
        End Try
        _cache(i) = bmp
        Return bmp
    End Function

    Private Sub ClearCache()
        For Each b In _cache.Values
            b.Dispose()
        Next
        _cache.Clear()
    End Sub

    Private Sub EnsureVisible(i As Integer)
        If i < 0 OrElse i >= _rects.Count Then Return
        Dim r = ClientRect(i)
        If r.Top < 0 OrElse r.Bottom > ClientSize.Height Then
            AutoScrollPosition = New Point(0, Math.Max(0, _rects(i).Top - CInt(Pad * DeviceDpi / 96.0)))
        End If
    End Sub

    Protected Overrides Sub OnScroll(se As ScrollEventArgs)
        MyBase.OnScroll(se)
        Invalidate()
    End Sub

    Protected Overrides Sub OnMouseWheel(e As MouseEventArgs)
        MyBase.OnMouseWheel(e)
        Invalidate()
    End Sub

#End Region

#Region "Mouse and keyboard"

    Private Function HitTest(pt As Point) As Integer
        Dim labelH = CInt(LabelHeight * DeviceDpi / 96.0)
        For i = 0 To _rects.Count - 1
            Dim r = ClientRect(i)
            If Rectangle.Inflate(New Rectangle(r.X, r.Y, r.Width, r.Height + labelH), 5, 5).Contains(pt) Then Return i
        Next
        Return -1
    End Function

    Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
        MyBase.OnMouseDown(e)
        Focus()
        Dim i = HitTest(e.Location)
        If i < 0 Then Return

        If e.Button = MouseButtons.Right Then
            ' Right-click on an unselected page acts on that page only.
            If Not _selected.Contains(i) Then
                _selected.Clear()
                _selected.Add(i)
                _anchor = i
            End If
        ElseIf (ModifierKeys And Keys.Control) = Keys.Control Then
            If Not _selected.Remove(i) Then _selected.Add(i)
            _anchor = i
        ElseIf (ModifierKeys And Keys.Shift) = Keys.Shift Then
            _selected.Clear()
            For p = Math.Min(_anchor, i) To Math.Max(_anchor, i)
                _selected.Add(p)
            Next
        ElseIf _selected.Count > 1 AndAlso _selected.Contains(i) Then
            ' Keep the selection so all of it can be dragged; a plain click narrows it on release.
            _clickPending = True
        Else
            _selected.Clear()
            _anchor = i
        End If
        If e.Button = MouseButtons.Left Then
            _pressPage = i
            _pressPoint = e.Location
        End If
        _current = i
        Invalidate()
        RaiseEvent SelectionChanged(Me, EventArgs.Empty)
        RaiseEvent PageClicked(Me, EventArgs.Empty)
    End Sub

    Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
        MyBase.OnMouseMove(e)
        If _dragPages IsNot Nothing Then
            UpdatePageDrag(e.Location)
        ElseIf _pressPage >= 0 AndAlso e.Button = MouseButtons.Left AndAlso _editor IsNot Nothing AndAlso _editor.PageCount > 1 Then
            Dim slop = SystemInformation.DragSize
            If Math.Abs(e.X - _pressPoint.X) > slop.Width OrElse Math.Abs(e.Y - _pressPoint.Y) > slop.Height Then
                ' Start dragging: the whole selection if the page was in it, otherwise just that page.
                _dragPages = If(_selected.Contains(_pressPage), _selected.ToList(), New List(Of Integer) From {_pressPage})
                _clickPending = False
                Capture = True
                Cursor = Cursors.SizeNS
                _dragTimer.Start()
                UpdatePageDrag(e.Location)
            End If
        End If
    End Sub

    Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
        MyBase.OnMouseUp(e)
        If _dragPages IsNot Nothing AndAlso e.Button = MouseButtons.Left Then
            Dim pages = _dragPages
            Dim at = _dropIndex
            CancelPageDrag()
            If at >= 0 Then RaiseEvent PagesMoveRequested(Me, New PagesMoveEventArgs(pages, at))
        ElseIf _clickPending Then
            _selected.Clear()
            _anchor = _current
            Invalidate()
            RaiseEvent SelectionChanged(Me, EventArgs.Empty)
        End If
        _clickPending = False
        _pressPage = -1
    End Sub

    Protected Overrides Sub OnLostFocus(e As EventArgs)
        MyBase.OnLostFocus(e)
        CancelPageDrag()   ' switched to another window mid-drag
    End Sub

    ''' <summary>Follows the mouse while pages are dragged: scrolls near the edges and moves the blue line.</summary>
    Private Sub UpdatePageDrag(pt As Point)
        If _dragPages Is Nothing Then Return
        ScrollForDrag(pt)
        ShowDropMarker(DropIndexAt(pt))
    End Sub

    Private Sub CancelPageDrag()
        If _dragPages Is Nothing Then Return
        _dragPages = Nothing
        _dragTimer.Stop()
        Cursor = Cursors.Default
        ShowDropMarker(-1)
        If Capture Then Capture = False
        Invalidate()
    End Sub

    Protected Overrides Function IsInputKey(keyData As Keys) As Boolean
        Select Case keyData And Keys.KeyCode
            Case Keys.Up, Keys.Down, Keys.Delete, Keys.Escape
                Return True
        End Select
        Return MyBase.IsInputKey(keyData)
    End Function

    Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
        MyBase.OnKeyDown(e)
        If _editor Is Nothing Then Return
        Select Case e.KeyCode
            Case Keys.Escape
                CancelPageDrag()
            Case Keys.Delete
                RaiseEvent DeleteRequested(Me, EventArgs.Empty)
            Case Keys.Down, Keys.Up
                Dim i = Math.Max(0, Math.Min(_editor.PageCount - 1, _current + If(e.KeyCode = Keys.Down, 1, -1)))
                _selected.Clear()
                _current = i
                _anchor = i
                EnsureVisible(i)
                Invalidate()
                RaiseEvent SelectionChanged(Me, EventArgs.Empty)
                RaiseEvent PageClicked(Me, EventArgs.Empty)
            Case Keys.A
                If e.Control Then
                    _selected.Clear()
                    For p = 0 To _editor.PageCount - 1
                        _selected.Add(p)
                    Next
                    Invalidate()
                    RaiseEvent SelectionChanged(Me, EventArgs.Empty)
                End If
        End Select
    End Sub

#End Region

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            _dragTimer.Dispose()
            ClearCache()
        End If
        MyBase.Dispose(disposing)
    End Sub

End Class

''' <summary>Which pages were dragged, and the gap they were dropped in (before page InsertAt).</summary>
Public Class PagesMoveEventArgs
    Inherits EventArgs

    Public Sub New(pages As IList(Of Integer), insertAt As Integer)
        Me.Pages = pages
        Me.InsertAt = insertAt
    End Sub

    Public ReadOnly Property Pages As IList(Of Integer)
    Public ReadOnly Property InsertAt As Integer
End Class
