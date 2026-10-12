Imports System.IO

''' <summary>The main window: menus, toolbar, page strip, page view and comments list.</summary>
Public Class MainForm
    Inherits Form

    Private ReadOnly _settings As AppSettings = AppSettings.Load()
    Private _editor As PdfEditor
    Private _renderer As PdfRenderer

    ''' <summary>File passed on the command line (or by "Open with").</summary>
    Public Property FileToOpenOnStart As String

    ' Layout
    Private ReadOnly _menu As New MenuStrip()
    Private ReadOnly _toolbar As New ToolStrip With {.GripStyle = ToolStripGripStyle.Hidden, .ImageScalingSize = New Size(16, 16)}
    Private ReadOnly _status As New StatusStrip()
    Private ReadOnly _statusFile As New ToolStripStatusLabel With {.Spring = True, .TextAlign = ContentAlignment.MiddleLeft}
    Private ReadOnly _statusHint As New ToolStripStatusLabel()
    Private ReadOnly _outerSplit As New SplitContainer With {.Dock = DockStyle.Fill, .FixedPanel = FixedPanel.Panel1}
    Private ReadOnly _innerSplit As New SplitContainer With {.Dock = DockStyle.Fill, .FixedPanel = FixedPanel.Panel2}
    Private ReadOnly _thumbs As New ThumbnailView With {.Dock = DockStyle.Fill}
    Private ReadOnly _view As New DocumentView With {.Dock = DockStyle.Fill}
    Private ReadOnly _comments As New ListView With {
        .Dock = DockStyle.Fill, .View = View.Details, .FullRowSelect = True, .MultiSelect = False, .HideSelection = False}
    Private ReadOnly _swatches As New ImageList With {.ColorDepth = ColorDepth.Depth32Bit}

    ' Toolbar items that change state
    Private ReadOnly _tbSave As New ToolStripButton("Save")
    Private ReadOnly _tbUndo As New ToolStripButton("Undo")
    Private ReadOnly _tbRedo As New ToolStripButton("Redo")
    Private ReadOnly _tbPage As New ToolStripTextBox With {.Width = 40, .TextBoxTextAlign = HorizontalAlignment.Center}
    Private ReadOnly _tbPageCount As New ToolStripLabel("/ 0")
    Private ReadOnly _tbZoom As New ToolStripComboBox With {.Width = 70}
    Private ReadOnly _tbSelect As New ToolStripButton("Select") With {.CheckOnClick = False}
    Private ReadOnly _tbHighlight As New ToolStripButton("Highlight")
    Private ReadOnly _tbNote As New ToolStripButton("Comment")
    Private ReadOnly _tbLine As New ToolStripButton("Line")
    Private ReadOnly _tbWidth As New ToolStripDropDownButton("Thickness") With {.Visible = False}
    Private ReadOnly _tbColor As New ToolStripDropDownButton("Colour")
    Private ReadOnly _docItems As New List(Of ToolStripItem)   ' enabled only with a document open

    ' Menu items that change state
    Private _miSave, _miSaveAs, _miClose, _miUndo, _miRedo As ToolStripMenuItem
    Private _miThumbs, _miComments As ToolStripMenuItem

    Public Sub New()
        SuspendLayout()
        AutoScaleDimensions = New SizeF(96.0F, 96.0F)
        AutoScaleMode = AutoScaleMode.Dpi
        Font = New Font("Segoe UI", 9.0F)
        Text = "Tumbler"
        Using iconStream = GetType(MainForm).Assembly.GetManifestResourceStream("Tumbler.ico")
            If iconStream IsNot Nothing Then Icon = New Icon(iconStream)
        End Using
        Size = New Size(1280, 860)
        StartPosition = FormStartPosition.CenterScreen
        KeyPreview = True

        BuildMenu()
        BuildToolbar()
        BuildPanels()
        _status.Items.AddRange({_statusFile, _statusHint})

        Controls.Add(_outerSplit)
        Controls.Add(_toolbar)
        Controls.Add(_menu)
        Controls.Add(_status)
        MainMenuStrip = _menu
        ResumeLayout(True)

        AddHandler _view.CurrentPageChanged, AddressOf OnViewPageChanged
        AddHandler _view.ZoomChanged, Sub() UpdateZoomBox()
        AddHandler _view.HighlightRequested, AddressOf OnHighlightRequested
        AddHandler _view.NoteRequested, AddressOf OnNoteRequested
        AddHandler _view.LineRequested, AddressOf OnLineRequested
        AddHandler _view.AnnotationActivated, Sub(s, e) EditAnnotation(e.Annotation)
        AddHandler _view.MenuRequested, AddressOf OnViewMenuRequested
        AddHandler _view.CopyRequested, Sub() CopySelectedAnnotation()
        AddHandler _view.PasteRequested, Sub() PasteAnnotation()
        AddHandler _view.DeleteAnnotationRequested, Sub() DeleteSelectedAnnotation()
        AddHandler _thumbs.PageClicked, Sub() _view.ScrollToPage(_thumbs.CurrentPage)
        AddHandler _thumbs.DeleteRequested, Sub() DeleteSelectedPages()
        AddHandler _thumbs.SelectionChanged, Sub() UpdateHint()

        For Each target In New Control() {Me, _view, _thumbs, _comments, _outerSplit, _innerSplit,
                                        _outerSplit.Panel1, _outerSplit.Panel2, _innerSplit.Panel1, _innerSplit.Panel2, _toolbar, _status}
            EnableFileDrop(target)
        Next

        SetTool(ToolMode.Select)
        UpdateUi()
    End Sub

