Imports System.IO
Imports System.Net
Imports System.Net.Http
Imports System.Threading
Imports Xunit

Public Class UpdateTests

    Private Const SampleRelease As String = "{
      ""tag_name"": ""v1.4.2"", ""name"": ""Tumbler 1.4.2"", ""draft"": false, ""prerelease"": false,
      ""html_url"": ""https://github.com/pacikalus/tumbler/releases/tag/v1.4.2"",
      ""body"": ""Lines can be moved."",
      ""assets"": [
        { ""name"": ""notes.txt"", ""size"": 10, ""browser_download_url"": ""https://example.test/notes.txt"" },
        { ""name"": ""Tumbler-Setup-1.4.2.exe"", ""size"": 5, ""browser_download_url"": ""https://example.test/setup.exe"" }
      ]}"

    ''' <summary>Answers requests like GitHub would, without the network.</summary>
    Private NotInheritable Class FakeGitHub
        Inherits HttpMessageHandler
        Public Property LatestJson As String = SampleRelease
        Public Property LatestStatus As HttpStatusCode = HttpStatusCode.OK
        Public Property SetupBytes As Byte() = {1, 2, 3, 4, 5}
        Public Property UserAgent As String

        Protected Overrides Function SendAsync(request As HttpRequestMessage, cancel As CancellationToken) As Task(Of HttpResponseMessage)
            UserAgent = request.Headers.UserAgent.ToString()
            Dim response As HttpResponseMessage
            If request.RequestUri.AbsolutePath.EndsWith("/releases/latest") Then
                response = New HttpResponseMessage(LatestStatus) With {.Content = New StringContent(LatestJson)}
            Else
                response = New HttpResponseMessage(HttpStatusCode.OK) With {.Content = New ByteArrayContent(SetupBytes)}
            End If
            Return Task.FromResult(response)
        End Function
    End Class

    <Fact>
    Public Sub ParseRelease_FindsVersionNotesAndSetup()
        Dim r = UpdateChecker.ParseRelease(SampleRelease)
        Assert.Equal(New Version(1, 4, 2), r.Version)
        Assert.Equal("Tumbler-Setup-1.4.2.exe", r.SetupName)
        Assert.Equal("https://example.test/setup.exe", r.SetupUrl)
        Assert.Equal(5, r.SetupSize)
        Assert.Equal("Lines can be moved.", r.Notes)
    End Sub

    <Fact>
    Public Sub ParseRelease_IgnoresDraftsAndPrereleases()
        Assert.Null(UpdateChecker.ParseRelease(SampleRelease.Replace("""draft"": false", """draft"": true")))
        Assert.Null(UpdateChecker.ParseRelease(SampleRelease.Replace("""prerelease"": false", """prerelease"": true")))
    End Sub

    <Theory>
    <InlineData("v1.4.2", "1.4.2")>
    <InlineData("1.4", "1.4.0")>
    <InlineData("Tumbler 2.0.13", "2.0.13")>
    Public Sub ParseVersion_ReadsTags(tag As String, expected As String)
        Assert.Equal(Version.Parse(expected), UpdateChecker.ParseVersion(tag))
    End Sub

    <Theory>
    <InlineData("1.4.2", "1.4.1", True)>
    <InlineData("1.5.0", "1.4.9", True)>
    <InlineData("1.4.1", "1.4.1.0", False)>
    <InlineData("1.4.0", "1.4.1", False)>
    Public Sub IsNewer_ComparesThreeNumbers(candidate As String, current As String, expected As Boolean)
        Assert.Equal(expected, UpdateChecker.IsNewer(Version.Parse(candidate), Version.Parse(current)))
    End Sub

    <Fact>
    Public Async Function Check_ReturnsNewerRelease_AndDownloadsIt() As Task
        Dim fake As New FakeGitHub()
        Dim checker As New UpdateChecker(New Version(1, 4, 1), fake)
        Dim r = Await checker.CheckAsync()
        Assert.NotNull(r)
        Assert.Contains("Tumbler/1.4.1", fake.UserAgent)

        Dim path = IO.Path.Combine(IO.Path.GetTempPath(), $"tumbler-update-test-{Guid.NewGuid():N}.exe")
        Try
            Dim seen As New List(Of Double)
            Await checker.DownloadAsync(r, path, New Progress(Of Double)(Sub(p) seen.Add(p)))
            Assert.True(fake.SetupBytes.SequenceEqual(File.ReadAllBytes(path)), "downloaded bytes differ")
        Finally
            File.Delete(path)
        End Try
    End Function

    <Fact>
    Public Async Function Check_ReturnsNothing_WhenUpToDateOrNothingPublished() As Task
        Assert.Null(Await New UpdateChecker(New Version(1, 4, 2), New FakeGitHub()).CheckAsync())
        Assert.Null(Await New UpdateChecker(New Version(1, 0, 0), New FakeGitHub With {.LatestStatus = HttpStatusCode.NotFound}).CheckAsync())
        Dim noSetup = SampleRelease.Replace("Tumbler-Setup-1.4.2.exe", "something-else.zip")
        Assert.Null(Await New UpdateChecker(New Version(1, 0, 0), New FakeGitHub With {.LatestJson = noSetup}).CheckAsync())
    End Function

    <Fact>
    Public Async Function Download_RejectsIncompleteFile() As Task
        Dim fake As New FakeGitHub With {.SetupBytes = {1, 2, 3}}
        Dim checker As New UpdateChecker(New Version(1, 0, 0), fake)
        Dim r = Await checker.CheckAsync()
        Dim path = IO.Path.Combine(IO.Path.GetTempPath(), $"tumbler-update-test-{Guid.NewGuid():N}.exe")
        Await Assert.ThrowsAsync(Of IOException)(Function() checker.DownloadAsync(r, path))
        Assert.False(File.Exists(path))
        File.Delete(path & ".part")
    End Function

End Class
