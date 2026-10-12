Module Program

    <STAThread>
    Sub Main(args As String())
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2)
        Application.EnableVisualStyles()
        Application.SetCompatibleTextRenderingDefault(False)
        AddHandler Application.ThreadException, AddressOf OnThreadException

        Dim form As New MainForm()
        If args.Length > 0 Then form.FileToOpenOnStart = args(0)
        Application.Run(form)
    End Sub

    Private Sub OnThreadException(sender As Object, e As Threading.ThreadExceptionEventArgs)
        MessageBox.Show("Something went wrong:" & vbCrLf & vbCrLf & e.Exception.Message,
                        "Tumbler", MessageBoxButtons.OK, MessageBoxIcon.Error)
    End Sub

End Module
