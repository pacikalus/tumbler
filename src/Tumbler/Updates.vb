Imports System.Diagnostics
Imports System.IO
Imports System.Reflection
Imports System.Security.Cryptography.X509Certificates
Imports System.Threading

''' <summary>Checking GitHub for a newer Tumbler and running its setup program.</summary>
Public Module Updates

    Public ReadOnly Property CurrentVersion As Version
        Get
            Return Assembly.GetExecutingAssembly().GetName().Version
        End Get
    End Property

    ''' <summary>
    ''' True if the downloaded setup is signed with the same certificate as this copy
    ''' of Tumbler, so only setups from the same publisher are run. A copy that isn't
    ''' signed itself (a developer build) has nothing to compare against and accepts it.
    ''' </summary>
    Public Function SignedBySamePublisher(setupPath As String) As Boolean
        Dim mine As X509Certificate
        Try
            mine = X509Certificate.CreateFromSignedFile(Application.ExecutablePath)
        Catch ex As Exception
            Return True
        End Try
        Try
            Dim theirs = X509Certificate.CreateFromSignedFile(setupPath)
            Return String.Equals(mine.GetCertHashString(), theirs.GetCertHashString(), StringComparison.OrdinalIgnoreCase)
        Catch ex As Exception
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Starts the setup program so it upgrades this copy in place (same folder, same
    ''' per-user or all-users mode) and opens Tumbler again when it's done.
    ''' </summary>
    Public Sub StartSetup(setupPath As String)
        Dim appDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)
        Dim forAllUsers = appDir.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), StringComparison.OrdinalIgnoreCase) OrElse
                          appDir.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), StringComparison.OrdinalIgnoreCase)
        Dim args = $"/SILENT /SP- /SUPPRESSMSGBOXES /NORESTART /DIR=""{appDir}"" " & If(forAllUsers, "/ALLUSERS", "/CURRENTUSER")
        Process.Start(New ProcessStartInfo(setupPath, args) With {.UseShellExecute = True})
    End Sub

    Public Function DownloadFolder() As String
        Dim dir = Path.Combine(Path.GetTempPath(), "Tumbler-update")
        Directory.CreateDirectory(dir)
        Return dir
    End Function

End Module

''' <summary>"A new version is available": what's new, then download and install.</summary>
Public Class UpdateDialog
    Inherits Form

    Private ReadOnly _release As ReleaseInfo
    Private ReadOnly _checker As UpdateChecker
    Private ReadOnly _notes As New TextBox With {
        .Multiline = True, .ReadOnly = True, .ScrollBars = ScrollBars.Vertical, .Dock = DockStyle.Fill,
        .BackColor = Color.White, .BorderStyle = BorderStyle.FixedSingle}
    Private ReadOnly _progress As New ProgressBar With {.Dock = DockStyle.Bottom, .Height = 8, .Visible = False, .Maximum = 1000}
    Private ReadOnly _status As New Label With {.Dock = DockStyle.Bottom, .AutoSize = False, .Height = 24, .TextAlign = ContentAlignment.MiddleLeft}
    Private ReadOnly _update As New Button With {.Text = "Update now", .AutoSize = True, .MinimumSize = New Size(110, 30)}
    Private ReadOnly _skip As New Button With {.Text = "Skip this version", .AutoSize = True, .MinimumSize = New Size(110, 30)}
    Private ReadOnly _later As New Button With {.Text = "Later", .AutoSize = True, .MinimumSize = New Size(90, 30)}
    Private _cancel As CancellationTokenSource

    ''' <summary>The downloaded setup, ready to run, once the dialog closes with OK.</summary>
    Public Property SetupPath As String

    ''' <summary>True if the person chose "Skip this version".</summary>
    Public Property Skipped As Boolean

    Public Sub New(release As ReleaseInfo, checker As UpdateChecker)
        _release = release
        _checker = checker
        SuspendLayout()
        AutoScaleDimensions = New SizeF(96.0F, 96.0F)
        AutoScaleMode = AutoScaleMode.Dpi
        Font = New Font("Segoe UI", 9.75F)
        Text = "Tumbler update"
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False
        ShowInTaskbar = False
        StartPosition = FormStartPosition.CenterParent
        ClientSize = New Size(480, 340)
        Padding = New Padding(16)

        Dim heading As New Label With {
            .Text = $"Tumbler {VersionText(release.Version)} is available",
            .Font = New Font(Font.FontFamily, 13.0F, FontStyle.Bold), .Dock = DockStyle.Top, .Height = 32}
        Dim subheading As New Label With {
            .Text = $"You have version {VersionText(checker.CurrentVersion)}. What's new:", .Dock = DockStyle.Top, .Height = 26,
            .ForeColor = Color.FromArgb(96, 106, 130)}
        _notes.Text = If(String.IsNullOrWhiteSpace(release.Notes), "Improvements and fixes.",
                         release.Notes.Replace(vbCr, "").Replace(vbLf, vbCrLf))

        Dim buttons As New FlowLayoutPanel With {
            .Dock = DockStyle.Bottom, .FlowDirection = FlowDirection.RightToLeft, .AutoSize = True, .Padding = New Padding(0, 8, 0, 0)}
        buttons.Controls.AddRange({_later, _update, _skip})
        AddHandler _update.Click, AddressOf OnUpdateClick
        AddHandler _later.Click, Sub() Close()
        AddHandler _skip.Click, Sub()
                                    Skipped = True
                                    Close()
                                End Sub
        AcceptButton = _update
        CancelButton = _later

        Controls.Add(_notes)
        Controls.Add(_status)
        Controls.Add(_progress)
        Controls.Add(buttons)
        Controls.Add(subheading)
        Controls.Add(heading)
        ResumeLayout(True)
    End Sub

    Private Shared Function VersionText(v As Version) As String
        Return $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}"
    End Function

    Private Async Sub OnUpdateClick(sender As Object, e As EventArgs)
        _update.Enabled = False
        _skip.Enabled = False
        _later.Text = "Cancel"
        _progress.Visible = True
        _status.Text = "Downloading…"
        _cancel = New CancellationTokenSource()
        Dim target = Path.Combine(DownloadFolder(), _release.SetupName)
        Try
            Await _checker.DownloadAsync(_release, target,
                                         New Progress(Of Double)(Sub(p) _progress.Value = CInt(p * 1000)), _cancel.Token)
            If Not SignedBySamePublisher(target) Then
                File.Delete(target)
                Dim answer = MessageBox.Show(Me,
                    "This download isn't signed by the same publisher as your copy of Tumbler, so Tumbler won't run it." &
                    vbCrLf & vbCrLf & "Open the release page on GitHub instead?", "Tumbler update",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning)
                If answer = DialogResult.Yes Then OpenPage()
                Close()
                Return
            End If
            SetupPath = target
            DialogResult = DialogResult.OK
            Close()
        Catch ex As OperationCanceledException
            Close()
        Catch ex As Exception
            _progress.Visible = False
            _status.Text = "The download failed: " & ex.Message
            _update.Text = "Try again"
            _update.Enabled = True
            _skip.Enabled = True
            _later.Text = "Later"
        End Try
    End Sub

    Private Sub OpenPage()
        Try
            Process.Start(New ProcessStartInfo(_release.PageUrl) With {.UseShellExecute = True})
        Catch
        End Try
    End Sub

    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        _cancel?.Cancel()
        MyBase.OnFormClosing(e)
    End Sub

End Class
