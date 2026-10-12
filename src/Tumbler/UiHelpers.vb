Imports System.Drawing.Imaging
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Text.Json

''' <summary>The colours offered for highlights, comments and lines.</summary>
Public Module Palette

    Public ReadOnly Colors As (Name As String, Color As Color)() = {
        ("Yellow", Color.FromArgb(255, 235, 59)),
        ("Green", Color.FromArgb(118, 221, 98)),
        ("Blue", Color.FromArgb(79, 166, 255)),
        ("Pink", Color.FromArgb(255, 128, 191)),
        ("Orange", Color.FromArgb(255, 167, 38)),
        ("Red", Color.FromArgb(229, 57, 53)),
        ("Purple", Color.FromArgb(149, 117, 205)),
        ("Black", Color.FromArgb(33, 33, 33))}

    Public Function ColorByName(name As String) As Color
        Return Colors.First(Function(c) c.Name = name).Color
    End Function

    ''' <summary>A small square image filled with a colour, for menus and lists.</summary>
    Public Function Swatch(c As Color, size As Integer) As Bitmap
        Dim bmp As New Bitmap(size, size)
        Using g = Graphics.FromImage(bmp), b As New SolidBrush(c)
            g.Clear(Color.Transparent)
            g.FillRectangle(b, 1, 1, size - 2, size - 2)
            g.DrawRectangle(Pens.DimGray, 1, 1, size - 3, size - 3)
        End Using
        Return bmp
    End Function

End Module

''' <summary>The line that shows where dragged-in pages will go.</summary>
Public Module DropMarkerPainter

    Public ReadOnly MarkerColor As Color = Color.DodgerBlue

    Public Function Describe(index As Integer, pageCount As Integer) As String
        If index <= 0 Then Return "Insert at the start"
        If index >= pageCount Then Return "Insert at the end"
        Return $"Insert between pages {index} and {index + 1}"
    End Function

    Public Sub Draw(g As Graphics, y As Integer, left As Integer, right As Integer, dpi As Integer,
                    Optional label As String = Nothing, Optional font As Font = Nothing)
        Dim scale = dpi / 96.0F
        Dim thick = Math.Max(3, CInt(4 * scale))
        Dim arrow = CInt(7 * scale)
        Dim oldMode = g.SmoothingMode
        g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
        Using b As New SolidBrush(MarkerColor)
            g.FillRectangle(b, left, y - thick \ 2, right - left, thick)
            ' Arrowheads at both ends, pointing into the gap.
            g.FillPolygon(b, {New Point(left - arrow, y - arrow), New Point(left, y), New Point(left - arrow, y + arrow)})
            g.FillPolygon(b, {New Point(right + arrow, y - arrow), New Point(right, y), New Point(right + arrow, y + arrow)})
            If label IsNot Nothing AndAlso font IsNot Nothing Then
                Dim size = TextRenderer.MeasureText(label, font)
                Dim pad = CInt(6 * scale)
                Dim box As New Rectangle((left + right - size.Width) \ 2 - pad, y - thick \ 2 - size.Height - pad,
                                         size.Width + 2 * pad, size.Height + pad)
                g.FillRectangle(b, box)
                TextRenderer.DrawText(g, label, font, box, Color.White,
                                      TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            End If
        End Using
        g.SmoothingMode = oldMode
    End Sub

End Module

Public Module ImageUtil

    ''' <summary>Copies a rendered page into a GDI+ bitmap.</summary>
    Public Function ToBitmap(page As RenderedPage) As Bitmap
        Dim bmp As New Bitmap(page.Width, page.Height, PixelFormat.Format32bppPArgb)
        Dim data = bmp.LockBits(New Rectangle(0, 0, page.Width, page.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb)
        Try
            Dim rowBytes = page.Width * 4
            For y = 0 To page.Height - 1
                Marshal.Copy(page.Bgra, y * rowBytes, data.Scan0 + y * data.Stride, rowBytes)
            Next
        Finally
            bmp.UnlockBits(data)
        End Try
        Return bmp
    End Function

End Module

''' <summary>Small per-user settings stored in %AppData%\Tumbler\settings.json.</summary>
Public Class AppSettings

    Public Property Author As String = Environment.UserName
    Public Property LastFolder As String = ""
    Public Property HighlightColor As Integer = Palette.ColorByName("Yellow").ToArgb()
    Public Property NoteColor As Integer = Palette.ColorByName("Yellow").ToArgb()
    Public Property TextBoxColor As Integer = Palette.ColorByName("Red").ToArgb()
    Public Property TextBoxFontSize As Double = 12
    Public Property CommentFontSize As Double = 11
    Public Property LineColor As Integer = Palette.ColorByName("Red").ToArgb()
    Public Property LineWidth As Double = 2

    Private Shared ReadOnly FilePath As String =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Tumbler", "settings.json")

    Public Shared Function Load() As AppSettings
        Try
            If File.Exists(FilePath) Then
                Return If(JsonSerializer.Deserialize(Of AppSettings)(File.ReadAllText(FilePath)), New AppSettings())
            End If
        Catch
            ' A damaged settings file is not worth bothering the user about.
        End Try
        Return New AppSettings()
    End Function

    Public Sub Save()
        Try
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath))
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Me, New JsonSerializerOptions With {.WriteIndented = True}))
        Catch
        End Try
    End Sub

End Class
