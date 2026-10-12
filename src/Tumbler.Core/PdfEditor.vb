Imports System.Drawing
Imports System.IO
Imports PdfSharp.Pdf
Imports PdfSharp.Pdf.Advanced
Imports PdfSharp.Pdf.IO

''' <summary>
''' The open document. The PDF is held in memory as bytes; every edit opens
''' those bytes with PDFsharp, changes them, and saves new bytes. That keeps
''' the original file untouched until the user saves, and gives undo/redo
''' for free (each step is just the previous bytes).
''' </summary>
Public NotInheritable Class PdfEditor

    Private Const MaxUndo As Integer = 30

    Private _bytes As Byte()
    Private _pages As IReadOnlyList(Of PageInfo)
    Private ReadOnly _undo As New List(Of Byte())
    Private ReadOnly _redo As New List(Of Byte())
    Private _savedBytes As Byte()

    ''' <summary>Raised after any change to the document (edit, undo, redo).</summary>
    Public Event Changed As EventHandler

    Private Sub New(bytes As Byte(), path As String)
        _bytes = bytes
        _savedBytes = bytes
        FilePath = path
        _pages = ReadPages(bytes)
    End Sub

#Region "Opening and saving"

    ''' <summary>Opens a PDF file. Throws <see cref="PdfPasswordException"/> when a password is needed.</summary>
    Public Shared Function Open(path As String, Optional password As String = Nothing) As PdfEditor
        Dim original = File.ReadAllBytes(path)
        Dim bytes = Normalize(original, password)
        Return New PdfEditor(bytes, path) With {.WasDecrypted = Not String.IsNullOrEmpty(password)}
    End Function

    ''' <summary>Opens a PDF held in memory (used by tests).</summary>
    Public Shared Function Open(bytes As Byte(), Optional password As String = Nothing) As PdfEditor
        Return New PdfEditor(Normalize(bytes, password), Nothing)
    End Function

    ''' <summary>
    ''' Round-trips the file through PDFsharp once, so the bytes we render are
    ''' exactly the bytes we edit, and any password protection is removed.
    ''' </summary>
    Private Shared Function Normalize(bytes As Byte(), password As String) As Byte()
        Try
            Return RoundTrip(bytes, password)
        Catch ex As PdfPasswordException
            ' The file can be read but is locked against editing (common for
            ' government forms), or was opened with its "open" password only.
            ' PDFsharp will not modify it, but PDFium can write an unlocked copy,
            ' and failing that PDFsharp can copy the pages into a new document.
            ' If even reading needs a password, ask for one.
            Dim unlocked = PdfiumNative.Rewrite(bytes, password)
            If unlocked IsNot Nothing Then
                Try
                    Return RoundTrip(unlocked, Nothing)
                Catch
                End Try
            End If
            Try
                Return CopyPagesToNewDocument(bytes, password)
            Catch
                Throw ex
            End Try
        Catch ex As Exception
            ' PDFsharp is strict about damaged files that Acrobat and browsers
            ' open without complaint. Let PDFium repair the file, then try again.
            Dim repaired = PdfiumNative.Rewrite(bytes, password)
            If repaired Is Nothing Then
                Throw New InvalidDataException("This file is not a PDF, or it is too damaged to open.", ex)
            End If
            Return RoundTrip(repaired, Nothing)
        End Try
    End Function

    Private Shared Function RoundTrip(bytes As Byte(), password As String) As Byte()
        Using doc = OpenForEdit(bytes, password)
            If doc.SecuritySettings.IsEncrypted Then
                If doc.SecurityHandler Is Nothing Then Throw New PdfPasswordException("This PDF is locked against editing.")
                doc.SecurityHandler.SetEncryptionToNoneAndResetPasswords()
            End If
            Return SaveToBytes(doc)
        End Using
    End Function

    Private Shared Function CopyPagesToNewDocument(bytes As Byte(), password As String) As Byte()
        Using src = OpenForImport(bytes, password), copy As New PdfDocument()
            For Each page As PdfPage In src.Pages
                copy.AddPage(page)
            Next
            Return SaveToBytes(copy)
        End Using
    End Function

    Public Property FilePath As String

    ''' <summary>True when the source file was password protected and has been opened without protection.</summary>
    Public Property WasDecrypted As Boolean

    Public ReadOnly Property Bytes As Byte()
        Get
            Return _bytes
        End Get
    End Property

    Public ReadOnly Property IsDirty As Boolean
        Get
            Return _bytes IsNot _savedBytes
        End Get
    End Property

    Public Sub SaveAs(path As String)
        ' Write to a temp file first so a failure never leaves a half-written PDF.
        Dim temp = path & ".tumbler-tmp"
        File.WriteAllBytes(temp, _bytes)
        If File.Exists(path) Then
            File.Replace(temp, path, Nothing)
        Else
            File.Move(temp, path)
        End If
        FilePath = path
        _savedBytes = _bytes
        RaiseEvent Changed(Me, EventArgs.Empty)
    End Sub

