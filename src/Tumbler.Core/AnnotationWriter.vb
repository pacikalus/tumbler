Imports System.Drawing
Imports System.Globalization
Imports System.IO
Imports System.Text
Imports PdfSharp.Pdf
Imports PdfSharp.Pdf.Advanced

''' <summary>
''' Builds annotation dictionaries, including their appearance streams (/AP),
''' so every PDF viewer draws them the same way Tumbler does.
''' </summary>
Friend Module AnnotationWriter

    Friend Const NoteSize As Double = 22
    Private Const TextPadding As Double = 4
    Private Const LineSpacing As Double = 1.2

    Private ReadOnly Inv As CultureInfo = CultureInfo.InvariantCulture

    ' Helvetica advance widths for WinAnsi codes 32..255, per 1000 em.
    Private ReadOnly HelveticaWidths As Integer() = {
        278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278, 556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556,
        1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778, 667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556,
        333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556, 556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584, 0,
        556, 0, 222, 556, 333, 1000, 556, 556, 333, 1000, 667, 333, 1000, 0, 611, 0, 0, 222, 222, 333, 333, 350, 556, 1000, 333, 1000, 500, 333, 944, 0, 500, 667,
        278, 333, 556, 556, 556, 556, 260, 556, 333, 737, 370, 556, 584, 333, 737, 333, 400, 584, 333, 333, 333, 556, 537, 278, 333, 333, 365, 556, 834, 834, 834, 611,
        667, 667, 667, 667, 667, 667, 1000, 722, 667, 667, 667, 667, 278, 278, 278, 278, 722, 722, 778, 778, 778, 778, 778, 584, 778, 722, 722, 722, 722, 667, 667, 611,
        556, 556, 556, 556, 556, 556, 889, 500, 556, 556, 556, 556, 278, 278, 278, 278, 556, 556, 556, 556, 556, 556, 556, 584, 611, 556, 556, 556, 556, 500, 556, 500}

    Private ReadOnly WinAnsi As Encoding = CreateWinAnsi()

    Private Function CreateWinAnsi() As Encoding
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)
        Return Encoding.GetEncoding(1252, New EncoderReplacementFallback("?"), New DecoderReplacementFallback("?"))
    End Function

