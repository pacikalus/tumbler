''' <summary>Base for Tumbler's small dialogs: DPI scaling, OK/Cancel buttons.</summary>
Public Class DialogBase
    Inherits Form

    Protected ReadOnly Body As New TableLayoutPanel With {
        .Dock = DockStyle.Fill, .Padding = New Padding(12), .AutoSize = True, .ColumnCount = 1}
    Protected ReadOnly Buttons As New FlowLayoutPanel With {
        .Dock = DockStyle.Bottom, .FlowDirection = FlowDirection.RightToLeft, .AutoSize = True, .Padding = New Padding(8)}
    Protected ReadOnly OkButton As New Button With {.Text = "OK", .DialogResult = DialogResult.OK, .AutoSize = True, .MinimumSize = New Size(80, 0)}
    Protected ReadOnly CancelBtn As New Button With {.Text = "Cancel", .DialogResult = DialogResult.Cancel, .AutoSize = True, .MinimumSize = New Size(80, 0)}

    Public Sub New()
        SuspendLayout()
        AutoScaleDimensions = New SizeF(96.0F, 96.0F)
        AutoScaleMode = AutoScaleMode.Dpi
        Font = New Font("Segoe UI", 9.0F)
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False
        ShowInTaskbar = False
        StartPosition = FormStartPosition.CenterParent
        AcceptButton = OkButton
        CancelButton = CancelBtn
        Buttons.Controls.Add(CancelBtn)
        Buttons.Controls.Add(OkButton)
        Controls.Add(Body)
        Controls.Add(Buttons)
        ResumeLayout(False)
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        ' Size the window to its contents (after DPI scaling has been applied).
        Dim bodySize = Body.GetPreferredSize(Size.Empty)
        Dim buttonsSize = Buttons.GetPreferredSize(Size.Empty)
        ClientSize = New Size(Math.Max(bodySize.Width, buttonsSize.Width), bodySize.Height + buttonsSize.Height)
    End Sub

    Protected Shared Function MakeLabel(text As String) As Label
        Return New Label With {.Text = text, .AutoSize = True, .Margin = New Padding(0, 8, 0, 3)}
    End Function

End Class

''' <summary>Create or edit a comment, line or highlight: text, colour, size.</summary>
Public Class CommentDialog
    Inherits DialogBase

    Private ReadOnly _text As New TextBox With {.Multiline = True, .ScrollBars = ScrollBars.Vertical, .AcceptsReturn = True,
        .Size = New Size(400, 120)}
    Private ReadOnly _colors As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = False}
    Private ReadOnly _fontSize As New NumericUpDown With {.Minimum = 6, .Maximum = 72, .DecimalPlaces = 0, .Value = 12, .Width = 70}
    Private ReadOnly _deleteButton As New Button With {.Text = "Delete", .AutoSize = True, .MinimumSize = New Size(80, 0), .Visible = False}
    Private _color As Color

    ''' <summary>True when the user pressed Delete.</summary>
    Public Property DeleteRequested As Boolean

    Public Sub New(kind As AnnotationKind, isNew As Boolean, text As String, color As Color, fontSize As Double, author As String)
        Dim kindName As String
        Select Case kind
            Case AnnotationKind.Note : kindName = "Sticky note"
            Case AnnotationKind.Comment : kindName = "Comment"
            Case AnnotationKind.TextBox : kindName = "Text box"
            Case AnnotationKind.Line : kindName = "Line"
            Case Else : kindName = "Highlight"
        End Select
        Me.Text = If(isNew, "Add " & kindName.ToLower(), "Edit " & kindName.ToLower())
        _color = color

        Dim prompt = If(kind = AnnotationKind.Highlight OrElse kind = AnnotationKind.Line, "Note (optional):", "Text:")
        If Not isNew AndAlso Not String.IsNullOrEmpty(author) Then prompt = $"{prompt}    (by {author})"
        Body.Controls.Add(MakeLabel(prompt))
        _text.Text = If(text, "").Replace(vbCr, "").Replace(vbLf, vbCrLf)
        Body.Controls.Add(_text)

        Body.Controls.Add(MakeLabel("Colour:"))
        For Each entry In Palette.Colors
            Dim swatch As New Button With {
                .Width = 30, .Height = 30, .BackColor = entry.Color, .FlatStyle = FlatStyle.Flat,
                .Tag = entry.Color, .Margin = New Padding(0, 0, 6, 0)}
            AddHandler swatch.Click, Sub(s, e) SetColor(CType(DirectCast(s, Button).Tag, Color))
            _colors.Controls.Add(swatch)
        Next
        Dim more As New Button With {.Text = "More…", .AutoSize = True, .Height = 30}
        AddHandler more.Click, AddressOf PickCustomColor
        _colors.Controls.Add(more)
        Body.Controls.Add(_colors)

        If kind = AnnotationKind.TextBox OrElse kind = AnnotationKind.Comment Then
            Dim row As New FlowLayoutPanel With {.AutoSize = True}
            row.Controls.Add(New Label With {.Text = "Font size:", .AutoSize = True, .Margin = New Padding(0, 6, 6, 0)})
            _fontSize.Value = CDec(Math.Max(6, Math.Min(72, Math.Round(fontSize))))
            row.Controls.Add(_fontSize)
            Body.Controls.Add(row)
        End If

        If Not isNew Then
            _deleteButton.Visible = True
            AddHandler _deleteButton.Click, Sub()
                                                DeleteRequested = True
                                                DialogResult = DialogResult.OK
                                            End Sub
            Buttons.Controls.Add(_deleteButton)
            _deleteButton.Margin = New Padding(3, 3, 60, 3)
        End If

        ' Enter adds a new line in the text, so make Ctrl+Enter the OK shortcut instead.
        AcceptButton = Nothing
        AddHandler _text.KeyDown, Sub(s, e)
                                      If e.KeyCode = Keys.Enter AndAlso e.Control Then
                                          e.SuppressKeyPress = True
                                          DialogResult = DialogResult.OK
                                      End If
                                  End Sub
        UpdateSwatches()
    End Sub

    Public ReadOnly Property CommentText As String
        Get
            Return _text.Text
        End Get
    End Property

    Public ReadOnly Property SelectedColor As Color
        Get
            Return _color
        End Get
    End Property

    Public ReadOnly Property FontSizeValue As Double
        Get
            Return CDbl(_fontSize.Value)
        End Get
    End Property

    Private Sub SetColor(c As Color)
        _color = c
        UpdateSwatches()
    End Sub

    Private Sub PickCustomColor(sender As Object, e As EventArgs)
        Using dlg As New ColorDialog With {.Color = _color, .FullOpen = True}
            If dlg.ShowDialog(Me) = DialogResult.OK Then SetColor(dlg.Color)
        End Using
    End Sub

    Private Sub UpdateSwatches()
        For Each c As Control In _colors.Controls
            Dim b = TryCast(c, Button)
            If b Is Nothing OrElse b.Tag Is Nothing Then Continue For
            Dim selected = CType(b.Tag, Color).ToArgb() = _color.ToArgb()
            b.FlatAppearance.BorderSize = If(selected, 3, 1)
            b.FlatAppearance.BorderColor = If(selected, Color.Black, Color.Gray)
        Next
    End Sub

    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        _text.Focus()
        _text.SelectionStart = _text.TextLength
    End Sub

End Class

''' <summary>Pick another PDF, which of its pages, and where to put them.</summary>
Public Class InsertPagesDialog
    Inherits DialogBase

    Private ReadOnly _file As New TextBox With {.Width = 360, .ReadOnly = True}
    Private ReadOnly _info As New Label With {.AutoSize = True, .ForeColor = Color.DimGray}
    Private ReadOnly _range As New TextBox With {.Width = 200, .Text = "all"}
    Private ReadOnly _atStart As New RadioButton With {.Text = "At the beginning", .AutoSize = True}
    Private ReadOnly _before As New RadioButton With {.AutoSize = True}
    Private ReadOnly _after As New RadioButton With {.AutoSize = True, .Checked = True}
    Private ReadOnly _atEnd As New RadioButton With {.Text = "At the end", .AutoSize = True}
    Private ReadOnly _currentPage As Integer
    Private ReadOnly _pageCount As Integer
    Private _sourcePageCount As Integer
    Private _password As String

    Public Sub New(currentPage As Integer, pageCount As Integer, initialFolder As String)
        Text = "Insert pages from another PDF"
        _currentPage = currentPage
        _pageCount = pageCount

        Body.Controls.Add(MakeLabel("PDF file:"))
        Dim fileRow As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = False}
        fileRow.Controls.Add(_file)
        Dim browseButton As New Button With {.Text = "Browse…", .AutoSize = True}
        AddHandler browseButton.Click, Sub() Browse(initialFolder)
        fileRow.Controls.Add(browseButton)
        Body.Controls.Add(fileRow)
        Body.Controls.Add(_info)

        Body.Controls.Add(MakeLabel("Pages to insert (e.g. all, 1-3, 5):"))
        Body.Controls.Add(_range)

        Body.Controls.Add(MakeLabel("Insert:"))
        _before.Text = $"Before page {currentPage + 1}"
        _after.Text = $"After page {currentPage + 1}"
        Dim group As New FlowLayoutPanel With {.FlowDirection = FlowDirection.TopDown, .AutoSize = True}
        group.Controls.AddRange({_atStart, _before, _after, _atEnd})
        Body.Controls.Add(group)

        OkButton.Text = "Insert"
        OkButton.Enabled = False
    End Sub

    Public ReadOnly Property SourcePath As String
        Get
            Return _file.Text
        End Get
    End Property

    Public ReadOnly Property SourcePassword As String
        Get
            Return _password
        End Get
    End Property

    ''' <summary>Zero-based pages of the source file, in the order to insert them.</summary>
    Public Property SourcePages As List(Of Integer)

    ''' <summary>Zero-based position in the open document.</summary>
    Public ReadOnly Property InsertAt As Integer
        Get
            If _atStart.Checked Then Return 0
            If _before.Checked Then Return _currentPage
            If _after.Checked Then Return _currentPage + 1
            Return _pageCount
        End Get
    End Property

    Private Sub Browse(initialFolder As String)
        Using dlg As New OpenFileDialog With {.Filter = "PDF files (*.pdf)|*.pdf|All files (*.*)|*.*", .Title = "Choose a PDF to take pages from"}
            If Not String.IsNullOrEmpty(initialFolder) Then dlg.InitialDirectory = initialFolder
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            _password = Nothing
            Do
                Try
                    _sourcePageCount = PdfEditor.CountPages(dlg.FileName, _password)
                    Exit Do
                Catch ex As PdfPasswordException
                    _password = PasswordDialog.Ask(Me, IO.Path.GetFileName(dlg.FileName), _password IsNot Nothing)
                    If _password Is Nothing Then Return
                Catch ex As Exception
                    MessageBox.Show(Me, "That file could not be read as a PDF:" & vbCrLf & ex.Message, Text,
                                    MessageBoxButtons.OK, MessageBoxIcon.Warning)
                    Return
                End Try
            Loop
            _file.Text = dlg.FileName
            _info.Text = $"{_sourcePageCount} page{If(_sourcePageCount = 1, "", "s")}"
            OkButton.Enabled = True
        End Using
    End Sub

    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        MyBase.OnFormClosing(e)
        If DialogResult <> DialogResult.OK Then Return
        Try
            SourcePages = PageRanges.Parse(_range.Text, _sourcePageCount)
        Catch ex As FormatException
            MessageBox.Show(Me, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning)
            e.Cancel = True
        End Try
    End Sub

End Class

''' <summary>Asks for a PDF's password.</summary>
Public Class PasswordDialog
    Inherits DialogBase

    Private ReadOnly _box As New TextBox With {.UseSystemPasswordChar = True, .Width = 280}

    Private Sub New(fileName As String, retry As Boolean)
        Text = "Password needed"
        Body.Controls.Add(MakeLabel(If(retry, "That password did not work. Try again:", $"""{fileName}"" is protected. Enter its password:")))
        Body.Controls.Add(_box)
    End Sub

    ''' <summary>Returns the password, or Nothing if the user cancelled.</summary>
    Public Shared Function Ask(owner As IWin32Window, fileName As String, retry As Boolean) As String
        Using dlg As New PasswordDialog(fileName, retry)
            If dlg.ShowDialog(owner) <> DialogResult.OK Then Return Nothing
            Return dlg._box.Text
        End Using
    End Function

End Class