#End Region

#Region "Pages and annotations (read side)"

    Public ReadOnly Property PageCount As Integer
        Get
            Return _pages.Count
        End Get
    End Property

    Public ReadOnly Property Pages As IReadOnlyList(Of PageInfo)
        Get
            Return _pages
        End Get
    End Property

    Public Function AllAnnotations() As IEnumerable(Of AnnotationInfo)
        Return _pages.SelectMany(Function(p) p.Annotations)
    End Function

    Private Shared Function ReadPages(bytes As Byte()) As IReadOnlyList(Of PageInfo)
        Dim list As New List(Of PageInfo)
        Using ms As New MemoryStream(bytes, False)
            Using doc = PdfReader.Open(ms, PdfDocumentOpenMode.Import)
                For i = 0 To doc.PageCount - 1
                    Dim page = doc.Pages(i)
                    Dim geo = GetGeometry(page)
                    list.Add(New PageInfo With {.Index = i, .Geometry = geo, .Annotations = ReadAnnotations(page, i)})
                Next
            End Using
        End Using
        Return list
    End Function

    Friend Shared Function GetGeometry(page As PdfPage) As PageGeometry
        ' PDFium shows the CropBox clipped to the MediaBox, so we do the same.
        Dim media = AnnotationWriter.ReadNumbers(page.Elements.GetArray("/MediaBox"))
        If media.Count < 4 Then media = New List(Of Double) From {0, 0, 612, 792}
        Dim box = PdfRect.FromCorners(media(0), media(1), media(2), media(3))
        Dim crop = AnnotationWriter.ReadNumbers(page.Elements.GetArray("/CropBox"))
        If crop.Count >= 4 Then
            Dim c = PdfRect.FromCorners(crop(0), crop(1), crop(2), crop(3))
            Dim clipped = PdfRect.FromCorners(Math.Max(box.X1, c.X1), Math.Max(box.Y1, c.Y1),
                                              Math.Min(box.X2, c.X2), Math.Min(box.Y2, c.Y2))
            If clipped.Width > 0 AndAlso clipped.Height > 0 Then box = clipped
        End If
        Return New PageGeometry(box.X1, box.Y1, box.X2, box.Y2, page.Elements.GetInteger("/Rotate"))
    End Function

    Private Shared Function ReadAnnotations(page As PdfPage, pageIndex As Integer) As IReadOnlyList(Of AnnotationInfo)
        Dim list As New List(Of AnnotationInfo)
        Dim geo = GetGeometry(page)
        Dim annots = page.Elements.GetArray("/Annots")
        If annots Is Nothing Then Return list
        For i = 0 To annots.Elements.Count - 1
            Dim dict = ResolveDict(annots.Elements(i))
            If dict Is Nothing Then Continue For
            Dim subtype = dict.Elements.GetName("/Subtype")
            ' Popups and links are plumbing, not comments.
            If subtype = "/Popup" OrElse subtype = "/Link" OrElse subtype = "/Widget" Then Continue For

            Dim info As New AnnotationInfo With {
                .PageIndex = pageIndex,
                .AnnotIndex = i,
                .Subtype = subtype.TrimStart("/"c),
                .Contents = dict.Elements.GetString("/Contents"),
                .Author = dict.Elements.GetString("/T"),
                .Rect = AnnotationWriter.ReadRect(dict),
                .Modified = AnnotationWriter.ParsePdfDate(dict.Elements.GetString("/M"))}
            info.DisplayRect = DisplayBounds(geo, info.Rect, dict.Elements.GetInteger("/F"))

            Select Case subtype
                Case "/Text"
                    info.Kind = AnnotationKind.Note
                    info.Color = AnnotationWriter.ReadColor(dict, Color.Gold)
                Case "/FreeText"
                    Dim own = AnnotationWriter.ReadNumbers(dict.Elements.GetArray(AnnotationWriter.CommentColorKey))
                    If own.Count = 3 Then
                        info.Kind = AnnotationKind.Comment
                        info.Color = Color.FromArgb(CInt(own(0) * 255), CInt(own(1) * 255), CInt(own(2) * 255))
                    Else
                        info.Kind = AnnotationKind.TextBox
                        info.Color = AnnotationWriter.ReadDaColor(dict, Color.Black)
                    End If
                    info.FontSize = AnnotationWriter.ReadFontSize(dict)
                Case "/Line"
                    info.Kind = AnnotationKind.Line
                    info.Color = AnnotationWriter.ReadColor(dict, Color.Black)
                    info.LineWidth = AnnotationWriter.ReadLineWidth(dict)
                    Dim ends = AnnotationWriter.ReadNumbers(dict.Elements.GetArray("/L"))
                    If ends.Count >= 4 Then
                        info.LineStart = geo.ToDisplay(ends(0), ends(1))
                        info.LineEnd = geo.ToDisplay(ends(2), ends(3))
                    End If
                Case "/Highlight"
                    info.Kind = AnnotationKind.Highlight
                    info.Color = AnnotationWriter.ReadColor(dict, Color.Yellow)
                Case Else
                    info.Kind = AnnotationKind.Other
                    info.Color = AnnotationWriter.ReadColor(dict, Color.Gray)
            End Select
            list.Add(info)
        Next
        Return list
    End Function

    ''' <summary>Screen position of an annotation, allowing for the NoRotate flag (sticky notes).</summary>
    Private Shared Function DisplayBounds(geo As PageGeometry, rect As PdfRect, flags As Integer) As RectangleF
        Const NoRotate = 16
        If (flags And NoRotate) <> 0 AndAlso geo.Rotation <> 0 Then
            Dim corner = geo.ToDisplay(rect.X1, rect.Y2)
            Return New RectangleF(corner.X, corner.Y, CSng(rect.Width), CSng(rect.Height))
        End If
        Return geo.ToDisplayRect(rect)
    End Function

    Private Shared Function ResolveDict(item As PdfItem) As PdfDictionary
        Dim ref = TryCast(item, PdfReference)
        Return TryCast(If(ref IsNot Nothing, ref.Value, item), PdfDictionary)
    End Function

#End Region

#Region "Page operations"

    ''' <summary>Deletes the given pages (zero-based). At least one page must remain.</summary>
    Public Sub DeletePages(pageIndexes As IEnumerable(Of Integer))
        Dim targets = pageIndexes.Distinct().Where(Function(i) i >= 0 AndAlso i < PageCount).OrderByDescending(Function(i) i).ToList()
        If targets.Count = 0 Then Return
        If targets.Count >= PageCount Then Throw New InvalidOperationException("A PDF must keep at least one page.")
        Edit(Sub(doc)
                 For Each i In targets
                     doc.Pages.RemoveAt(i)
                 Next
             End Sub)
    End Sub

    ''' <summary>Rotates pages by a multiple of 90 degrees (positive = clockwise).</summary>
    Public Sub RotatePages(pageIndexes As IEnumerable(Of Integer), degrees As Integer)
        Dim targets = pageIndexes.Distinct().Where(Function(i) i >= 0 AndAlso i < PageCount).ToList()
        If targets.Count = 0 OrElse PageGeometry.NormalizeRotation(degrees) = 0 Then Return
        Edit(Sub(doc)
                 For Each i In targets
                     Dim page = doc.Pages(i)
                     Dim current = page.Elements.GetInteger("/Rotate")
                     page.Elements.SetInteger("/Rotate", PageGeometry.NormalizeRotation(current + degrees))
                 Next
             End Sub)
    End Sub

    Public Sub RotateAllPages(degrees As Integer)
        RotatePages(Enumerable.Range(0, PageCount), degrees)
    End Sub

    ''' <summary>Number of pages in another PDF file.</summary>
    Public Shared Function CountPages(path As String, Optional password As String = Nothing) As Integer
        Using doc = OpenForImport(Normalize(File.ReadAllBytes(path), password), Nothing)
            Return doc.PageCount
        End Using
    End Function

    ''' <summary>
    ''' Inserts pages from another PDF. <paramref name="sourcePages"/> are zero-based
    ''' page numbers in that file; <paramref name="insertAt"/> is the zero-based
    ''' position in this document (PageCount = append at the end).
    ''' </summary>
    Public Sub InsertPagesFrom(path As String, sourcePages As IEnumerable(Of Integer), insertAt As Integer,
                               Optional password As String = Nothing)
        ' Unlocked and repaired the same way as an opened file.
        Dim sourceBytes = Normalize(File.ReadAllBytes(path), password)
        Dim wanted = sourcePages.ToList()
        insertAt = Math.Max(0, Math.Min(insertAt, PageCount))
        Edit(Sub(doc)
                 Using src = OpenForImport(sourceBytes, Nothing)
                     Dim position = insertAt
                     For Each p In wanted
                         If p < 0 OrElse p >= src.PageCount Then
                             Throw New ArgumentOutOfRangeException(NameOf(sourcePages), $"Page {p + 1} does not exist in that file (it has {src.PageCount}).")
                         End If
                         doc.Pages.Insert(position, src.Pages(p))
                         position += 1
                     Next
                 End Using
             End Sub)
    End Sub

#End Region

#Region "Comments and highlights"

    ''' <summary>Adds a sticky note. <paramref name="displayPoint"/> is in display space (points).</summary>
    Public Sub AddNote(pageIndex As Integer, displayPoint As PointF, text As String, color As Color, author As String)
        Edit(Sub(doc)
                 Dim page = doc.Pages(pageIndex)
                 AddAnnotation(doc, page, AnnotationWriter.CreateNote(doc, GetGeometry(page), displayPoint, text, color, author))
             End Sub)
    End Sub

    ''' <summary>
    ''' Adds a visible comment box whose top-left corner is at <paramref name="displayPoint"/>
    ''' (display space, points). The box is sized to fit the text.
    ''' </summary>
    Public Sub AddComment(pageIndex As Integer, displayPoint As PointF, text As String, color As Color,
                          fontSize As Double, author As String)
        Edit(Sub(doc)
                 Dim page = doc.Pages(pageIndex)
                 AddAnnotation(doc, page, AnnotationWriter.CreateComment(doc, GetGeometry(page), displayPoint, text, color, fontSize, author))
             End Sub)
    End Sub

    ''' <summary>Adds text written directly on the page inside a box (display space, points).</summary>
    Public Sub AddTextBox(pageIndex As Integer, displayRect As RectangleF, text As String, color As Color,
                          fontSize As Double, author As String)
        Edit(Sub(doc)
                 Dim page = doc.Pages(pageIndex)
                 AddAnnotation(doc, page, AnnotationWriter.CreateTextBox(doc, GetGeometry(page), displayRect, text, color, fontSize, author))
             End Sub)
    End Sub

    ''' <summary>Adds a straight line between two points (display space, points).</summary>
    Public Sub AddLine(pageIndex As Integer, fromPoint As PointF, toPoint As PointF, color As Color,
                       width As Double, author As String)
        If fromPoint = toPoint Then Return
        Edit(Sub(doc)
                 Dim page = doc.Pages(pageIndex)
                 AddAnnotation(doc, page, AnnotationWriter.CreateLine(doc, GetGeometry(page), fromPoint, toPoint, color, width, author))
             End Sub)
    End Sub

    ''' <summary>Adds a highlight covering the given line quads (display space, points).</summary>
    Public Sub AddHighlight(pageIndex As Integer, displayQuads As IEnumerable(Of HighlightQuad), color As Color,
                            author As String, Optional note As String = "")
        Dim quads = displayQuads.Where(Function(q) q.Bounds.Width > 0 AndAlso q.Bounds.Height > 0).ToList()
        If quads.Count = 0 Then Return
        Edit(Sub(doc)
                 Dim page = doc.Pages(pageIndex)
                 AddAnnotation(doc, page, AnnotationWriter.CreateHighlight(doc, GetGeometry(page), quads, color, author, note))
             End Sub)
    End Sub

    ''' <summary>
    ''' Adds a copy of a copied annotation to a page, with its top-left corner at
    ''' <paramref name="displayTopLeft"/> (display space, points), kept on the page.
    ''' The new annotation is the last one in that page's list.
    ''' </summary>
    Public Sub PasteAnnotation(clip As AnnotationClip, pageIndex As Integer, displayTopLeft As PointF, author As String)
        Dim geo = _pages(pageIndex).Geometry
        Dim b = clip.Bounds
        Dim x = Math.Max(0, Math.Min(displayTopLeft.X, geo.DisplayWidth - b.Width))
        Dim y = Math.Max(0, Math.Min(displayTopLeft.Y, geo.DisplayHeight - b.Height))
        Dim dx = CSng(x - b.X), dy = CSng(y - b.Y)
        Dim topLeft As New PointF(CSng(x), CSng(y))
        Select Case clip.Kind
            Case AnnotationKind.Comment
                AddComment(pageIndex, topLeft, clip.Text, clip.Color, If(clip.FontSize > 0, clip.FontSize, 11), author)
            Case AnnotationKind.Line
                AddLine(pageIndex, New PointF(clip.LineStart.X + dx, clip.LineStart.Y + dy),
                        New PointF(clip.LineEnd.X + dx, clip.LineEnd.Y + dy), clip.Color, If(clip.LineWidth > 0, clip.LineWidth, 2), author)
            Case AnnotationKind.Note
                AddNote(pageIndex, topLeft, clip.Text, clip.Color, author)
            Case AnnotationKind.TextBox
                AddTextBox(pageIndex, New RectangleF(topLeft, b.Size), clip.Text, clip.Color, If(clip.FontSize > 0, clip.FontSize, 12), author)
            Case Else
                Throw New InvalidOperationException("Only comments, lines, sticky notes and text boxes can be pasted.")
        End Select
    End Sub

    ''' <summary>True for the kinds that can be dragged to a new place.</summary>
    Public Shared Function CanMove(a As AnnotationInfo) As Boolean
        Return a IsNot Nothing AndAlso (a.Kind = AnnotationKind.Comment OrElse a.Kind = AnnotationKind.Line OrElse
                                        a.Kind = AnnotationKind.Note OrElse a.Kind = AnnotationKind.TextBox)
    End Function

    ''' <summary>Where an annotation sits on screen; for lines, the box around the two ends.</summary>
    Public Shared Function MoveBounds(a As AnnotationInfo) As RectangleF
        If a.Kind = AnnotationKind.Line Then
            Return RectangleF.FromLTRB(Math.Min(a.LineStart.X, a.LineEnd.X), Math.Min(a.LineStart.Y, a.LineEnd.Y),
                                       Math.Max(a.LineStart.X, a.LineEnd.X), Math.Max(a.LineStart.Y, a.LineEnd.Y))
        End If
        Return a.DisplayRect
    End Function

    ''' <summary>
    ''' The offset actually used when moving by (<paramref name="dx"/>, <paramref name="dy"/>)
    ''' display points: reduced so the annotation stays on its page.
    ''' </summary>
    Public Function ClampMove(a As AnnotationInfo, dx As Single, dy As Single) As SizeF
        Dim geo = _pages(a.PageIndex).Geometry
        Dim b = MoveBounds(a)
        Dim x = Math.Max(0, Math.Min(b.X + dx, geo.DisplayWidth - b.Width))
        Dim y = Math.Max(0, Math.Min(b.Y + dy, geo.DisplayHeight - b.Height))
        Return New SizeF(CSng(x - b.X), CSng(y - b.Y))
    End Function

    ''' <summary>Moves a comment, line, sticky note or text box by an offset in display points.</summary>
    Public Sub MoveAnnotation(annotation As AnnotationInfo, dx As Single, dy As Single)
        If Not CanMove(annotation) Then Throw New InvalidOperationException("Only comments, lines, sticky notes and text boxes can be moved.")
        Dim d = ClampMove(annotation, dx, dy)
        If d.Width = 0 AndAlso d.Height = 0 Then Return
        Edit(Sub(doc)
                 Dim page = doc.Pages(annotation.PageIndex)
                 Dim geo = GetGeometry(page)
                 ' The display-to-PDF mapping is linear, so an offset maps like a point minus the origin.
                 Dim o = geo.ToPdf(0, 0), m = geo.ToPdf(d.Width, d.Height)
                 AnnotationWriter.Move(doc, FindAnnotation(page, annotation), m.X - o.X, m.Y - o.Y)
             End Sub)
    End Sub

    ''' <summary>Changes the text and colour of a note, text box or highlight.</summary>
    Public Sub UpdateAnnotation(annotation As AnnotationInfo, text As String, color As Color, Optional fontSize As Double = 0)
        If Not annotation.CanEdit Then Throw New InvalidOperationException("Tumbler can only edit its own kinds of comments.")
        Edit(Sub(doc)
                 Dim page = doc.Pages(annotation.PageIndex)
                 Dim dict = FindAnnotation(page, annotation)
                 AnnotationWriter.Update(doc, GetGeometry(page), dict, annotation.Kind, text, color, fontSize)
             End Sub)
    End Sub

    ''' <summary>Removes an annotation, and its popup window if it has one.</summary>
    Public Sub DeleteAnnotation(annotation As AnnotationInfo)
        Edit(Sub(doc)
                 Dim page = doc.Pages(annotation.PageIndex)
                 Dim dict = FindAnnotation(page, annotation)
                 Dim annots = page.Elements.GetArray("/Annots")
                 Dim popup = ResolveDict(dict.Elements("/Popup"))
                 For i = annots.Elements.Count - 1 To 0 Step -1
                     Dim d = ResolveDict(annots.Elements(i))
                     If d Is dict OrElse (popup IsNot Nothing AndAlso d Is popup) Then annots.Elements.RemoveAt(i)
                 Next
             End Sub)
    End Sub

    Private Shared Function FindAnnotation(page As PdfPage, annotation As AnnotationInfo) As PdfDictionary
        Dim annots = page.Elements.GetArray("/Annots")
        If annots Is Nothing OrElse annotation.AnnotIndex >= annots.Elements.Count Then
            Throw New InvalidOperationException("That comment no longer exists.")
        End If
        Return ResolveDict(annots.Elements(annotation.AnnotIndex))
    End Function

    Private Shared Sub AddAnnotation(doc As PdfDocument, page As PdfPage, annot As PdfDictionary)
        doc.Internals.AddObject(annot)
        annot.Elements.SetReference("/P", page)
        Dim annots = page.Elements.GetArray("/Annots")
        If annots Is Nothing Then
            annots = New PdfArray(doc)
            page.Elements("/Annots") = annots
        End If
        annots.Elements.Add(annot.Reference)
    End Sub

#End Region

#Region "Undo / redo"

    Public ReadOnly Property CanUndo As Boolean
        Get
            Return _undo.Count > 0
        End Get
    End Property

    Public ReadOnly Property CanRedo As Boolean
        Get
            Return _redo.Count > 0
        End Get
    End Property

    Public Sub Undo()
        If Not CanUndo Then Return
        _redo.Add(_bytes)
        SetBytes(Pop(_undo))
    End Sub

    Public Sub Redo()
        If Not CanRedo Then Return
        _undo.Add(_bytes)
        SetBytes(Pop(_redo))
    End Sub

    Private Shared Function Pop(list As List(Of Byte())) As Byte()
        Dim last = list(list.Count - 1)
        list.RemoveAt(list.Count - 1)
        Return last
    End Function

#End Region

#Region "Plumbing"

    Private Sub Edit(change As Action(Of PdfDocument))
        Dim newBytes As Byte()
        Using doc = OpenForEdit(_bytes, Nothing)
            change(doc)
            newBytes = SaveToBytes(doc)
        End Using
        Dim newPages = ReadPages(newBytes)   ' validate before committing
        _undo.Add(_bytes)
        If _undo.Count > MaxUndo Then _undo.RemoveAt(0)
        _redo.Clear()
        _bytes = newBytes
        _pages = newPages
        RaiseEvent Changed(Me, EventArgs.Empty)
    End Sub

    Private Sub SetBytes(bytes As Byte())
        _bytes = bytes
        _pages = ReadPages(bytes)
        RaiseEvent Changed(Me, EventArgs.Empty)
    End Sub

    Private Shared Function OpenForEdit(bytes As Byte(), password As String) As PdfDocument
        Return OpenWith(bytes, password, PdfDocumentOpenMode.Modify)
    End Function

    Private Shared Function OpenForImport(bytes As Byte(), password As String) As PdfDocument
        Return OpenWith(bytes, password, PdfDocumentOpenMode.Import)
    End Function

    Private Shared Function OpenWith(bytes As Byte(), password As String, mode As PdfDocumentOpenMode) As PdfDocument
        Dim ms As New MemoryStream(bytes, False)
        Try
            Dim doc As PdfDocument
            If String.IsNullOrEmpty(password) Then
                doc = PdfReader.Open(ms, mode, AddressOf NoPassword)
            Else
                doc = PdfReader.Open(ms, password, mode)
            End If
            ' PDFsharp returns nothing (rather than throwing) when a password was needed.
            If doc Is Nothing Then Throw New PdfPasswordException("This PDF is protected by a password.")
            Return doc
        Catch ex As PdfPasswordException
            Throw
        Catch ex As PdfReaderException When ex.Message.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0
            Throw New PdfPasswordException(ex.Message, ex)
        End Try
    End Function

    Private Shared Sub NoPassword(args As PdfPasswordProviderArgs)
        args.Abort = True
    End Sub

    Private Shared Function SaveToBytes(doc As PdfDocument) As Byte()
        Using ms As New MemoryStream()
            doc.Save(ms, False)
            Return ms.ToArray()
        End Using
    End Function

#End Region

End Class

''' <summary>Thrown when a PDF needs a password (or the one given is wrong).</summary>
Public Class PdfPasswordException
    Inherits Exception

    Public Sub New(message As String, Optional inner As Exception = Nothing)
        MyBase.New(message, inner)
    End Sub
End Class
