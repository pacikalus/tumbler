Imports System.Drawing

''' <summary>
''' Converts between PDF user space (origin bottom-left, y up, unrotated)
''' and "display space": the page as the user sees it, in points,
''' origin top-left, y down, with the page /Rotate already applied.
''' </summary>
Public NotInheritable Class PageGeometry

    Public ReadOnly Property Left As Double
    Public ReadOnly Property Bottom As Double
    Public ReadOnly Property Right As Double
    Public ReadOnly Property Top As Double

    ''' <summary>Page rotation in degrees: 0, 90, 180 or 270 (clockwise).</summary>
    Public ReadOnly Property Rotation As Integer

    Public Sub New(left As Double, bottom As Double, right As Double, top As Double, rotation As Integer)
        Me.Left = Math.Min(left, right)
        Me.Right = Math.Max(left, right)
        Me.Bottom = Math.Min(bottom, top)
        Me.Top = Math.Max(bottom, top)
        Me.Rotation = NormalizeRotation(rotation)
    End Sub

    Public Shared Function NormalizeRotation(degrees As Integer) As Integer
        Dim r = degrees Mod 360
        If r < 0 Then r += 360
        ' PDF only allows multiples of 90; snap anything else.
        Return CInt(Math.Round(r / 90.0)) * 90 Mod 360
    End Function

    Public ReadOnly Property BoxWidth As Double
        Get
            Return Right - Left
        End Get
    End Property

    Public ReadOnly Property BoxHeight As Double
        Get
            Return Top - Bottom
        End Get
    End Property

    ''' <summary>Width of the page as displayed (after rotation), in points.</summary>
    Public ReadOnly Property DisplayWidth As Double
        Get
            Return If(Rotation = 90 OrElse Rotation = 270, BoxHeight, BoxWidth)
        End Get
    End Property

    ''' <summary>Height of the page as displayed (after rotation), in points.</summary>
    Public ReadOnly Property DisplayHeight As Double
        Get
            Return If(Rotation = 90 OrElse Rotation = 270, BoxWidth, BoxHeight)
        End Get
    End Property

    ''' <summary>Display point (top-left origin, points) to PDF user space.</summary>
    Public Function ToPdf(u As Double, v As Double) As PointF
        Select Case Rotation
            Case 90
                Return New PointF(CSng(Left + v), CSng(Bottom + u))
            Case 180
                Return New PointF(CSng(Right - u), CSng(Bottom + v))
            Case 270
                Return New PointF(CSng(Right - v), CSng(Top - u))
            Case Else
                Return New PointF(CSng(Left + u), CSng(Top - v))
        End Select
    End Function

    Public Function ToPdf(p As PointF) As PointF
        Return ToPdf(p.X, p.Y)
    End Function

    ''' <summary>PDF user space point to display point (top-left origin, points).</summary>
    Public Function ToDisplay(x As Double, y As Double) As PointF
        Select Case Rotation
            Case 90
                Return New PointF(CSng(y - Bottom), CSng(x - Left))
            Case 180
                Return New PointF(CSng(Right - x), CSng(y - Bottom))
            Case 270
                Return New PointF(CSng(Top - y), CSng(Right - x))
            Case Else
                Return New PointF(CSng(x - Left), CSng(Top - y))
        End Select
    End Function

    ''' <summary>Display rectangle to the PDF rectangle that covers it.</summary>
    Public Function ToPdfRect(r As RectangleF) As PdfRect
        Dim a = ToPdf(r.Left, r.Top)
        Dim b = ToPdf(r.Right, r.Bottom)
        Return PdfRect.FromCorners(a.X, a.Y, b.X, b.Y)
    End Function

    ''' <summary>PDF rectangle to the display rectangle that covers it.</summary>
    Public Function ToDisplayRect(r As PdfRect) As RectangleF
        Dim a = ToDisplay(r.X1, r.Y1)
        Dim b = ToDisplay(r.X2, r.Y2)
        Return RectangleF.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y))
    End Function

End Class

''' <summary>A rectangle in PDF user space (x1,y1 = lower-left, x2,y2 = upper-right).</summary>
Public Structure PdfRect

    Public X1 As Double
    Public Y1 As Double
    Public X2 As Double
    Public Y2 As Double

    Public Shared Function FromCorners(ax As Double, ay As Double, bx As Double, by As Double) As PdfRect
        Return New PdfRect With {
            .X1 = Math.Min(ax, bx), .Y1 = Math.Min(ay, by),
            .X2 = Math.Max(ax, bx), .Y2 = Math.Max(ay, by)}
    End Function

    Public ReadOnly Property Width As Double
        Get
            Return X2 - X1
        End Get
    End Property

    Public ReadOnly Property Height As Double
        Get
            Return Y2 - Y1
        End Get
    End Property

    Public Function Union(other As PdfRect) As PdfRect
        Return New PdfRect With {
            .X1 = Math.Min(X1, other.X1), .Y1 = Math.Min(Y1, other.Y1),
            .X2 = Math.Max(X2, other.X2), .Y2 = Math.Max(Y2, other.Y2)}
    End Function

    Public Function Inflate(amount As Double) As PdfRect
        Return New PdfRect With {.X1 = X1 - amount, .Y1 = Y1 - amount, .X2 = X2 + amount, .Y2 = Y2 + amount}
    End Function

    Public Overrides Function ToString() As String
        Return $"[{X1:0.##} {Y1:0.##} {X2:0.##} {Y2:0.##}]"
    End Function

End Structure