#Region "Building the window"

    Private Function Item(text As String, handler As Action, Optional keys As Keys = Keys.None, Optional needsDoc As Boolean = True) As ToolStripMenuItem
        Dim mi As New ToolStripMenuItem(text)
        If keys <> Keys.None Then mi.ShortcutKeys = keys
        AddHandler mi.Click, Sub() handler()
        If needsDoc Then _docItems.Add(mi)
        Return mi
    End Function

    Private Sub BuildMenu()
        Dim file As New ToolStripMenuItem("&File")
        _miSave = Item("&Save", AddressOf SaveDocument, Keys.Control Or Keys.S)
        _miSaveAs = Item("Save &As…", Sub() SaveDocumentAs(), Keys.Control Or Keys.Shift Or Keys.S)
        _miClose = Item("&Close", Sub() CloseDocument(), Keys.Control Or Keys.W)
        file.DropDownItems.AddRange({
            Item("&Open…", AddressOf OpenDocumentDialog, Keys.Control Or Keys.O, needsDoc:=False),
            _miSave, _miSaveAs, _miClose,
            New ToolStripSeparator(),
            Item("E&xit", AddressOf Close, needsDoc:=False)})

        Dim edit As New ToolStripMenuItem("&Edit")
        _miUndo = Item("&Undo", AddressOf UndoEdit, Keys.Control Or Keys.Z)
        _miRedo = Item("&Redo", AddressOf RedoEdit, Keys.Control Or Keys.Y)
        ' Ctrl+C / Ctrl+V are handled by the page view itself, so they keep working
        ' normally in text fields; the menu only shows them.
        Dim copyItem = Item("&Copy comment or line", Sub() CopySelectedAnnotation())
        copyItem.ShortcutKeyDisplayString = "Ctrl+C"
        Dim pasteItem = Item("&Paste", Sub() PasteAnnotation())
        pasteItem.ShortcutKeyDisplayString = "Ctrl+V"
        edit.DropDownItems.AddRange({_miUndo, _miRedo, New ToolStripSeparator(), copyItem, pasteItem})

        Dim view As New ToolStripMenuItem("&View")
        _miThumbs = New ToolStripMenuItem("Page &thumbnails") With {.Checked = True, .CheckOnClick = True}
        AddHandler _miThumbs.CheckedChanged, Sub() _outerSplit.Panel1Collapsed = Not _miThumbs.Checked
        _miComments = New ToolStripMenuItem("&Comments list") With {.Checked = True, .CheckOnClick = True}
        AddHandler _miComments.CheckedChanged, Sub() _innerSplit.Panel2Collapsed = Not _miComments.Checked
        view.DropDownItems.AddRange({
            Item("Zoom &in", Sub() _view.Zoom *= 1.25, Keys.Control Or Keys.Oemplus),
            Item("Zoom &out", Sub() _view.Zoom /= 1.25, Keys.Control Or Keys.OemMinus),
            Item("&Actual size", Sub() _view.Zoom = 1, Keys.Control Or Keys.D0),
            Item("Fit &width", Sub() _view.Zoom = _view.FitWidthZoom(), Keys.Control Or Keys.D2),
            New ToolStripSeparator(),
            _miThumbs, _miComments})

        Dim pages As New ToolStripMenuItem("&Pages")
        pages.DropDownItems.AddRange({
            Item("Rotate selected pages &left", Sub() RotateSelected(-90), Keys.Control Or Keys.Shift Or Keys.OemMinus),
            Item("Rotate selected pages &right", Sub() RotateSelected(90), Keys.Control Or Keys.Shift Or Keys.Oemplus),
            Item("Rotate &all pages left", Sub() RotateAll(-90)),
            Item("Rotate a&ll pages right", Sub() RotateAll(90)),
            Item("Rotate all pages &180°", Sub() RotateAll(180)),
            New ToolStripSeparator(),
            Item("&Delete selected pages…", AddressOf DeleteSelectedPages),
            New ToolStripSeparator(),
            Item("&Insert pages from another PDF…", AddressOf InsertPages, Keys.Control Or Keys.I)})

        Dim comments As New ToolStripMenuItem("&Comments")
        comments.DropDownItems.AddRange({
            Item("&Select / edit", Sub() SetTool(ToolMode.Select)),
            Item("&Highlight text", Sub() SetTool(ToolMode.Highlight)),
            Item("Add &comment", Sub() SetTool(ToolMode.Note)),
            Item("Draw a &line", Sub() SetTool(ToolMode.Line)),
            New ToolStripSeparator(),
            Item("Your &name on comments…", AddressOf ChangeAuthor, needsDoc:=False)})

        Dim help As New ToolStripMenuItem("&Help")
        help.DropDownItems.Add(Item("&About Tumbler", AddressOf ShowAbout, needsDoc:=False))

        _menu.Items.AddRange({file, edit, view, pages, comments, help})
    End Sub

    Private Function Button(text As String, tip As String, handler As Action, Optional needsDoc As Boolean = True) As ToolStripButton
        Dim b As New ToolStripButton(text) With {.ToolTipText = tip, .DisplayStyle = ToolStripItemDisplayStyle.Text}
        AddHandler b.Click, Sub() handler()
        If needsDoc Then _docItems.Add(b)
        Return b
    End Function

    Private Sub BuildToolbar()
        Dim open = Button("Open", "Open a PDF (Ctrl+O)", AddressOf OpenDocumentDialog, needsDoc:=False)
        _tbSave.ToolTipText = "Save (Ctrl+S)"
        AddHandler _tbSave.Click, Sub() SaveDocument()
        _tbUndo.ToolTipText = "Undo (Ctrl+Z)"
        AddHandler _tbUndo.Click, Sub() UndoEdit()
        _tbRedo.ToolTipText = "Redo (Ctrl+Y)"
        AddHandler _tbRedo.Click, Sub() RedoEdit()

        AddHandler _tbPage.KeyDown, AddressOf OnPageBoxKeyDown
        _docItems.AddRange({_tbPage})

        For Each z In {"25%", "50%", "75%", "100%", "125%", "150%", "200%", "300%", "400%", "Fit width"}
            _tbZoom.Items.Add(z)
        Next
        AddHandler _tbZoom.SelectedIndexChanged, Sub()
                                                     If _tbZoom.SelectedIndex >= 0 Then ApplyZoomText()
                                                 End Sub
        AddHandler _tbZoom.KeyDown, Sub(s, e)
                                        If e.KeyCode = Keys.Enter Then ApplyZoomText() : e.SuppressKeyPress = True
                                    End Sub
        _docItems.Add(_tbZoom)

        For Each t In {(_tbSelect, ToolMode.Select, "Select, scroll, and click comments to edit them"),
                       (_tbHighlight, ToolMode.Highlight, "Highlight: drag across text (or drag a box where there is no text)"),
                       (_tbNote, ToolMode.Note, "Comment: click on the page where the comment box should go"),
                       (_tbLine, ToolMode.Line, "Line: drag to draw a line (hold Shift for straight across, down or 45°)")}
            Dim mode = t.Item2
            t.Item1.ToolTipText = t.Item3
            AddHandler t.Item1.Click, Sub() SetTool(mode)
            _docItems.Add(t.Item1)
        Next

        For Each entry In Palette.Colors
            Dim c = entry.Color
            Dim mi As New ToolStripMenuItem(entry.Name, Palette.Swatch(c, 16))
            AddHandler mi.Click, Sub() SetToolColor(c)
            _tbColor.DropDownItems.Add(mi)
        Next
        _tbColor.DropDownItems.Add(New ToolStripSeparator())
        Dim custom As New ToolStripMenuItem("More colours…")
        AddHandler custom.Click, Sub()
                                     Using dlg As New ColorDialog With {.Color = CurrentToolColor(), .FullOpen = True}
                                         If dlg.ShowDialog(Me) = DialogResult.OK Then SetToolColor(dlg.Color)
                                     End Using
                                 End Sub
        _tbColor.DropDownItems.Add(custom)
        _tbColor.ToolTipText = "Colour for new highlights, comments and lines"
        _docItems.Add(_tbColor)

        For Each w In {("Thin (1 pt)", 1.0), ("Medium (2 pt)", 2.0), ("Thick (4 pt)", 4.0), ("Very thick (8 pt)", 8.0)}
            Dim width = w.Item2
            Dim mi As New ToolStripMenuItem(w.Item1)
            AddHandler mi.Click, Sub()
                                     _settings.LineWidth = width
                                     _settings.Save()
                                     _view.LineWidth = width
                                     UpdateWidthButton()
                                 End Sub
            _tbWidth.DropDownItems.Add(mi)
        Next
        _tbWidth.ToolTipText = "Thickness of new lines"
        _docItems.Add(_tbWidth)

        Dim rotateAllButton As New ToolStripDropDownButton("Rotate all") With {.ToolTipText = "Rotate every page"}
        For Each r In {("Left 90°", -90), ("Right 90°", 90), ("180°", 180)}
            Dim degrees = r.Item2
            Dim mi As New ToolStripMenuItem(r.Item1)
            AddHandler mi.Click, Sub() RotateAll(degrees)
            rotateAllButton.DropDownItems.Add(mi)
        Next
        _docItems.Add(rotateAllButton)

        _toolbar.Items.AddRange({
            open, _tbSave, New ToolStripSeparator(),
            _tbUndo, _tbRedo, New ToolStripSeparator(),
            Button("◀", "Previous page", Sub() GoToPage(_view.CurrentPage - 1)),
            _tbPage, _tbPageCount,
            Button("▶", "Next page", Sub() GoToPage(_view.CurrentPage + 1)),
            New ToolStripSeparator(),
            Button("−", "Zoom out (Ctrl+-)", Sub() _view.Zoom /= 1.25),
            _tbZoom,
            Button("+", "Zoom in (Ctrl++)", Sub() _view.Zoom *= 1.25),
            New ToolStripSeparator(),
            _tbSelect, _tbHighlight, _tbNote, _tbLine, _tbColor, _tbWidth,
            New ToolStripSeparator(),
            Button("↺ Rotate", "Rotate selected page(s) left", Sub() RotateSelected(-90)),
            Button("↻ Rotate", "Rotate selected page(s) right", Sub() RotateSelected(90)),
            rotateAllButton,
            Button("Delete pages", "Delete selected page(s)", AddressOf DeleteSelectedPages),
            Button("Insert pages…", "Insert pages from another PDF", AddressOf InsertPages)})
    End Sub

    Private Sub BuildPanels()
        _outerSplit.Panel1.Controls.Add(_thumbs)
        _outerSplit.Panel2.Controls.Add(_innerSplit)
        _innerSplit.Panel1.Controls.Add(_view)

        ' Comments panel: list plus Edit / Delete buttons.
        Dim header As New Label With {.Text = "Comments", .Dock = DockStyle.Top, .Height = 26,
            .TextAlign = ContentAlignment.MiddleLeft, .Padding = New Padding(6, 0, 0, 0), .Font = New Font(Font, FontStyle.Bold)}
        Dim buttons As New FlowLayoutPanel With {.Dock = DockStyle.Bottom, .AutoSize = True, .Padding = New Padding(4)}
        Dim editBtn As New System.Windows.Forms.Button With {.Text = "Edit…", .AutoSize = True}
        Dim deleteBtn As New System.Windows.Forms.Button With {.Text = "Delete", .AutoSize = True}
        AddHandler editBtn.Click, Sub() EditSelectedComment()
        AddHandler deleteBtn.Click, Sub() DeleteSelectedComment()
        buttons.Controls.AddRange({editBtn, deleteBtn})

        _comments.Columns.Add("Page", 50)
        _comments.Columns.Add("Type", 75)
        _comments.Columns.Add("Comment", 200)
        _comments.Columns.Add("Author", 90)
        _comments.Columns.Add("Date", 120)
        _comments.SmallImageList = _swatches
        AddHandler _comments.SelectedIndexChanged, AddressOf OnCommentSelected
        AddHandler _comments.ItemActivate, Sub() EditSelectedComment()
        AddHandler _comments.KeyDown, Sub(s, e)
                                          If e.KeyCode = Keys.Delete Then DeleteSelectedComment()
                                          If e.KeyCode = Keys.C AndAlso e.Control Then CopySelectedAnnotation() : e.Handled = True
                                          If e.KeyCode = Keys.V AndAlso e.Control Then PasteAnnotation() : e.Handled = True
                                      End Sub

        _innerSplit.Panel2.Controls.Add(_comments)
        _innerSplit.Panel2.Controls.Add(buttons)
        _innerSplit.Panel2.Controls.Add(header)
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        ' Splitter positions need the final (DPI-scaled) size.
        Dim dpi = DeviceDpi / 96.0
        Try
            _outerSplit.SplitterDistance = CInt(150 * dpi)
            _innerSplit.SplitterDistance = Math.Max(200, _innerSplit.Width - CInt(330 * dpi))
        Catch ex As InvalidOperationException
            ' Window too small for the preferred layout; keep the defaults.
        End Try
        _swatches.ImageSize = New Size(CInt(14 * dpi), CInt(14 * dpi))
    End Sub

    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        If Not String.IsNullOrEmpty(FileToOpenOnStart) Then OpenDocument(FileToOpenOnStart)
    End Sub

