Imports System.Drawing.Drawing2D

''' <summary>
''' The toolbar pictures. They are embedded as strips (Icons\toolbar-NN.png, drawn by
''' tools\make-toolbar-icons.py) in several sizes, so they stay sharp at any Windows scaling.
''' </summary>
Public Module ToolbarIcons

    ''' <summary>The icons in each strip, left to right.</summary>
    Public ReadOnly Names As String() = {
        "open", "save", "undo", "redo", "prev", "next", "zoom-out", "zoom-in",
        "select", "highlight", "comment", "line", "thickness",
        "rotate-left", "rotate-right", "rotate-all", "delete-pages", "insert-pages"}

    Private ReadOnly Sizes As Integer() = {16, 20, 24, 30, 40}

    ''' <summary>The icon size for a screen: 20 px at 100% scaling, the nearest strip otherwise.</summary>
    Public Function SizeFor(dpi As Integer) As Integer
        Dim wanted = 20.0 * dpi / 96
        Return Sizes.OrderBy(Function(s) Math.Abs(s - wanted)).First()
    End Function

    ''' <summary>Cuts the strip for this size into one bitmap per icon name.</summary>
    Public Function Load(size As Integer) As Dictionary(Of String, Bitmap)
        Dim result As New Dictionary(Of String, Bitmap)
        Using stream = GetType(ToolbarIcons).Assembly.GetManifestResourceStream($"toolbar-{size}.png")
            If stream Is Nothing Then Return result
            Using strip As New Bitmap(stream)
                For i = 0 To Names.Length - 1
                    If (i + 1) * size > strip.Width Then Exit For
                    result(Names(i)) = strip.Clone(New Rectangle(i * size, 0, size, size), strip.PixelFormat)
                Next
            End Using
        End Using
        Return result
    End Function

    ''' <summary>A round dot of the current colour, sized to sit with the other icons.</summary>
    Public Function ColorDot(c As Color, size As Integer) As Bitmap
        Dim bmp As New Bitmap(size, size)
        Using g = Graphics.FromImage(bmp), fill As New SolidBrush(c),
              edge As New Pen(Color.FromArgb(110, 0, 0, 0), Math.Max(1.0F, size / 20.0F))
            g.Clear(Color.Transparent)
            g.SmoothingMode = SmoothingMode.AntiAlias
            Dim inset = size * 0.15F
            Dim r As New RectangleF(inset, inset, size - 2 * inset, size - 2 * inset)
            g.FillEllipse(fill, r)
            g.DrawEllipse(edge, r)
        End Using
        Return bmp
    End Function

End Module

''' <summary>A flat, light toolbar with soft blue hover and "on" states, instead of the old gradients.</summary>
Public NotInheritable Class FlatToolbarRenderer
    Inherits ToolStripProfessionalRenderer

    Public Sub New()
        MyBase.New(New FlatToolbarColors())
        RoundedEdges = False
    End Sub

    Protected Overrides Sub OnRenderToolStripBorder(e As ToolStripRenderEventArgs)
        ' Just a hairline under the toolbar.
        Using p As New Pen(FlatToolbarColors.Edge)
            e.Graphics.DrawLine(p, 0, e.ToolStrip.Height - 1, e.ToolStrip.Width, e.ToolStrip.Height - 1)
        End Using
    End Sub

    Private NotInheritable Class FlatToolbarColors
        Inherits ProfessionalColorTable

        Friend Shared ReadOnly Back As Color = Color.FromArgb(248, 249, 251)
        Friend Shared ReadOnly Edge As Color = Color.FromArgb(222, 226, 232)
        Private Shared ReadOnly Hover As Color = Color.FromArgb(232, 239, 253)
        Private Shared ReadOnly HoverEdge As Color = Color.FromArgb(190, 207, 245)
        Private Shared ReadOnly Pressed As Color = Color.FromArgb(208, 223, 252)
        Private Shared ReadOnly Checked As Color = Color.FromArgb(219, 231, 255)
        Private Shared ReadOnly CheckedEdge As Color = Color.FromArgb(47, 107, 255)

        Public Sub New()
            UseSystemColors = False
        End Sub

        Public Overrides ReadOnly Property ToolStripGradientBegin As Color = Back
        Public Overrides ReadOnly Property ToolStripGradientMiddle As Color = Back
        Public Overrides ReadOnly Property ToolStripGradientEnd As Color = Back
        Public Overrides ReadOnly Property ToolStripBorder As Color = Edge
        Public Overrides ReadOnly Property SeparatorDark As Color = Edge
        Public Overrides ReadOnly Property SeparatorLight As Color = Back
        Public Overrides ReadOnly Property ButtonSelectedHighlight As Color = Hover
        Public Overrides ReadOnly Property ButtonSelectedHighlightBorder As Color = HoverEdge
        Public Overrides ReadOnly Property ButtonSelectedGradientBegin As Color = Hover
        Public Overrides ReadOnly Property ButtonSelectedGradientMiddle As Color = Hover
        Public Overrides ReadOnly Property ButtonSelectedGradientEnd As Color = Hover
        Public Overrides ReadOnly Property ButtonSelectedBorder As Color = HoverEdge
        Public Overrides ReadOnly Property ButtonPressedHighlight As Color = Pressed
        Public Overrides ReadOnly Property ButtonPressedHighlightBorder As Color = CheckedEdge
        Public Overrides ReadOnly Property ButtonPressedGradientBegin As Color = Pressed
        Public Overrides ReadOnly Property ButtonPressedGradientMiddle As Color = Pressed
        Public Overrides ReadOnly Property ButtonPressedGradientEnd As Color = Pressed
        Public Overrides ReadOnly Property ButtonPressedBorder As Color = CheckedEdge
        Public Overrides ReadOnly Property ButtonCheckedHighlight As Color = Checked
        Public Overrides ReadOnly Property ButtonCheckedHighlightBorder As Color = CheckedEdge
        Public Overrides ReadOnly Property ButtonCheckedGradientBegin As Color = Checked
        Public Overrides ReadOnly Property ButtonCheckedGradientMiddle As Color = Checked
        Public Overrides ReadOnly Property ButtonCheckedGradientEnd As Color = Checked
        Public Overrides ReadOnly Property CheckBackground As Color = Checked
        Public Overrides ReadOnly Property CheckSelectedBackground As Color = Pressed
        Public Overrides ReadOnly Property CheckPressedBackground As Color = Pressed
    End Class

End Class
