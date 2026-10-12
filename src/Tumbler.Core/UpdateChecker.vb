Imports System.IO
Imports System.Net
Imports System.Net.Http
Imports System.Text.Json
Imports System.Threading

''' <summary>A version of Tumbler published on GitHub, with its setup program.</summary>
Public NotInheritable Class ReleaseInfo
    Public Property Version As Version
    Public Property Tag As String
    Public Property Title As String
    Public Property Notes As String
    Public Property PageUrl As String
    Public Property SetupName As String
    Public Property SetupUrl As String
    Public Property SetupSize As Long
End Class

''' <summary>
''' Looks for a newer Tumbler on GitHub (the repository's latest release) and
''' downloads its setup program. Only the latest published, non-draft,
''' non-prerelease release counts.
''' </summary>
Public NotInheritable Class UpdateChecker

    Public Const Repository As String = "pacikalus/tumbler"
    Private Shared ReadOnly LatestUrl As String = $"https://api.github.com/repos/{Repository}/releases/latest"

    Private ReadOnly _http As HttpClient

    ''' <param name="handler">For tests; leave out to use the network.</param>
    Public Sub New(currentVersion As Version, Optional handler As HttpMessageHandler = Nothing)
        Me.CurrentVersion = currentVersion
        _http = If(handler Is Nothing, New HttpClient(), New HttpClient(handler))
        _http.Timeout = TimeSpan.FromSeconds(30)
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"Tumbler/{currentVersion}")
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json")
    End Sub

    Public ReadOnly Property CurrentVersion As Version

    ''' <summary>
    ''' The newest release if it is newer than this copy and has a setup program
    ''' attached; Nothing when Tumbler is up to date or nothing is published yet.
    ''' Network and GitHub errors are thrown, so the caller decides whether to show them.
    ''' </summary>
    Public Async Function CheckAsync(Optional cancel As CancellationToken = Nothing) As Task(Of ReleaseInfo)
        Using response = Await _http.GetAsync(LatestUrl, cancel).ConfigureAwait(False)
            If response.StatusCode = HttpStatusCode.NotFound Then Return Nothing   ' no releases yet
            response.EnsureSuccessStatusCode()
            Dim json = Await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(False)
            Dim release = ParseRelease(json)
            If release Is Nothing OrElse release.SetupUrl Is Nothing Then Return Nothing
            Return If(IsNewer(release.Version, CurrentVersion), release, Nothing)
        End Using
    End Function

    ''' <summary>Downloads the setup program to <paramref name="path"/>, reporting 0..1 progress.</summary>
    Public Async Function DownloadAsync(release As ReleaseInfo, path As String,
                                        Optional progress As IProgress(Of Double) = Nothing,
                                        Optional cancel As CancellationToken = Nothing) As Task
        Dim partPath = path & ".part"
        Using response = Await _http.GetAsync(release.SetupUrl, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(False)
            response.EnsureSuccessStatusCode()
            Dim total = If(response.Content.Headers.ContentLength, release.SetupSize)
            Using source = Await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(False),
                  target = File.Create(partPath)
                Dim buffer(81919) As Byte
                Dim done As Long = 0
                Do
                    Dim n = Await source.ReadAsync(buffer, 0, buffer.Length, cancel).ConfigureAwait(False)
                    If n = 0 Then Exit Do
                    Await target.WriteAsync(buffer, 0, n, cancel).ConfigureAwait(False)
                    done += n
                    If total > 0 Then progress?.Report(Math.Min(1.0, done / CDbl(total)))
                Loop
                If release.SetupSize > 0 AndAlso done <> release.SetupSize Then
                    Throw New IOException($"The download was incomplete ({done:N0} of {release.SetupSize:N0} bytes).")
                End If
            End Using
        End Using
        File.Move(partPath, path, overwrite:=True)
    End Function

    ''' <summary>Reads GitHub's release JSON. Nothing for drafts, prereleases or unreadable tags.</summary>
    Public Shared Function ParseRelease(json As String) As ReleaseInfo
        Using doc = JsonDocument.Parse(json)
            Dim root = doc.RootElement
            If Bool(root, "draft") OrElse Bool(root, "prerelease") Then Return Nothing
            Dim tag = Str(root, "tag_name")
            Dim version = ParseVersion(tag)
            If version Is Nothing Then Return Nothing
            Dim release As New ReleaseInfo With {
                .Version = version, .Tag = tag, .Title = Str(root, "name"),
                .Notes = Str(root, "body"), .PageUrl = Str(root, "html_url")}
            Dim assets As JsonElement
            If root.TryGetProperty("assets", assets) AndAlso assets.ValueKind = JsonValueKind.Array Then
                For Each a In assets.EnumerateArray()
                    Dim name = Str(a, "name")
                    If IsSetupName(name) Then
                        release.SetupName = name
                        release.SetupUrl = Str(a, "browser_download_url")
                        Dim size As JsonElement
                        If a.TryGetProperty("size", size) AndAlso size.ValueKind = JsonValueKind.Number Then release.SetupSize = size.GetInt64()
                        Exit For
                    End If
                Next
            End If
            Return release
        End Using
    End Function

    ''' <summary>"v1.4.2", "1.4.2" or "Tumbler 1.4" → a version; Nothing if there isn't one.</summary>
    Public Shared Function ParseVersion(tag As String) As Version
        If String.IsNullOrWhiteSpace(tag) Then Return Nothing
        Dim m = Text.RegularExpressions.Regex.Match(tag, "(\d+)\.(\d+)(?:\.(\d+))?")
        If Not m.Success Then Return Nothing
        Return New Version(CInt(m.Groups(1).Value), CInt(m.Groups(2).Value),
                           If(m.Groups(3).Success, CInt(m.Groups(3).Value), 0))
    End Function

    ''' <summary>Compares major.minor.patch only (the 4th "revision" number .NET adds is ignored).</summary>
    Public Shared Function IsNewer(candidate As Version, current As Version) As Boolean
        Dim a = New Version(candidate.Major, candidate.Minor, Math.Max(0, candidate.Build))
        Dim b = New Version(current.Major, current.Minor, Math.Max(0, current.Build))
        Return a > b
    End Function

    Public Shared Function IsSetupName(name As String) As Boolean
        Return name IsNot Nothing AndAlso
               name.StartsWith("Tumbler-Setup-", StringComparison.OrdinalIgnoreCase) AndAlso
               name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
    End Function

    Private Shared Function Str(e As JsonElement, name As String) As String
        Dim v As JsonElement
        Return If(e.TryGetProperty(name, v) AndAlso v.ValueKind = JsonValueKind.String, v.GetString(), Nothing)
    End Function

    Private Shared Function Bool(e As JsonElement, name As String) As Boolean
        Dim v As JsonElement
        Return e.TryGetProperty(name, v) AndAlso v.ValueKind = JsonValueKind.True
    End Function

End Class