#End Region

#Region "Opening, saving, closing"

    Private Sub OpenDocumentDialog()
        If Not ConfirmDiscard() Then Return
        Using dlg As New OpenFileDialog With {.Filter = "PDF files (*.pdf)|*.pdf|All files (*.*)|*.*", .Title = "Open PDF"}
            If Directory.Exists(_settings.LastFolder) Then dlg.InitialDirectory = _settings.LastFolder
            If dlg.ShowDialog(Me) = DialogResult.OK Then OpenDocument(dlg.FileName)
        End Using
    End Sub

    Private Sub OpenDocument(path As String)
        Dim password As String = Nothing
        Dim editor As PdfEditor
        Do
            Try
                UseWaitCursor = True
                editor = PdfEditor.Open(path, password)
                Exit Do
            Catch ex As PdfPasswordException
                UseWaitCursor = False
                password = PasswordDialog.Ask(Me, IO.Path.GetFileName(path), password IsNot Nothing)
                If password Is Nothing Then Return
            Catch ex As Exception
                UseWaitCursor = False
                MessageBox.Show(Me, $"Tumbler could not open ""{IO.Path.GetFileName(path)}"":" & vbCrLf & vbCrLf & ex.Message,
                                "Open PDF", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return
            Finally
                UseWaitCursor = False
            End Try
        Loop

        _settings.LastFolder = IO.Path.GetDirectoryName(path)
        _settings.Save()
        AttachEditor(editor)
        If editor.WasDecrypted Then
            MessageBox.Show(Me, "This PDF was password protected. Tumbler has opened it, and when you save, the saved copy will not have a password.",
                            "Open PDF", MessageBoxButtons.OK, MessageBoxIcon.Information)
        End If
        BeginInvoke(Sub() _view.Zoom = Math.Min(1.5, Math.Max(0.5, _view.FitWidthZoom())))
    End Sub

    Private Sub AttachEditor(editor As PdfEditor)
        If _editor IsNot Nothing Then RemoveHandler _editor.Changed, AddressOf OnDocumentChanged
        _editor = editor
        If _editor IsNot Nothing Then AddHandler _editor.Changed, AddressOf OnDocumentChanged
        RefreshDocument(keepPosition:=False)
    End Sub

    Private Function SaveDocument() As Boolean
        If _editor Is Nothing Then Return True
        If String.IsNullOrEmpty(_editor.FilePath) Then Return SaveDocumentAs()
        Return TrySave(_editor.FilePath)
    End Function

    Private Function SaveDocumentAs() As Boolean
        If _editor Is Nothing Then Return True
        Using dlg As New SaveFileDialog With {.Filter = "PDF files (*.pdf)|*.pdf", .DefaultExt = "pdf", .OverwritePrompt = True, .Title = "Save PDF as"}
            If Not String.IsNullOrEmpty(_editor.FilePath) Then
                dlg.InitialDirectory = IO.Path.GetDirectoryName(_editor.FilePath)
                dlg.FileName = IO.Path.GetFileNameWithoutExtension(_editor.FilePath) & " (edited).pdf"
            End If
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return False
            Return TrySave(dlg.FileName)
        End Using
    End Function

    Private Function TrySave(path As String) As Boolean
        Try
            _editor.SaveAs(path)
            Return True
        Catch ex As Exception
            MessageBox.Show(Me, $"Could not save to ""{path}"":" & vbCrLf & vbCrLf & ex.Message &
                            vbCrLf & vbCrLf & "If the file is open in another program, close it there or use Save As.",
                            "Save", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return False
        End Try
    End Function

    Private Function CloseDocument() As Boolean
        If Not ConfirmDiscard() Then Return False
        AttachEditor(Nothing)
        Return True
    End Function

    ''' <summary>Asks about unsaved changes. Returns False if the user cancels.</summary>
    Private Function ConfirmDiscard() As Boolean
        If _editor Is Nothing OrElse Not _editor.IsDirty Then Return True
        Dim name = If(String.IsNullOrEmpty(_editor.FilePath), "this document", IO.Path.GetFileName(_editor.FilePath))
        Select Case MessageBox.Show(Me, $"Save changes to {name}?", "Tumbler", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question)
            Case DialogResult.Yes : Return SaveDocument()
            Case DialogResult.No : Return True
            Case Else : Return False
        End Select
    End Function

    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        If Not ConfirmDiscard() Then
            e.Cancel = True
            Return
        End If
        MyBase.OnFormClosing(e)
    End Sub

    Protected Overrides Sub OnFormClosed(e As FormClosedEventArgs)
        _renderer?.Dispose()
        MyBase.OnFormClosed(e)
    End Sub

    ''' <summary>
    ''' Every part of the window accepts dropped files, not just its edges:
    ''' child controls swallow drops unless they opt in too.
    ''' </summary>
    Private Sub EnableFileDrop(target As Control)
        target.AllowDrop = True
        AddHandler target.DragEnter, AddressOf OnFileDragOver
        AddHandler target.DragOver, AddressOf OnFileDragOver
        AddHandler target.DragLeave, AddressOf OnFileDragLeave
        AddHandler target.DragDrop, AddressOf OnFileDragDrop
    End Sub

    ''' <summary>Insert position chosen by the mouse during a drag; -1 means the end.</summary>
    Private _dropAt As Integer = -1

    Private Sub OnFileDragOver(sender As Object, e As DragEventArgs)
        e.Effect = If(e.Data.GetDataPresent(DataFormats.FileDrop), DragDropEffects.Copy, DragDropEffects.None)
        Dim at = -1
        If _editor IsNot Nothing AndAlso e.Effect <> DragDropEffects.None Then
            ' Over the pages or the page strip, the mouse picks the spot; anywhere else means the end.
            If sender Is _view Then
                Dim pt = _view.PointToClient(New Point(e.X, e.Y))
                _view.ScrollForDrag(pt)
                at = _view.DropIndexAt(pt)
            ElseIf sender Is _thumbs Then
                Dim pt = _thumbs.PointToClient(New Point(e.X, e.Y))
                _thumbs.ScrollForDrag(pt)
                at = _thumbs.DropIndexAt(pt)
            End If
        End If
        _dropAt = at
        _view.ShowDropMarker(If(sender Is _view, at, -1))
        _thumbs.ShowDropMarker(If(sender Is _thumbs, at, -1))
        If _editor IsNot Nothing AndAlso e.Effect <> DragDropEffects.None Then
            _statusHint.Text = If(at >= 0, DropMarkerPainter.Describe(at, _editor.PageCount),
                                  "Drop to add the pages at the end, or move over the pages to choose where")
        End If
    End Sub

    Private Sub OnFileDragLeave(sender As Object, e As EventArgs)
        ClearDropMarkers()
    End Sub

    Private Sub ClearDropMarkers()
        _view.ShowDropMarker(-1)
        _thumbs.ShowDropMarker(-1)
        UpdateHint()
    End Sub

    ''' <summary>
    ''' Dropped PDFs: with a document open, their pages go where the insert line
    ''' showed (or at the end), in the order dropped. With nothing open, the
    ''' first one opens and the rest are appended.
    ''' </summary>
    Private Sub OnFileDragDrop(sender As Object, e As DragEventArgs)
        Dim at = _dropAt
        _dropAt = -1
        ClearDropMarkers()
        Dim files = TryCast(e.Data.GetData(DataFormats.FileDrop), String())
        If files Is Nothing OrElse files.Length = 0 Then Return
        ' Let the drag source (Explorer, possibly on another screen) finish before we do slow work.
        BeginInvoke(Sub() HandleDroppedFiles(files, at))
    End Sub

    Private Sub HandleDroppedFiles(files As String(), insertAt As Integer)
        Activate()
        Dim queue = files.Where(Function(f) File.Exists(f)).ToList()
        If queue.Count = 0 Then Return
        If _editor Is Nothing Then
            OpenDocument(queue(0))
            queue.RemoveAt(0)
            If _editor Is Nothing Then Return
            insertAt = -1
        End If
        Dim position = If(insertAt < 0 OrElse insertAt > _editor.PageCount, _editor.PageCount, insertAt)
        Dim firstNewPage = position
        For Each path In queue
            Dim added = InsertFile(path, position)
            If added < 0 Then Exit For
            position += added
        Next
        If position > firstNewPage Then _view.ScrollToPage(firstNewPage)
    End Sub

    ''' <summary>
    ''' Inserts every page of a PDF at a position. Returns the number of pages
    ''' added, or -1 if the user cancelled.
    ''' </summary>
    Private Function InsertFile(path As String, position As Integer) As Integer
        Dim password As String = Nothing
        Do
            Try
                UseWaitCursor = True
                Dim count = PdfEditor.CountPages(path, password)
                _editor.InsertPagesFrom(path, Enumerable.Range(0, count), position, password)
                Return count
            Catch ex As PdfPasswordException
                UseWaitCursor = False
                password = PasswordDialog.Ask(Me, IO.Path.GetFileName(path), password IsNot Nothing)
                If password Is Nothing Then Return -1
            Catch ex As Exception
                UseWaitCursor = False
                Dim goOn = MessageBox.Show(Me, $"Could not add ""{IO.Path.GetFileName(path)}"":" & vbCrLf & ex.Message,
                                           "Tumbler", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) = DialogResult.OK
                Return If(goOn, 0, -1)
            Finally
                UseWaitCursor = False
            End Try
        Loop
    End Function

#End Region

#Region "Document changes"

    ''' <summary>Runs an edit and reports failures without losing the document.</summary>
    Private Sub RunEdit(action As Action)
        If _editor Is Nothing Then Return
        Try
            UseWaitCursor = True
            action()
        Catch ex As Exception
            MessageBox.Show(Me, ex.Message, "Tumbler", MessageBoxButtons.OK, MessageBoxIcon.Warning)
        Finally
            UseWaitCursor = False
        End Try
    End Sub

    Private Sub OnDocumentChanged(sender As Object, e As EventArgs)
        RefreshDocument(keepPosition:=True)
    End Sub

    Private Sub RefreshDocument(keepPosition As Boolean)
        Dim old = _renderer
        _renderer = If(_editor Is Nothing, Nothing, New PdfRenderer(_editor.Bytes))
        _view.SetDocument(_editor, _renderer, keepPosition)
        _thumbs.SetDocument(_editor, _renderer, keepPosition)
        _thumbs.CurrentPage = _view.CurrentPage
        old?.Dispose()
        RefreshComments()
        UpdateUi()
    End Sub

    Private Sub UndoEdit()
        RunEdit(Sub() _editor.Undo())
    End Sub

    Private Sub RedoEdit()
        RunEdit(Sub() _editor.Redo())
    End Sub

#End Region

#Region "Page commands"

    Private Sub RotateSelected(degrees As Integer)
        If _editor Is Nothing Then Return
        RunEdit(Sub() _editor.RotatePages(_thumbs.SelectedPages(), degrees))
    End Sub

    Private Sub RotateAll(degrees As Integer)
        If _editor Is Nothing Then Return
        RunEdit(Sub() _editor.RotateAllPages(degrees))
    End Sub

    Private Sub DeleteSelectedPages()
        If _editor Is Nothing Then Return
        Dim pages = _thumbs.SelectedPages()
        If pages.Count = 0 Then Return
        If pages.Count >= _editor.PageCount Then
            MessageBox.Show(Me, "You can't delete every page. Select fewer pages in the page strip.", "Delete pages",
                            MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        Dim names = If(pages.Count = 1, $"page {pages(0) + 1}", $"{pages.Count} pages ({DescribePages(pages)})")
        If MessageBox.Show(Me, $"Delete {names}?", "Delete pages", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) <> DialogResult.OK Then Return
        _thumbs.ClearSelection()
        RunEdit(Sub() _editor.DeletePages(pages))
    End Sub

    Private Shared Function DescribePages(pages As List(Of Integer)) As String
        Dim shown = pages.Take(8).Select(Function(p) (p + 1).ToString())
        Return String.Join(", ", shown) & If(pages.Count > 8, ", …", "")
    End Function

    Private Sub InsertPages()
        If _editor Is Nothing Then Return
        Using dlg As New InsertPagesDialog(_view.CurrentPage, _editor.PageCount, _settings.LastFolder)
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Dim at = dlg.InsertAt
            RunEdit(Sub() _editor.InsertPagesFrom(dlg.SourcePath, dlg.SourcePages, at, dlg.SourcePassword))
            BeginInvoke(Sub() _view.ScrollToPage(Math.Min(at, _editor.PageCount - 1)))
        End Using
    End Sub

    Private Sub GoToPage(index As Integer)
        If _editor Is Nothing Then Return
        _view.ScrollToPage(Math.Max(0, Math.Min(_editor.PageCount - 1, index)))
    End Sub

    Private Sub OnPageBoxKeyDown(sender As Object, e As KeyEventArgs)
        If e.KeyCode <> Keys.Enter Then Return
        e.SuppressKeyPress = True
        Dim n As Integer
        If Integer.TryParse(_tbPage.Text, n) Then GoToPage(n - 1)
        _view.Focus()
    End Sub

    Private Sub OnViewPageChanged(sender As Object, e As EventArgs)
        _thumbs.CurrentPage = _view.CurrentPage
        UpdatePageBox()
    End Sub

#End Region

#Region "Tools and colours"

    Private Sub SetTool(mode As ToolMode)
        _view.Tool = mode
        _tbSelect.Checked = mode = ToolMode.Select
        _tbHighlight.Checked = mode = ToolMode.Highlight
        _tbNote.Checked = mode = ToolMode.Note
        _tbLine.Checked = mode = ToolMode.Line
        _tbWidth.Visible = mode = ToolMode.Line
        _view.HighlightColor = Color.FromArgb(_settings.HighlightColor)
        _view.LineColor = Color.FromArgb(_settings.LineColor)
        _view.LineWidth = _settings.LineWidth
        UpdateColorButton()
        UpdateWidthButton()
        UpdateHint()
    End Sub

    Private Function CurrentToolColor() As Color
        Select Case _view.Tool
            Case ToolMode.Note : Return Color.FromArgb(_settings.NoteColor)
            Case ToolMode.Line : Return Color.FromArgb(_settings.LineColor)
            Case Else : Return Color.FromArgb(_settings.HighlightColor)
        End Select
    End Function

    Private Sub SetToolColor(c As Color)
        Select Case _view.Tool
            Case ToolMode.Note : _settings.NoteColor = c.ToArgb()
            Case ToolMode.Line : _settings.LineColor = c.ToArgb()
            Case Else : _settings.HighlightColor = c.ToArgb()
        End Select
        _settings.Save()
        _view.HighlightColor = Color.FromArgb(_settings.HighlightColor)
        _view.LineColor = Color.FromArgb(_settings.LineColor)
        UpdateColorButton()
    End Sub

    Private Sub UpdateColorButton()
        Dim old = _tbColor.Image
        _tbColor.Image = Palette.Swatch(CurrentToolColor(), 16)
        _tbColor.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText
        old?.Dispose()
        Dim forWhat = If(_view.Tool = ToolMode.Note, "comments", If(_view.Tool = ToolMode.Line, "lines", "highlights"))
        _tbColor.ToolTipText = $"Colour for new {forWhat}"
    End Sub

    Private Sub UpdateWidthButton()
        _tbWidth.Text = $"{_settings.LineWidth:0.#} pt"
        For Each mi In _tbWidth.DropDownItems.OfType(Of ToolStripMenuItem)()
            mi.Checked = mi.Text.Contains($"({_settings.LineWidth:0.#} pt)")
        Next
    End Sub

    Private Sub ChangeAuthor()
        Dim name = Microsoft.VisualBasic.Interaction.InputBox("Name shown as the author of your comments:", "Tumbler", _settings.Author)
        If String.IsNullOrWhiteSpace(name) Then Return
        _settings.Author = name.Trim()
        _settings.Save()
    End Sub

#End Region

#Region "Copy and paste"

    ''' <summary>The last comment or line copied, ready to paste on any page.</summary>
    Private _clip As AnnotationClip
    Private _lastPaste As (Page As Integer, Point As PointF) = (-1, PointF.Empty)
    Private _pasteStep As Integer

    Private Sub CopySelectedAnnotation()
        Dim a = _view.SelectedAnnotation
        If a Is Nothing Then
            _statusHint.Text = "Click a comment or line first (Select tool), then copy it"
            Return
        End If
        If Not AnnotationClip.CanCopy(a) Then
            _statusHint.Text = $"A {a.KindName.ToLower()} can't be copied; comments, lines and notes can"
            Return
        End If
        _clip = AnnotationClip.From(a)
        _lastPaste = (-1, PointF.Empty)
        ' Comment text also goes on the Windows clipboard, for pasting into other programs.
        If Not String.IsNullOrWhiteSpace(a.Contents) Then
            Try
                Clipboard.SetText(a.Contents)
            Catch
                ' Another program may be holding the clipboard; Tumbler's own copy still works.
            End Try
        End If
        _statusHint.Text = $"{a.KindName} copied. Press Ctrl+V with the mouse where it should go."
        UpdateUi()
    End Sub

    ''' <summary>
    ''' Pastes the copied comment or line. With a page point given (right-click), it goes
    ''' there; otherwise under the mouse if it is over a page, or next to the original.
    ''' </summary>
    Private Sub PasteAnnotation(Optional pageIndex As Integer = -1, Optional point As PointF = Nothing)
        If _clip Is Nothing OrElse _editor Is Nothing Then Return
        If pageIndex < 0 Then point = _view.PointUnderMouse(pageIndex)
        If pageIndex < 0 Then
            pageIndex = _view.CurrentPage
            point = New PointF(_clip.Bounds.X + 12, _clip.Bounds.Y + 12)
        End If
        ' Pasting again at the same spot steps each copy down and right so they don't stack.
        If _lastPaste.Page = pageIndex AndAlso Math.Abs(_lastPaste.Point.X - point.X) < 1 AndAlso Math.Abs(_lastPaste.Point.Y - point.Y) < 1 Then
            _pasteStep += 1
        Else
            _pasteStep = 0
        End If
        _lastPaste = (pageIndex, point)
        Dim page = pageIndex
        Dim at As New PointF(point.X + 12 * _pasteStep, point.Y + 12 * _pasteStep)
        Dim before = _editor.Pages(page).Annotations.Count
        RunEdit(Sub() _editor.PasteAnnotation(_clip, page, at, _settings.Author))
        Dim annots = _editor.Pages(page).Annotations
        If annots.Count > before Then _view.SelectedAnnotation = annots.Last()
    End Sub

    Private Sub DeleteSelectedAnnotation()
        Dim a = _view.SelectedAnnotation
        If a Is Nothing Then Return
        RunEdit(Sub() _editor.DeleteAnnotation(a))
        _view.SelectedAnnotation = Nothing
    End Sub

    Private Sub OnViewMenuRequested(sender As Object, e As ViewMenuEventArgs)
        Dim menu As New ContextMenuStrip()
        Dim a = e.Annotation
        If a IsNot Nothing Then
            menu.Items.Add($"Edit {a.KindName.ToLower()}…", Nothing, Sub() EditAnnotation(a))
            Dim copy = DirectCast(menu.Items.Add("Copy", Nothing, Sub() CopySelectedAnnotation()), ToolStripMenuItem)
            copy.ShortcutKeyDisplayString = "Ctrl+C"
            copy.Enabled = AnnotationClip.CanCopy(a)
            Dim del = DirectCast(menu.Items.Add("Delete", Nothing, Sub() DeleteSelectedAnnotation()), ToolStripMenuItem)
            del.ShortcutKeyDisplayString = "Del"
            menu.Items.Add(New ToolStripSeparator())
        End If
        Dim page = e.PageIndex, pt = e.Point
        Dim paste = DirectCast(menu.Items.Add(If(_clip Is Nothing, "Paste here", $"Paste {ClipName()} here"), Nothing,
                                              Sub() PasteAnnotation(page, pt)), ToolStripMenuItem)
        paste.ShortcutKeyDisplayString = "Ctrl+V"
        paste.Enabled = _clip IsNot Nothing AndAlso page >= 0
        AddHandler menu.Closed, Sub() BeginInvoke(Sub() menu.Dispose())
        menu.Show(_view, e.ClientLocation)
    End Sub

    Private Function ClipName() As String
        Select Case _clip.Kind
            Case AnnotationKind.Note : Return "sticky note"
            Case AnnotationKind.TextBox : Return "text box"
            Case Else : Return _clip.Kind.ToString().ToLower()
        End Select
    End Function

#End Region

#Region "Creating and editing comments"

    Private Sub OnHighlightRequested(sender As Object, e As PageEventArgs)
        RunEdit(Sub() _editor.AddHighlight(e.PageIndex, e.Quads, Color.FromArgb(_settings.HighlightColor), _settings.Author))
    End Sub

    Private Sub OnNoteRequested(sender As Object, e As PageEventArgs)
        Using dlg As New CommentDialog(AnnotationKind.Comment, True, "", Color.FromArgb(_settings.NoteColor), _settings.CommentFontSize, _settings.Author)
            If dlg.ShowDialog(Me) <> DialogResult.OK OrElse String.IsNullOrWhiteSpace(dlg.CommentText) Then Return
            RememberColor(AnnotationKind.Note, dlg.SelectedColor)
            _settings.CommentFontSize = dlg.FontSizeValue
            _settings.Save()
            RunEdit(Sub() _editor.AddComment(e.PageIndex, e.Point, dlg.CommentText, dlg.SelectedColor, dlg.FontSizeValue, _settings.Author))
        End Using
    End Sub

    Private Sub OnLineRequested(sender As Object, e As PageEventArgs)
        RunEdit(Sub() _editor.AddLine(e.PageIndex, e.Point, e.EndPoint, Color.FromArgb(_settings.LineColor), _settings.LineWidth, _settings.Author))
    End Sub

    Private Sub RememberColor(kind As AnnotationKind, c As Color)
        If kind = AnnotationKind.Note Then _settings.NoteColor = c.ToArgb()
        _settings.Save()
        UpdateColorButton()
    End Sub

    Private Sub EditAnnotation(a As AnnotationInfo)
        If a Is Nothing Then Return
        If Not a.CanEdit Then
            If MessageBox.Show(Me, $"This is a ""{a.Subtype}"" comment, which Tumbler can show but not edit." & vbCrLf & vbCrLf &
                               If(a.Contents, "") & vbCrLf & vbCrLf & "Delete it?", "Comment",
                               MessageBoxButtons.YesNo, MessageBoxIcon.Information) = DialogResult.Yes Then
                RunEdit(Sub() _editor.DeleteAnnotation(a))
            End If
            Return
        End If
        Using dlg As New CommentDialog(a.Kind, False, a.Contents, a.Color, a.FontSize, a.Author)
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            If dlg.DeleteRequested Then
                RunEdit(Sub() _editor.DeleteAnnotation(a))
            Else
                RunEdit(Sub() _editor.UpdateAnnotation(a, dlg.CommentText, dlg.SelectedColor, dlg.FontSizeValue))
            End If
        End Using
    End Sub

    Private Sub RefreshComments()
        _comments.BeginUpdate()
        _comments.Items.Clear()
        If _editor IsNot Nothing Then
            For Each a In _editor.AllAnnotations()
                Dim key = a.Color.ToArgb().ToString("X8")
                If Not _swatches.Images.ContainsKey(key) Then _swatches.Images.Add(key, Palette.Swatch(a.Color, _swatches.ImageSize.Width))
                Dim text = If(a.Contents, "").Replace(vbCr, " ").Replace(vbLf, " ")
                Dim li As New ListViewItem({(a.PageIndex + 1).ToString(), a.KindName, text, If(a.Author, ""),
                                            If(a.Modified.HasValue, a.Modified.Value.ToString("g"), "")}) With {.ImageKey = key, .Tag = a}
                _comments.Items.Add(li)
            Next
        End If
        _comments.EndUpdate()
    End Sub

    Private Function SelectedComment() As AnnotationInfo
        If _comments.SelectedItems.Count = 0 Then Return Nothing
        Return TryCast(_comments.SelectedItems(0).Tag, AnnotationInfo)
    End Function

    Private Sub OnCommentSelected(sender As Object, e As EventArgs)
        Dim a = SelectedComment()
        If a IsNot Nothing AndAlso a.PageIndex <> _view.CurrentPage Then _view.ScrollToPage(a.PageIndex)
        If a IsNot Nothing Then _view.SelectedAnnotation = a
    End Sub

    Private Sub EditSelectedComment()
        EditAnnotation(SelectedComment())
    End Sub

    Private Sub DeleteSelectedComment()
        Dim a = SelectedComment()
        If a Is Nothing Then Return
        If MessageBox.Show(Me, $"Delete this {a.KindName.ToLower()} on page {a.PageIndex + 1}?", "Delete comment",
                           MessageBoxButtons.OKCancel, MessageBoxIcon.Question) = DialogResult.OK Then
            RunEdit(Sub() _editor.DeleteAnnotation(a))
        End If
    End Sub

#End Region

#Region "UI state"

    Private Sub UpdateUi()
        Dim hasDoc = _editor IsNot Nothing
        For Each i In _docItems
            i.Enabled = hasDoc
        Next
        _tbSave.Enabled = hasDoc AndAlso _editor.IsDirty
        _miSave.Enabled = _tbSave.Enabled
        _tbUndo.Enabled = hasDoc AndAlso _editor.CanUndo
        _miUndo.Enabled = _tbUndo.Enabled
        _tbRedo.Enabled = hasDoc AndAlso _editor.CanRedo
        _miRedo.Enabled = _tbRedo.Enabled

        If hasDoc Then
            Dim name = If(String.IsNullOrEmpty(_editor.FilePath), "Untitled", IO.Path.GetFileName(_editor.FilePath))
            Text = $"{If(_editor.IsDirty, "*", "")}{name} - Tumbler"
            _statusFile.Text = If(_editor.FilePath, "")
            _tbPageCount.Text = $"/ {_editor.PageCount}"
        Else
            Text = "Tumbler"
            _statusFile.Text = "No document open"
            _tbPageCount.Text = "/ 0"
            _tbPage.Text = ""
        End If
        UpdatePageBox()
        UpdateZoomBox()
        UpdateHint()
    End Sub

    Private Sub UpdatePageBox()
        If _editor Is Nothing Then Return
        If Not _tbPage.Focused Then _tbPage.Text = (_view.CurrentPage + 1).ToString()
    End Sub

    Private Sub UpdateZoomBox()
        If Not _tbZoom.Focused Then _tbZoom.Text = $"{Math.Round(_view.Zoom * 100)}%"
    End Sub

    Private Sub ApplyZoomText()
        Dim t = _tbZoom.Text.Trim()
        If t.StartsWith("Fit", StringComparison.OrdinalIgnoreCase) Then
            _view.Zoom = _view.FitWidthZoom()
        Else
            Dim v As Double
            If Double.TryParse(t.TrimEnd("%"c).Trim(), v) AndAlso v > 0 Then _view.Zoom = v / 100
        End If
        UpdateZoomBox()
        _view.Focus()
    End Sub

    Private Sub UpdateHint()
        If _editor Is Nothing Then
            _statusHint.Text = ""
            Return
        End If
        Dim sel = _thumbs.SelectedPages()
        Dim pagesText = If(_thumbs.HasExplicitSelection, $"{sel.Count} page(s) selected", $"Page {_view.CurrentPage + 1} of {_editor.PageCount}")
        Dim toolText As String
        Select Case _view.Tool
            Case ToolMode.Highlight : toolText = "Drag across text to highlight it"
            Case ToolMode.Note : toolText = "Click on the page to add a comment"
            Case ToolMode.Line : toolText = "Drag to draw a line; hold Shift to keep it straight"
            Case Else : toolText = "Click a comment or line to select it, Ctrl+C / Ctrl+V to copy it; double-click to edit"
        End Select
        _statusHint.Text = $"{pagesText}   |   {toolText}"
    End Sub

    Private Sub ShowAbout()
        Using dlg As New AboutDialog()
            dlg.ShowDialog(Me)
        End Using
    End Sub

#End Region

End Class
