Imports System.Drawing

''' <summary>The kinds of annotation Tumbler creates and knows how to edit.</summary>
Public Enum AnnotationKind
    Other
    Note        ' /Text: sticky note
    TextBox     ' /FreeText: text written on the page
    Highlight   ' /Highlight
    Comment     ' /FreeText drawn as plain coloured text (Tumbler's visible comment)
    Line        ' /Line
End Enum

''' <summary>A read-only snapshot of one annotation on a page.</summary>
Public NotInheritable Class AnnotationInfo

    ''' <summary>Zero-based page index.</summary>
    Public Property PageIndex As Integer

    ''' <summary>Position of the annotation in the page's /Annots array.</summary>
    Public Property AnnotIndex As Integer

    Public Property Kind As AnnotationKind
    Public Property Subtype As String
    Public Property Contents As String
    Public Property Author As String
    Public Property Color As Color
    Public Property Modified As Date?
    Public Property Rect As PdfRect

    ''' <summary>Where the annotation appears on screen, in display space (points).</summary>
    Public Property DisplayRect As RectangleF

    ''' <summary>For lines: the two ends in display space (points), and the stroke width.</summary>
    Public Property LineStart As PointF
    Public Property LineEnd As PointF
    Public Property LineWidth As Double

    ''' <summary>Font size for text boxes; 0 otherwise.</summary>
    Public Property FontSize As Double

    Public ReadOnly Property CanEdit As Boolean
        Get
            Return Kind <> AnnotationKind.Other
        End Get
    End Property

    Public ReadOnly Property KindName As String
        Get
            Select Case Kind
                Case AnnotationKind.Note : Return "Sticky note"
                Case AnnotationKind.Comment : Return "Comment"
                Case AnnotationKind.TextBox : Return "Text box"
                Case AnnotationKind.Highlight : Return "Highlight"
                Case AnnotationKind.Line : Return "Line"
                Case Else : Return Subtype
            End Select
        End Get
    End Property

End Class

''' <summary>Everything the UI needs to know about one page.</summary>
Public NotInheritable Class PageInfo

    Public Property Index As Integer
    Public Property Geometry As PageGeometry
    Public Property Annotations As IReadOnlyList(Of AnnotationInfo)

End Class

''' <summary>One character on a rendered page, in display space (points, top-left origin).</summary>
Public Structure TextChar

    Public Ch As Char
    Public Box As RectangleF

    Public ReadOnly Property IsVisible As Boolean
        Get
            Return Box.Width > 0.01F AndAlso Box.Height > 0.01F AndAlso Not Char.IsControl(Ch)
        End Get
    End Property

End Structure

''' <summary>A rendered page bitmap: 32-bit BGRA, opaque, top-down rows.</summary>
Public NotInheritable Class RenderedPage

    Public Property Width As Integer
    Public Property Height As Integer
    Public Property Bgra As Byte()

End Class

''' <summary>
''' A copied comment, line, sticky note or text box, kept in display space
''' (points, top-left origin) so it can be pasted on any page.
''' </summary>
Public NotInheritable Class AnnotationClip

    Public Property Kind As AnnotationKind
    Public Property Text As String
    Public Property Color As Color
    Public Property FontSize As Double
    Public Property LineWidth As Double
    ''' <summary>Where it sat on its page; lines also keep their two ends.</summary>
    Public Property Bounds As RectangleF
    Public Property LineStart As PointF
    Public Property LineEnd As PointF

    ''' <summary>True for the kinds that can be copied.</summary>
    Public Shared Function CanCopy(a As AnnotationInfo) As Boolean
        Return a IsNot Nothing AndAlso (a.Kind = AnnotationKind.Comment OrElse a.Kind = AnnotationKind.Line OrElse
                                        a.Kind = AnnotationKind.Note OrElse a.Kind = AnnotationKind.TextBox)
    End Function

    Public Shared Function From(a As AnnotationInfo) As AnnotationClip
        If Not CanCopy(a) Then Throw New InvalidOperationException("Only comments, lines, sticky notes and text boxes can be copied.")
        Dim bounds = a.DisplayRect
        If a.Kind = AnnotationKind.Line Then
            bounds = RectangleF.FromLTRB(Math.Min(a.LineStart.X, a.LineEnd.X), Math.Min(a.LineStart.Y, a.LineEnd.Y),
                                         Math.Max(a.LineStart.X, a.LineEnd.X), Math.Max(a.LineStart.Y, a.LineEnd.Y))
        End If
        Return New AnnotationClip With {
            .Kind = a.Kind, .Text = a.Contents, .Color = a.Color, .FontSize = a.FontSize,
            .LineWidth = a.LineWidth, .Bounds = bounds, .LineStart = a.LineStart, .LineEnd = a.LineEnd}
    End Function

End Class
