Imports System.Diagnostics
Imports System.Reflection

''' <summary>Who made Tumbler and how to reach them. Shown in Help > About.</summary>
Public Module AppInfo
    Public Const AuthorName As String = "Pacikalus"
    Public Const CoAuthor As String = "with Claude by Anthropic, co-author"
    Public Const AuthorEmail As String = "pacikalus@gmail.com"
    Public Const ProjectUrl As String = "https://github.com/pacikalus/tumbler"

    Public ReadOnly Property Version As String
        Get
            Dim v = Assembly.GetExecutingAssembly().GetName().Version
            Return $"{v.Major}.{v.Minor}.{v.Build}"
        End Get
    End Property
End Module

''' <summary>Help > About: the logo, the version, the author and the libraries used.</summary>
Public Class AboutDialog
    Inherits Form

    Private Shared ReadOnly Ink As Color = Color.FromArgb(19, 35, 79)
    Private Shared ReadOnly Muted As Color = Color.FromArgb(96, 106, 130)
    Private Shared ReadOnly Accent As Color = Color.FromArgb(31, 79, 216)

    Public Sub New()
        SuspendLayout()
        AutoScaleDimensions = New SizeF(96.0F, 96.0F)
        AutoScaleMode = AutoScaleMode.Dpi
        Font = New Font("Segoe UI", 9.75F)
        Text = "About Tumbler"
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False
        ShowInTaskbar = False
        StartPosition = FormStartPosition.CenterParent
        BackColor = Color.White
        ClientSize = New Size(460, 430)

        Dim body As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 1, .Padding = New Padding(36, 28, 36, 12), .BackColor = Color.White}
        body.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))

        Dim logo As New PictureBox With {
            .SizeMode = PictureBoxSizeMode.Zoom, .Size = New Size(300, 84), .Margin = New Padding(0, 0, 0, 6),
            .Anchor = AnchorStyles.None}
        Using s = GetType(AboutDialog).Assembly.GetManifestResourceStream("logo.png")
            If s IsNot Nothing Then logo.Image = New Bitmap(s)
        End Using
        body.Controls.Add(logo)

        body.Controls.Add(Centered($"Version {AppInfo.Version}", New Font(Font.FontFamily, 9.0F), Muted, New Padding(0, 0, 0, 14)))
        body.Controls.Add(Centered("A simple PDF editor: view, rotate, delete and insert pages," & vbCrLf &
                                   "add coloured comments, lines and highlights.", Font, Ink, New Padding(0, 0, 0, 18)))

        body.Controls.Add(Rule())

        If Not String.IsNullOrWhiteSpace(AppInfo.AuthorName) Then
            body.Controls.Add(Centered("Created by", New Font(Font.FontFamily, 8.5F), Muted, New Padding(0, 16, 0, 6)))
            Dim avatar As New PictureBox With {
                .SizeMode = PictureBoxSizeMode.Zoom, .Size = New Size(72, 72), .Margin = New Padding(0, 0, 0, 4),
                .Anchor = AnchorStyles.None}
            Using s = GetType(AboutDialog).Assembly.GetManifestResourceStream("pacikalus.png")
                If s IsNot Nothing Then avatar.Image = New Bitmap(s) Else avatar.Visible = False
            End Using
            body.Controls.Add(avatar)
            body.Controls.Add(Centered(AppInfo.AuthorName, New Font(Font.FontFamily, 13.0F, FontStyle.Bold), Ink, New Padding(0, 2, 0, 0)))
            body.Controls.Add(Centered(AppInfo.CoAuthor, New Font(Font.FontFamily, 9.0F), Muted, New Padding(0, 2, 0, 8)))
        Else
            body.Controls.Add(New Label With {.AutoSize = True, .Margin = New Padding(0, 10, 0, 0)})
        End If
        body.Controls.Add(Link(AppInfo.AuthorEmail, "mailto:" & AppInfo.AuthorEmail, New Padding(0, 0, 0, 4)))
        body.Controls.Add(Link("github.com/pacikalus/tumbler", AppInfo.ProjectUrl, New Padding(0, 0, 0, 16)))

        body.Controls.Add(Rule())
        body.Controls.Add(Centered("Built with PDFsharp (MIT) and PDFium (BSD-3) via Docnet.Core (MIT)." & vbCrLf &
                                   $"Free and open source under the MIT licence. © {Date.Today.Year}" &
                                   If(String.IsNullOrWhiteSpace(AppInfo.AuthorName), "", " " & AppInfo.AuthorName) & ".",
                                   New Font(Font.FontFamily, 8.25F), Muted, New Padding(0, 12, 0, 0)))

        Dim footer As New Panel With {.Dock = DockStyle.Bottom, .Height = 52, .BackColor = Color.FromArgb(244, 246, 250)}
        Dim close As New Button With {
            .Text = "Close", .DialogResult = DialogResult.OK, .Size = New Size(96, 30),
            .Anchor = AnchorStyles.Right Or AnchorStyles.Top, .Location = New Point(460 - 96 - 16, 11)}
        footer.Controls.Add(close)
        AcceptButton = close
        CancelButton = close

        Controls.Add(body)
        Controls.Add(footer)
        _body = body
        _footer = footer
        ResumeLayout(False)
    End Sub

    Private ReadOnly _body As TableLayoutPanel
    Private ReadOnly _footer As Panel

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        ' Fit the height to the contents once DPI scaling has been applied.
        Dim wanted = _body.GetPreferredSize(New Size(ClientSize.Width, 0))
        ClientSize = New Size(Math.Max(ClientSize.Width, wanted.Width), wanted.Height + _footer.Height)
    End Sub

    Private Shared Function Centered(text As String, font As Font, color As Color, margin As Padding) As Label
        Return New Label With {
            .Text = text, .Font = font, .ForeColor = color, .AutoSize = True, .Anchor = AnchorStyles.None,
            .TextAlign = ContentAlignment.MiddleCenter, .Margin = margin}
    End Function

    Private Function Link(text As String, target As String, margin As Padding) As LinkLabel
        Dim l As New LinkLabel With {
            .Text = text, .AutoSize = True, .Anchor = AnchorStyles.None, .Margin = margin,
            .LinkColor = Accent, .ActiveLinkColor = Ink, .LinkBehavior = LinkBehavior.HoverUnderline}
        AddHandler l.LinkClicked, Sub()
                                      Try
                                          Process.Start(New ProcessStartInfo(target) With {.UseShellExecute = True})
                                      Catch ex As Exception
                                          MessageBox.Show(Me, $"Could not open {text}: {ex.Message}", "Tumbler",
                                                          MessageBoxButtons.OK, MessageBoxIcon.Information)
                                      End Try
                                  End Sub
        Return l
    End Function

    Private Shared Function Rule() As Control
        Return New Panel With {.Height = 1, .Dock = DockStyle.Top, .BackColor = Color.FromArgb(226, 230, 238),
                               .Margin = New Padding(0), .Anchor = AnchorStyles.Left Or AnchorStyles.Right}
    End Function

End Class