#Region "Creating annotations"

    Friend Function CreateNote(doc As PdfDocument, geo As PageGeometry, displayTopLeft As PointF,
                               text As String, color As Color, author As String) As PdfDictionary
        ' Keep the icon inside the page.
        Dim u = Math.Max(0, Math.Min(displayTopLeft.X, geo.DisplayWidth - NoteSize))
        Dim v = Math.Max(0, Math.Min(displayTopLeft.Y, geo.DisplayHeight - NoteSize))
        ' Notes carry the NoRotate flag: viewers keep the icon upright and pin its
        ' upper-left corner (in unrotated page space) to the clicked point.
        Dim corner = geo.ToPdf(u, v)
        Dim rect = PdfRect.FromCorners(corner.X, corner.Y - NoteSize, corner.X + NoteSize, corner.Y)

        Dim annot = NewAnnotation(doc, "/Text", rect, text, color, author)
        annot.Elements.SetName("/Name", "/Comment")
        annot.Elements.SetInteger("/F", 4 Or 8 Or 16) ' Print, NoZoom, NoRotate
        annot.Elements("/C") = ColorArray(doc, color)
        SetAppearance(doc, annot, BuildNoteAppearance(doc, color))
        Return annot
    End Function

    ''' <summary>
    ''' Creates a visible comment: just the text, written on the page in the
    ''' chosen colour, with no box or background. Its area is sized to fit the text.
    ''' </summary>
    Friend Function CreateComment(doc As PdfDocument, geo As PageGeometry, displayTopLeft As PointF,
                                  text As String, color As Color, fontSize As Double, author As String) As PdfDictionary
        Dim box = CommentBox(geo, New RectangleF(displayTopLeft, SizeF.Empty), text, fontSize, keepWidth:=False)
        Dim annot = NewAnnotation(doc, "/FreeText", geo.ToPdfRect(box), text, color, author)
        annot.Elements.SetInteger("/F", 4)
        annot.Elements.SetString("/DA", DefaultAppearance(color, fontSize))
        annot.Elements.SetInteger("/Q", 0)
        annot.Elements(CommentColorKey) = ColorArray(doc, color)
        Dim bs As New PdfDictionary(doc)
        bs.Elements.SetInteger("/W", 0)
        annot.Elements("/BS") = bs
        SetAppearance(doc, annot, BuildTextBoxAppearance(doc, geo.Rotation, box.Width, box.Height, text, color, fontSize, border:=False))
        Return annot
    End Function

    ''' <summary>Our own key marking a comment (as opposed to a text box) and holding its colour.</summary>
    Friend Const CommentColorKey As String = "/TumblerColor"
    Private Const CommentMaxWidth As Double = 280
    Private Const CommentMinWidth As Double = 60

    ''' <summary>Size and place a comment box so its text fits and it stays on the page.</summary>
    Private Function CommentBox(geo As PageGeometry, current As RectangleF, text As String, fontSize As Double, keepWidth As Boolean) As RectangleF
        Dim width As Double
        If keepWidth AndAlso current.Width > 0 Then
            width = current.Width
        Else
            Dim natural = 0.0
            For Each para In If(text, "").Replace(vbCr, "").Split(ChrW(10))
                natural = Math.Max(natural, MeasureText(para, fontSize))
            Next
            width = Math.Max(CommentMinWidth, Math.Min(CommentMaxWidth, natural + 2 * TextPadding + 2))
        End If
        width = Math.Min(width, geo.DisplayWidth)
        Dim lines = WrapText(text, fontSize, width - 2 * TextPadding).Count
        Dim height = 2 * TextPadding + fontSize * 1.05 + (lines - 1) * fontSize * LineSpacing
        height = Math.Min(height, geo.DisplayHeight)
        Dim x = Math.Max(0, Math.Min(current.X, geo.DisplayWidth - width))
        Dim y = Math.Max(0, Math.Min(current.Y, geo.DisplayHeight - height))
        Return New RectangleF(CSng(x), CSng(y), CSng(width), CSng(height))
    End Function

    Friend Function CreateTextBox(doc As PdfDocument, geo As PageGeometry, displayRect As RectangleF,
                                  text As String, color As Color, fontSize As Double, author As String) As PdfDictionary
        Dim rect = geo.ToPdfRect(displayRect)
        Dim annot = NewAnnotation(doc, "/FreeText", rect, text, color, author)
        annot.Elements.SetInteger("/F", 4)
        annot.Elements.SetString("/DA", DefaultAppearance(color, fontSize))
        annot.Elements.SetInteger("/Q", 0)
        Dim bs As New PdfDictionary(doc)
        bs.Elements.SetInteger("/W", 1)
        annot.Elements("/BS") = bs
        SetAppearance(doc, annot, BuildTextBoxAppearance(doc, geo.Rotation, displayRect.Width, displayRect.Height, text, color, fontSize))
        Return annot
    End Function

    ''' <summary>Creates a highlight from line quads in display space.</summary>
    Friend Function CreateHighlight(doc As PdfDocument, geo As PageGeometry, displayQuads As IEnumerable(Of HighlightQuad),
                                    color As Color, author As String, note As String) As PdfDictionary
        Dim quads As New List(Of Double)
        Dim bounds As PdfRect? = Nothing
        For Each q In displayQuads
            ' QuadPoints order used by Acrobat: upper-left, upper-right, lower-left, lower-right.
            For Each corner In {q.UpperLeft, q.UpperRight, q.LowerLeft, q.LowerRight}
                Dim p = geo.ToPdf(corner)
                quads.Add(p.X)
                quads.Add(p.Y)
            Next
            Dim r = geo.ToPdfRect(q.Bounds)
            bounds = If(bounds.HasValue, bounds.Value.Union(r), r)
        Next
        If quads.Count = 0 Then Throw New ArgumentException("Nothing to highlight.")

        Dim annot = NewAnnotation(doc, "/Highlight", bounds.Value.Inflate(1), note, color, author)
        annot.Elements.SetInteger("/F", 4)
        annot.Elements("/C") = ColorArray(doc, color)
        Dim qp As New PdfArray(doc)
        For Each q In quads
            qp.Elements.Add(New PdfReal(Math.Round(q, 3)))
        Next
        annot.Elements("/QuadPoints") = qp
        SetAppearance(doc, annot, BuildHighlightAppearance(doc, bounds.Value.Inflate(1), quads, color))
        Return annot
    End Function

    ''' <summary>A straight line between two points (display space, points).</summary>
    Friend Function CreateLine(doc As PdfDocument, geo As PageGeometry, fromPoint As PointF, toPoint As PointF,
                               color As Color, width As Double, author As String) As PdfDictionary
        Dim a = geo.ToPdf(fromPoint), b = geo.ToPdf(toPoint)
        Dim ends As New List(Of Double) From {a.X, a.Y, b.X, b.Y}
        Dim annot = NewAnnotation(doc, "/Line", LineRect(ends, width), "", color, author)
        annot.Elements.SetInteger("/F", 4)
        annot.Elements("/C") = ColorArray(doc, color)
        Dim l As New PdfArray(doc)
        For Each v In ends
            l.Elements.Add(New PdfReal(Math.Round(v, 3)))
        Next
        annot.Elements("/L") = l
        Dim bs As New PdfDictionary(doc)
        bs.Elements.SetReal("/W", width)
        bs.Elements.SetName("/S", "/S")
        annot.Elements("/BS") = bs
        Dim le As New PdfArray(doc)
        le.Elements.Add(New PdfName("/None"))
        le.Elements.Add(New PdfName("/None"))
        annot.Elements("/LE") = le
        SetAppearance(doc, annot, BuildLineAppearance(doc, ends, width, color))
        Return annot
    End Function

    Private Function LineRect(ends As IList(Of Double), width As Double) As PdfRect
        Return PdfRect.FromCorners(Math.Min(ends(0), ends(2)), Math.Min(ends(1), ends(3)),
                                   Math.Max(ends(0), ends(2)), Math.Max(ends(1), ends(3))).Inflate(width / 2 + 1)
    End Function

    Friend Function ReadLineWidth(annot As PdfDictionary) As Double
        Dim bs = TryCast(annot.Elements.GetObject("/BS"), PdfDictionary)
        If bs Is Nothing OrElse Not bs.Elements.ContainsKey("/W") Then Return 1
        Return bs.Elements.GetReal("/W")
    End Function

    Private Function NewAnnotation(doc As PdfDocument, subtype As String, rect As PdfRect,
                                   text As String, color As Color, author As String) As PdfDictionary
        Dim annot As New PdfDictionary(doc)
        annot.Elements.SetName("/Type", "/Annot")
        annot.Elements.SetName("/Subtype", subtype)
        annot.Elements("/Rect") = RectArray(doc, rect)
        annot.Elements("/Contents") = New PdfString(If(text, ""), PdfStringEncoding.Unicode)
        annot.Elements("/T") = New PdfString(If(author, ""), PdfStringEncoding.Unicode)
        annot.Elements("/NM") = New PdfString(Guid.NewGuid().ToString("D"))
        Dim now = PdfDate(Date.Now)
        annot.Elements("/M") = New PdfString(now)
        annot.Elements("/CreationDate") = New PdfString(now)
        annot.Elements.SetReal("/CA", 1)
        Return annot
    End Function

