Imports System.Drawing
Imports System.Text

''' <summary>Turns mouse positions into a run of selected characters, and that run into highlight line boxes.</summary>
Public Module TextSelection

    ''' <summary>
    ''' Index of the character at (or nearest to) a display-space point, or -1 if the page has no text.
    ''' </summary>
    Public Function HitTest(chars As IReadOnlyList(Of TextChar), p As PointF) As Integer
        Dim best = -1
        Dim bestScore = Double.MaxValue
        For i = 0 To chars.Count - 1
            Dim c = chars(i)
            If Not c.IsVisible Then Continue For
            Dim b = c.Box
            If b.Contains(p) Then Return i
            ' Distance to the box, with vertical distance weighted so the
            ' nearest character on the same line wins over one on the next line.
            Dim dx = Math.Max(0, Math.Max(b.Left - p.X, p.X - b.Right))
            Dim dy = Math.Max(0, Math.Max(b.Top - p.Y, p.Y - b.Bottom))
            Dim score = dx + dy * 4
            If score < bestScore Then
                bestScore = score
                best = i
            End If
        Next
        Return best
    End Function

    ''' <summary>
    ''' One quadrilateral per text line covering characters <paramref name="first"/>..<paramref name="last"/>.
    ''' Works for text running in any of the four directions on screen (rotated pages).
    ''' </summary>
    Public Function LineQuads(chars As IReadOnlyList(Of TextChar), first As Integer, last As Integer) As List(Of HighlightQuad)
        Dim result As New List(Of HighlightQuad)
        If first < 0 OrElse last < 0 Then Return result
        If first > last Then
            Dim t = first : first = last : last = t
        End If
        Dim boxes As New List(Of RectangleF)
        For i = first To Math.Min(last, chars.Count - 1)
            If chars(i).IsVisible Then boxes.Add(chars(i).Box)
        Next
        If boxes.Count = 0 Then Return result

        ' Work in a "text frame" where the text runs left to right, then map back.
        Dim dir = TextDirection(boxes)
        Dim current As RectangleF? = Nothing
        Dim prev As RectangleF = RectangleF.Empty
        For Each display In boxes
            Dim b = ToFrame(display, dir)
            If current.HasValue Then
                Dim line = current.Value
                Dim centre = b.Top + b.Height / 2
                Dim sameLine = centre >= line.Top AndAlso centre <= line.Bottom AndAlso b.Left >= prev.Left - b.Height
                If sameLine Then
                    current = RectangleF.Union(line, b)
                Else
                    result.Add(FrameQuad(line, dir))
                    current = b
                End If
            Else
                current = b
            End If
            prev = b
        Next
        result.Add(FrameQuad(current.Value, dir))
        Return result
    End Function

    ''' <summary>A plain rectangle as a quad, for highlighting an area with no text.</summary>
    Public Function AreaQuad(r As RectangleF) As HighlightQuad
        Return FrameQuad(r, 0)
    End Function

    ''' <summary>Direction the text runs on screen: 0 right, 90 down, 180 left, 270 up.</summary>
    Private Function TextDirection(boxes As List(Of RectangleF)) As Integer
        Dim sx = 0.0, sy = 0.0
        For i = 1 To boxes.Count - 1
            Dim a = boxes(i - 1), b = boxes(i)
            Dim dx = (b.Left + b.Width / 2) - (a.Left + a.Width / 2)
            Dim dy = (b.Top + b.Height / 2) - (a.Top + a.Height / 2)
            ' Ignore jumps between lines.
            Dim size = Math.Max(Math.Max(a.Width, a.Height), Math.Max(b.Width, b.Height))
            If Math.Abs(dx) + Math.Abs(dy) > size * 3 Then Continue For
            sx += dx : sy += dy
        Next
        If Math.Abs(sx) >= Math.Abs(sy) Then Return If(sx >= 0, 0, 180)
        Return If(sy >= 0, 90, 270)
    End Function

    Private Function ToFramePoint(x As Single, y As Single, dir As Integer) As PointF
        Select Case dir
            Case 90 : Return New PointF(y, -x)
            Case 180 : Return New PointF(-x, -y)
            Case 270 : Return New PointF(-y, x)
            Case Else : Return New PointF(x, y)
        End Select
    End Function

    Private Function FromFramePoint(x As Single, y As Single, dir As Integer) As PointF
        Select Case dir
            Case 90 : Return New PointF(-y, x)
            Case 180 : Return New PointF(-x, -y)
            Case 270 : Return New PointF(y, -x)
            Case Else : Return New PointF(x, y)
        End Select
    End Function

    Private Function ToFrame(r As RectangleF, dir As Integer) As RectangleF
        Dim a = ToFramePoint(r.Left, r.Top, dir)
        Dim b = ToFramePoint(r.Right, r.Bottom, dir)
        Return RectangleF.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y))
    End Function

    Private Function FrameQuad(r As RectangleF, dir As Integer) As HighlightQuad
        Return New HighlightQuad With {
            .UpperLeft = FromFramePoint(r.Left, r.Top, dir),
            .UpperRight = FromFramePoint(r.Right, r.Top, dir),
            .LowerLeft = FromFramePoint(r.Left, r.Bottom, dir),
            .LowerRight = FromFramePoint(r.Right, r.Bottom, dir)}
    End Function

    ''' <summary>The text of characters <paramref name="first"/>..<paramref name="last"/>.</summary>
    Public Function GetText(chars As IReadOnlyList(Of TextChar), first As Integer, last As Integer) As String
        If first < 0 OrElse last < 0 Then Return ""
        If first > last Then
            Dim t = first : first = last : last = t
        End If
        Dim sb As New StringBuilder()
        For i = first To Math.Min(last, chars.Count - 1)
            Dim ch = chars(i).Ch
            If ch = ChrW(13) Then Continue For
            sb.Append(If(ch = ChrW(10), " "c, ch))
        Next
        Return sb.ToString().Trim()
    End Function

End Module

''' <summary>Parses page ranges such as "1-3, 5, 8-" (1-based) into zero-based page indexes.</summary>
Public Module PageRanges

    Public Function Parse(text As String, pageCount As Integer) As List(Of Integer)
        Dim result As New List(Of Integer)
        Dim t = If(text, "").Trim()
        If t.Length = 0 OrElse t.Equals("all", StringComparison.OrdinalIgnoreCase) Then
            Return Enumerable.Range(0, pageCount).ToList()
        End If
        For Each rawPart In t.Split({","c, ";"c}, StringSplitOptions.RemoveEmptyEntries)
            Dim part = rawPart.Trim()
            Dim dash = part.IndexOf("-"c)
            Dim a As Integer, b As Integer
            If dash < 0 Then
                If Not Integer.TryParse(part, a) Then Throw New FormatException($"""{part}"" is not a page number.")
                b = a
            Else
                Dim left = part.Substring(0, dash).Trim()
                Dim right = part.Substring(dash + 1).Trim()
                a = If(left.Length = 0, 1, ParseNumber(left))
                b = If(right.Length = 0, pageCount, ParseNumber(right))
            End If
            If a < 1 OrElse b < 1 OrElse a > pageCount OrElse b > pageCount Then
                Throw New FormatException($"Page numbers must be between 1 and {pageCount}.")
            End If
            Dim stepDir = If(b >= a, 1, -1)
            For p = a To b Step stepDir
                result.Add(p - 1)
            Next
        Next
        Return result
    End Function

    Private Function ParseNumber(s As String) As Integer
        Dim n As Integer
        If Not Integer.TryParse(s, n) Then Throw New FormatException($"""{s}"" is not a page number.")
        Return n
    End Function

End Module

''' <summary>
''' A highlighted line in display space. Corners are named as the reader sees
''' the text (upper-left = start of the line, top of the glyphs).
''' </summary>
Public Structure HighlightQuad

    Public UpperLeft As PointF
    Public UpperRight As PointF
    Public LowerLeft As PointF
    Public LowerRight As PointF

    Public ReadOnly Property Bounds As RectangleF
        Get
            Dim xs = {UpperLeft.X, UpperRight.X, LowerLeft.X, LowerRight.X}
            Dim ys = {UpperLeft.Y, UpperRight.Y, LowerLeft.Y, LowerRight.Y}
            Return RectangleF.FromLTRB(xs.Min(), ys.Min(), xs.Max(), ys.Max())
        End Get
    End Property

End Structure
