Imports System.Runtime.InteropServices

''' <summary>
''' The few PDFium calls Tumbler makes itself, for drawing pages.
''' Docnet.Core's own page rendering sets up a PDFium "form" environment with
''' memory it frees too early, which crashes the app after a few edits; we call
''' PDFium directly instead. The DLL is the one Docnet.Core ships.
''' </summary>
Friend Module PdfiumNative

    Private Const PdfiumDll = "pdfium"

    Friend Const FPDF_ANNOT As Integer = &H1

    <DllImport(PdfiumDll, CallingConvention:=CallingConvention.Cdecl)>
    Friend Function FPDF_LoadMemDocument(data As IntPtr, size As Integer, <MarshalAs(UnmanagedType.LPStr)> password As String) As IntPtr
    End Function

    <DllImport(PdfiumDll, CallingConvention:=CallingConvention.Cdecl)>
    Friend Sub FPDF_CloseDocument(document As IntPtr)
    End Sub

    <DllImport(PdfiumDll, CallingConvention:=CallingConvention.Cdecl)>
    Friend Function FPDF_LoadPage(document As IntPtr, pageIndex As Integer) As IntPtr
    End Function

    <DllImport(PdfiumDll, CallingConvention:=CallingConvention.Cdecl)>
    Friend Sub FPDF_ClosePage(page As IntPtr)
    End Sub

    <DllImport(PdfiumDll, CallingConvention:=CallingConvention.Cdecl)>
    Friend Function FPDF_GetPageWidth(page As IntPtr) As Double
    End Function

    <DllImport(PdfiumDll, CallingConvention:=CallingConvention.Cdecl)>
    Friend Function FPDF_GetPageHeight(page As IntPtr) As Double
    End Function

    <DllImport(PdfiumDll, CallingConvention:=CallingConvention.Cdecl)>
    Friend Function FPDFBitmap_Create(width As Integer, height As Integer, alpha As Integer) As IntPtr
    End Function

    <DllImport(PdfiumDll, CallingConvention:=CallingConvention.Cdecl)>
    Friend Sub FPDFBitmap_FillRect(bitmap As IntPtr, left As Integer, top As Integer, width As Integer, height As Integer, color As UInteger)
    End Sub

    <DllImport(PdfiumDll, CallingConvention:=CallingConvention.Cdecl)>
    Friend Function FPDFBitmap_GetBuffer(bitmap As IntPtr) As IntPtr
    End Function

    <DllImport(PdfiumDll, CallingConvention:=CallingConvention.Cdecl)>
    Friend Function FPDFBitmap_GetStride(bitmap As IntPtr) As Integer
    End Function

    <DllImport(PdfiumDll, CallingConvention:=CallingConvention.Cdecl)>
    Friend Sub FPDFBitmap_Destroy(bitmap As IntPtr)
    End Sub

    <DllImport(PdfiumDll, CallingConvention:=CallingConvention.Cdecl)>
    Friend Sub FPDF_RenderPageBitmap(bitmap As IntPtr, page As IntPtr, startX As Integer, startY As Integer,
                                     sizeX As Integer, sizeY As Integer, rotate As Integer, flags As Integer)
    End Sub

    Friend Const FPDF_ANNOT_FLAG_HIDDEN As Integer = &H2

    <DllImport(PdfiumDll, CallingConvention:=CallingConvention.Cdecl)>
    Friend Function FPDFPage_GetAnnot(page As IntPtr, index As Integer) As IntPtr
    End Function

    <DllImport(PdfiumDll, CallingConvention:=CallingConvention.Cdecl)>
    Friend Sub FPDFPage_CloseAnnot(annot As IntPtr)
    End Sub

    <DllImport(PdfiumDll, CallingConvention:=CallingConvention.Cdecl)>
    Friend Function FPDFAnnot_GetFlags(annot As IntPtr) As Integer
    End Function

    <DllImport(PdfiumDll, CallingConvention:=CallingConvention.Cdecl)>
    Friend Function FPDFAnnot_SetFlags(annot As IntPtr, flags As Integer) As Integer
    End Function

    <DllImport(PdfiumDll, CallingConvention:=CallingConvention.Cdecl)>
    Private Function FPDF_SaveAsCopy(document As IntPtr, fileWrite As IntPtr, flags As Integer) As Integer
    End Function

    Private Const FPDF_REMOVE_SECURITY As Integer = 3

    <UnmanagedFunctionPointer(CallingConvention.Cdecl)>
    Private Delegate Function WriteBlockCallback(self As IntPtr, data As IntPtr, size As UIntPtr) As Integer

    ''' <summary>
    ''' Has PDFium read a PDF and write it out again. PDFium copes with damaged
    ''' files (broken cross-reference tables, bad stream lengths) that PDFsharp
    ''' refuses, and the copy it writes is clean and unlocked. Returns Nothing
    ''' if PDFium cannot read the file either.
    ''' </summary>
    Friend Function Rewrite(bytes As Byte(), password As String) As Byte()
        Dim init = Docnet.Core.DocLib.Instance   ' makes sure PDFium is initialised
        Dim data = Marshal.AllocHGlobal(Math.Max(1, bytes.Length))
        Dim fileWrite = IntPtr.Zero
        Try
            Marshal.Copy(bytes, 0, data, bytes.Length)
            Dim doc = FPDF_LoadMemDocument(data, bytes.Length, password)
            If doc = IntPtr.Zero Then Return Nothing
            Try
                Using output As New IO.MemoryStream()
                    Dim callback As WriteBlockCallback =
                        Function(self, block, size)
                            ' "unsigned long" is 32 bits on Windows; ignore whatever is above.
                            Dim count = CInt(size.ToUInt64() And &HFFFFFFFFUL)
                            If count > 0 Then
                                Dim buffer(count - 1) As Byte
                                Marshal.Copy(block, buffer, 0, count)
                                output.Write(buffer, 0, count)
                            End If
                            Return 1
                        End Function
                    ' struct FPDF_FILEWRITE { int version; int (*WriteBlock)(...); }
                    fileWrite = Marshal.AllocHGlobal(IntPtr.Size * 2)
                    Marshal.WriteIntPtr(fileWrite, 0, IntPtr.Zero)
                    Marshal.WriteInt32(fileWrite, 0, 1)
                    Marshal.WriteIntPtr(fileWrite, IntPtr.Size, Marshal.GetFunctionPointerForDelegate(callback))
                    Dim ok = FPDF_SaveAsCopy(doc, fileWrite, FPDF_REMOVE_SECURITY)
                    GC.KeepAlive(callback)
                    If ok = 0 OrElse output.Length = 0 Then Return Nothing
                    Return output.ToArray()
                End Using
            Finally
                FPDF_CloseDocument(doc)
            End Try
        Finally
            If fileWrite <> IntPtr.Zero Then Marshal.FreeHGlobal(fileWrite)
            Marshal.FreeHGlobal(data)
        End Try
    End Function

End Module