#End Region

#Region "Updating annotations"

    ''' <summary>Changes text and colour of an annotation Tumbler understands, and rebuilds its appearance.</summary>
    Friend Sub Update(doc As PdfDocument, geo As PageGeometry, annot As PdfDictionary, kind As AnnotationKind,
                      text As String, color As Color, fontSize As Double)
        annot.Elements("/Contents") = New PdfString(If(text, ""), PdfStringEncoding.Unicode)
        annot.Elements("/M") = New PdfString(PdfDate(Date.Now))

        Select Case kind
            Case AnnotationKind.Note
                annot.Elements("/C") = ColorArray(doc, color)
                SetAppearance(doc, annot, BuildNoteAppearance(doc, color))

            Case AnnotationKind.Comment
                If fontSize <= 0 Then fontSize = ReadFontSize(annot)
                annot.Elements.SetString("/DA", DefaultAppearance(color, fontSize))
                annot.Elements.Remove("/C")
                annot.Elements(CommentColorKey) = ColorArray(doc, color)
                ' Keep the top-left corner and width; grow or shrink to fit the new text.
                Dim box = CommentBox(geo, geo.ToDisplayRect(ReadRect(annot)), text, fontSize, keepWidth:=True)
                annot.Elements("/Rect") = RectArray(doc, geo.ToPdfRect(box))
                SetAppearance(doc, annot, BuildTextBoxAppearance(doc, geo.Rotation, box.Width, box.Height, text, color, fontSize, border:=False))

            Case AnnotationKind.TextBox
                If fontSize <= 0 Then fontSize = ReadFontSize(annot)
                annot.Elements.SetString("/DA", DefaultAppearance(color, fontSize))
                Dim d = geo.ToDisplayRect(ReadRect(annot))
                SetAppearance(doc, annot, BuildTextBoxAppearance(doc, geo.Rotation, d.Width, d.Height, text, color, fontSize))

            Case AnnotationKind.Line
                annot.Elements("/C") = ColorArray(doc, color)
                Dim ends = ReadNumbers(annot.Elements.GetArray("/L"))
                If ends.Count >= 4 Then
                    SetAppearance(doc, annot, BuildLineAppearance(doc, ends, ReadLineWidth(annot), color))
                End If

            Case AnnotationKind.Highlight
                annot.Elements("/C") = ColorArray(doc, color)
                Dim quads = ReadNumbers(annot.Elements.GetArray("/QuadPoints"))
                If quads.Count >= 8 Then
                    SetAppearance(doc, annot, BuildHighlightAppearance(doc, ReadRect(annot), quads, color))
                End If
        End Select
    End Sub

#End Region

#Region "Appearance streams"

    Private Sub SetAppearance(doc As PdfDocument, annot As PdfDictionary, form As PdfDictionary)
        Dim ap As New PdfDictionary(doc)
        ap.Elements.SetReference("/N", form)
        annot.Elements("/AP") = ap
    End Sub

    Private Function NewForm(doc As PdfDocument, bbox As PdfRect, rotation As Integer,
                             content As String, resources As PdfDictionary) As PdfDictionary
        Dim form As New PdfDictionary(doc)
        form.Elements.SetName("/Type", "/XObject")
        form.Elements.SetName("/Subtype", "/Form")
        form.Elements("/BBox") = RectArray(doc, bbox)
        If rotation <> 0 Then
            ' Counter-rotate so the appearance stays upright on a rotated page.
            Dim rad = rotation * Math.PI / 180
            Dim c = Math.Round(Math.Cos(rad)), s = Math.Round(Math.Sin(rad))
            form.Elements("/Matrix") = NumberArray(doc, c, s, -s, c, 0, 0)
        End If
        form.Elements("/Resources") = If(resources, New PdfDictionary(doc))
        form.CreateStream(WinAnsi.GetBytes(content))
        doc.Internals.AddObject(form)
        Return form
    End Function

    Private Function BuildNoteAppearance(doc As PdfDocument, color As Color) As PdfDictionary
        Dim s = NoteSize
        Dim sb As New StringBuilder()
        sb.AppendLine("q")
        ' Speech-bubble shaped note: coloured body with a dark outline.
        sb.AppendLine($"{Rgb(color)} rg 0.2 0.2 0.2 RG 1 w")
        sb.AppendLine($"{N(1)} {N(5)} m {N(s - 1)} {N(5)} l {N(s - 1)} {N(s - 1)} l {N(1)} {N(s - 1)} l h")
        sb.AppendLine($"{N(5)} {N(5)} m {N(4)} {N(1)} l {N(10)} {N(5)} l h B")
        ' Text lines.
        sb.AppendLine("0.15 0.15 0.15 RG 1.2 w")
        For Each y In {s - 6, s - 10, s - 14}
            sb.AppendLine($"{N(5)} {N(y)} m {N(If(y = s - 14, s - 9, s - 5))} {N(y)} l S")
        Next
        sb.AppendLine("Q")
        Return NewForm(doc, New PdfRect With {.X2 = s, .Y2 = s}, 0, sb.ToString(), Nothing)
    End Function

    Private Function BuildHighlightAppearance(doc As PdfDocument, bbox As PdfRect, quads As IList(Of Double), color As Color) As PdfDictionary
        Dim sb As New StringBuilder()
        sb.AppendLine("q /GS0 gs")
        sb.AppendLine($"{Rgb(color)} rg")
        For i = 0 To quads.Count - 8 Step 8
            ' ul, ur, ll, lr -> draw ul, ur, lr, ll
            sb.AppendLine($"{N(quads(i))} {N(quads(i + 1))} m {N(quads(i + 2))} {N(quads(i + 3))} l " &
                          $"{N(quads(i + 6))} {N(quads(i + 7))} l {N(quads(i + 4))} {N(quads(i + 5))} l h f")
        Next
        sb.AppendLine("Q")

        Dim gs As New PdfDictionary(doc)
        gs.Elements.SetName("/Type", "/ExtGState")
        gs.Elements.SetName("/BM", "/Multiply")
        Dim states As New PdfDictionary(doc)
        states.Elements("/GS0") = gs
        Dim res As New PdfDictionary(doc)
        res.Elements("/ExtGState") = states

        ' Without /Matrix and with BBox = Rect, form space is page space,
        ' so the quad points can be used as they are.
        Return NewForm(doc, bbox, 0, sb.ToString(), res)
    End Function

    Private Function BuildLineAppearance(doc As PdfDocument, ends As IList(Of Double), width As Double, color As Color) As PdfDictionary
        ' Round caps, so short strokes and joins with other lines look neat.
        Dim content = $"q {Rgb(color)} RG {N(width)} w 1 J {N(ends(0))} {N(ends(1))} m {N(ends(2))} {N(ends(3))} l S Q"
        ' Like highlights: BBox = Rect and no /Matrix, so form space is page space.
        Return NewForm(doc, LineRect(ends, width), 0, content, New PdfDictionary(doc))
    End Function

    Private Function BuildTextBoxAppearance(doc As PdfDocument, rotation As Integer, width As Double, height As Double,
                                            text As String, color As Color, fontSize As Double,
                                            Optional border As Boolean = True) As PdfDictionary
        Dim sb As New StringBuilder()
        sb.AppendLine("q")
        If border Then
            sb.AppendLine($"{Rgb(color)} RG 1 w 0.5 0.5 {N(width - 1)} {N(height - 1)} re S")
        End If
        sb.AppendLine($"{N(1)} {N(1)} {N(width - 2)} {N(height - 2)} re W n")
        sb.AppendLine("BT")
        sb.AppendLine($"/Helv {N(fontSize)} Tf {Rgb(color)} rg")
        Dim leading = fontSize * LineSpacing
        sb.AppendLine($"{N(leading)} TL")
        sb.AppendLine($"{N(TextPadding)} {N(height - TextPadding - fontSize * 0.8)} Td")
        Dim first = True
        For Each line In WrapText(text, fontSize, width - 2 * TextPadding)
            If Not first Then sb.Append("T* ")
            sb.AppendLine($"({EscapeString(line)}) Tj")
            first = False
        Next
        sb.AppendLine("ET Q")

        Dim font As New PdfDictionary(doc)
        font.Elements.SetName("/Type", "/Font")
        font.Elements.SetName("/Subtype", "/Type1")
        font.Elements.SetName("/BaseFont", "/Helvetica")
        font.Elements.SetName("/Encoding", "/WinAnsiEncoding")
        Dim fonts As New PdfDictionary(doc)
        fonts.Elements("/Helv") = font
        Dim res As New PdfDictionary(doc)
        res.Elements("/Font") = fonts

        Return NewForm(doc, New PdfRect With {.X2 = width, .Y2 = height}, rotation, sb.ToString(), res)
    End Function

#End Region

#Region "Text layout"

    Friend Function MeasureText(s As String, fontSize As Double) As Double
        Dim total = 0
        For Each b In WinAnsi.GetBytes(s)
            If b >= 32 Then total += HelveticaWidths(b - 32)
        Next
        Return total * fontSize / 1000.0
    End Function

    ''' <summary>Word-wraps text to a width, honouring explicit line breaks.</summary>
    Friend Function WrapText(text As String, fontSize As Double, maxWidth As Double) As List(Of String)
        Dim result As New List(Of String)
        Dim paragraphs = If(text, "").Replace(vbCrLf, vbLf).Replace(vbCr, vbLf).Split(ChrW(10))
        For Each para In paragraphs
            Dim line = ""
            For Each word In para.Split(" "c)
                Dim candidate = If(line.Length = 0, word, line & " " & word)
                If MeasureText(candidate, fontSize) <= maxWidth OrElse line.Length = 0 Then
                    line = candidate
                Else
                    result.Add(line)
                    line = word
                End If
                ' Break single words that are wider than the box.
                While MeasureText(line, fontSize) > maxWidth AndAlso line.Length > 1
                    Dim cut = line.Length - 1
                    While cut > 1 AndAlso MeasureText(line.Substring(0, cut), fontSize) > maxWidth
                        cut -= 1
                    End While
                    result.Add(line.Substring(0, cut))
                    line = line.Substring(cut)
                End While
            Next
            result.Add(line)
        Next
        Return result
    End Function

    Private Function EscapeString(s As String) As String
        Dim sb As New StringBuilder()
        For Each b In WinAnsi.GetBytes(s)
            Select Case b
                Case AscW("("c), AscW(")"c), AscW("\"c)
                    sb.Append("\"c).Append(ChrW(b))
                Case < 32, > 126
                    sb.Append("\"c).Append(Convert.ToString(b, 8).PadLeft(3, "0"c))
                Case Else
                    sb.Append(ChrW(b))
            End Select
        Next
        Return sb.ToString()
    End Function

#End Region

#Region "Reading helpers"

    Friend Function ReadRect(dict As PdfDictionary) As PdfRect
        Dim n = ReadNumbers(dict.Elements.GetArray("/Rect"))
        If n.Count < 4 Then Return New PdfRect()
        Return PdfRect.FromCorners(n(0), n(1), n(2), n(3))
    End Function

    Friend Function ReadNumbers(arr As PdfArray) As List(Of Double)
        Dim list As New List(Of Double)
        If arr Is Nothing Then Return list
        For i = 0 To arr.Elements.Count - 1
            Dim item = arr.Elements(i)
            Dim ref = TryCast(item, PdfReference)
            If ref IsNot Nothing Then item = ref.Value
            If TypeOf item Is PdfInteger Then
                list.Add(DirectCast(item, PdfInteger).Value)
            ElseIf TypeOf item Is PdfReal Then
                list.Add(DirectCast(item, PdfReal).Value)
            ElseIf TypeOf item Is PdfNumberObject OrElse TypeOf item Is PdfIntegerObject OrElse TypeOf item Is PdfRealObject Then
                list.Add(Double.Parse(item.ToString(), Inv))
            End If
        Next
        Return list
    End Function

    ''' <summary>Reads /C (any colour space) as an RGB colour.</summary>
    Friend Function ReadColor(dict As PdfDictionary, fallback As Color) As Color
        Dim n = ReadNumbers(dict.Elements.GetArray("/C"))
        Select Case n.Count
            Case 1 : Return FromUnit(n(0), n(0), n(0))
            Case 3 : Return FromUnit(n(0), n(1), n(2))
            Case 4 : Return FromUnit((1 - n(0)) * (1 - n(3)), (1 - n(1)) * (1 - n(3)), (1 - n(2)) * (1 - n(3)))
            Case Else : Return fallback
        End Select
    End Function

    ''' <summary>Reads the text colour from a /DA string such as "/Helv 12 Tf 1 0 0 rg".</summary>
    Friend Function ReadDaColor(dict As PdfDictionary, fallback As Color) As Color
        Dim tokens = If(dict.Elements.GetString("/DA"), "").Split({" "c, ChrW(10), ChrW(13)}, StringSplitOptions.RemoveEmptyEntries)
        For i = tokens.Length - 1 To 0 Step -1
            Dim v As Double, r As Double, g As Double, b As Double
            If tokens(i) = "rg" AndAlso i >= 3 AndAlso
               Double.TryParse(tokens(i - 3), NumberStyles.Float, Inv, r) AndAlso
               Double.TryParse(tokens(i - 2), NumberStyles.Float, Inv, g) AndAlso
               Double.TryParse(tokens(i - 1), NumberStyles.Float, Inv, b) Then
                Return FromUnit(r, g, b)
            ElseIf tokens(i) = "g" AndAlso i >= 1 AndAlso Double.TryParse(tokens(i - 1), NumberStyles.Float, Inv, v) Then
                Return FromUnit(v, v, v)
            End If
        Next
        Return fallback
    End Function

    Friend Function ReadFontSize(dict As PdfDictionary) As Double
        Dim tokens = If(dict.Elements.GetString("/DA"), "").Split({" "c, ChrW(10), ChrW(13)}, StringSplitOptions.RemoveEmptyEntries)
        For i = 1 To tokens.Length - 1
            Dim size As Double
            If tokens(i) = "Tf" AndAlso Double.TryParse(tokens(i - 1), NumberStyles.Float, Inv, size) AndAlso size > 0 Then
                Return size
            End If
        Next
        Return 12
    End Function

    Friend Function ParsePdfDate(s As String) As Date?
        If String.IsNullOrEmpty(s) Then Return Nothing
        If s.StartsWith("D:") Then s = s.Substring(2)
        Dim digits = New String(s.TakeWhile(Function(c) Char.IsDigit(c)).ToArray())
        If digits.Length < 8 Then Return Nothing
        digits = digits.PadRight(14, "0"c).Substring(0, 14)
        Dim d As Date
        If Date.TryParseExact(digits, "yyyyMMddHHmmss", Inv, DateTimeStyles.None, d) Then Return d
        Return Nothing
    End Function

#End Region

#Region "Formatting helpers"

    Private Function PdfDate(d As Date) As String
        Dim off = TimeZoneInfo.Local.GetUtcOffset(d)
        Dim sign = If(off < TimeSpan.Zero, "-", "+")
        off = off.Duration()
        Return $"D:{d.ToString("yyyyMMddHHmmss", Inv)}{sign}{off.Hours:00}'{off.Minutes:00}'"
    End Function

    Private Function DefaultAppearance(color As Color, fontSize As Double) As String
        Return $"/Helv {N(fontSize)} Tf {Rgb(color)} rg"
    End Function

    Private Function FromUnit(r As Double, g As Double, b As Double) As Color
        Return Color.FromArgb(ToByte(r), ToByte(g), ToByte(b))
    End Function

    Private Function ToByte(v As Double) As Integer
        Return CInt(Math.Round(Math.Max(0, Math.Min(1, v)) * 255))
    End Function

    Private Function Rgb(c As Color) As String
        Return $"{N(c.R / 255.0)} {N(c.G / 255.0)} {N(c.B / 255.0)}"
    End Function

    Private Function N(v As Double) As String
        Return Math.Round(v, 3).ToString("0.###", Inv)
    End Function

    Private Function ColorArray(doc As PdfDocument, c As Color) As PdfArray
        Return NumberArray(doc, Math.Round(c.R / 255.0, 4), Math.Round(c.G / 255.0, 4), Math.Round(c.B / 255.0, 4))
    End Function

    Private Function RectArray(doc As PdfDocument, r As PdfRect) As PdfArray
        Return NumberArray(doc, Math.Round(r.X1, 3), Math.Round(r.Y1, 3), Math.Round(r.X2, 3), Math.Round(r.Y2, 3))
    End Function

    Private Function NumberArray(doc As PdfDocument, ParamArray values As Double()) As PdfArray
        Dim arr As New PdfArray(doc)
        For Each v In values
            arr.Elements.Add(New PdfReal(v))
        Next
        Return arr
    End Function

#End Region

End Module
