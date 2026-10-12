<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="assets/logo-dark.svg">
    <img src="assets/logo.svg" alt="Tumbler" width="420">
  </picture>
</p>

# Tumbler

A Windows desktop PDF editor that sits between Acrobat Reader and Acrobat Pro.
VB.NET, WinForms, .NET 8.

## What it does

| Feature | How to use it |
|---|---|
| View PDFs | File > Open, drag a file onto the window, or `Tumbler.exe file.pdf`. Scroll, Ctrl+wheel to zoom, drag the page to pan. |
| Delete pages | Select pages in the left strip (Ctrl+click / Shift+click / Ctrl+A), then **Delete pages** or the Delete key. |
| Rotate one page or all | **↺ Rotate / ↻ Rotate** act on the selected pages (or the current page). **Rotate all** turns every page. |
| Multicolour comments | **Comment** tool: click on the page and type. The comment appears right there as plain text in the colour you pick, with no box. |
| Draw lines | **Line** tool: drag from one end to the other. Hold Shift to keep it straight across, straight down or at 45°. **Colour** and **Thickness** (1, 2, 4 or 8 pt) apply to new lines. |
| Highlight text | **Highlight** tool: drag across text. Where there is no text (a scan), drag a box instead. |
| Insert pages from other files | **Insert pages…**: choose a PDF, a page range (`all`, `1-3, 5`), and where to put them. Or drag PDFs from Explorer (any screen) onto the window. Over the pages or the page strip, a blue line shows where they will go; move the mouse to choose, and the view scrolls when you reach the top or bottom edge. Dropped anywhere else, they go at the end. |
| Edit or remove comments | **Select** tool: click a comment, line or highlight to select it (blue outline), drag to move it, double-click (or press Enter) to change its text or colour, Delete to remove it. Right-click for the same choices. The **Comments** panel on the right lists them all. |
| Move comments and lines | Press on a comment, line or sticky note and drag it to a new place. This works with any tool except Highlight, so you can place a comment and then drag it straight away. A new comment or line is selected as soon as it is added. |
| Resize comments and lines | A selected comment shows small squares on its corners and edges: drag one to make the box wider, narrower, taller or shorter, and the text re-wraps to fit (the box never gets smaller than its text). A selected line shows a circle at each end: drag one to move just that end (hold Shift for straight across, down or 45°). |
| Copy comments and lines | Select a comment or line and press **Ctrl+C** (or right-click > Copy). Point the mouse where the copy should go, on any page, and press **Ctrl+V** (or right-click there > Paste here). Pressing Ctrl+V again without moving the mouse steps each copy down and right. Comment text is also put on the Windows clipboard. |
| Undo / redo | Ctrl+Z / Ctrl+Y, up to 30 steps. |

Nothing touches your file until you save. **Save** writes back to the same
file. **Save As** suggests `name (edited).pdf`.

Comments are standard PDF annotations with their own appearance, so they show
up the same in Acrobat, Edge, Chrome and other viewers, and Acrobat users can
reply to or edit them.

## Build and run

You need the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) on Windows (or Visual Studio 2022 17.8+).

```
dotnet build Tumbler.sln
dotnet run --project src\Tumbler
dotnet test                         # 42 tests for the PDF engine
```

Or open `Tumbler.sln` in Visual Studio and press F5.

To make a folder you can copy to another PC (it needs the .NET 8 Desktop Runtime):

```
dotnet publish src\Tumbler -c Release -r win-x64 --self-contained false -o publish
```

## Making the installer

Double-click **Make Installer.bat**. It builds Tumbler with .NET included, signs it, and makes
`dist\Tumbler-Setup-<version>.exe` (about 50 MB), the one file to give to other people. The first
time, it installs [Inno Setup](https://jrsoftware.org/isinfo.php) (free) to build the setup program.

The installer:
- installs for the current user without an admin prompt, or for all users if they choose;
- adds Tumbler to the Start menu, and optionally to the desktop;
- adds Tumbler to the "Open with" menu for PDFs, without changing their default PDF app;
- shows the licence, and can be removed from Settings > Apps.

The version comes from `<Version>` in `src\Tumbler\Tumbler.vbproj` (see [Versions](#versions)).

## Updates

At the end, Make Installer asks whether to publish the new version on GitHub. Say **Y** and it
creates the release `v<version>` with the setup program attached, listing the commit titles since
the previous release as "What's new". The first time, it installs the GitHub command-line tool and
asks you to sign in to GitHub in the browser. To publish a setup you made earlier, double-click
**Publish Release.bat** (or run `tools\publish-release.ps1`).

Installed copies of Tumbler look for the newest release when they start (at most once a day) and
from **Help > Check for updates**. When there is a newer version, Tumbler shows what's new and
offers **Update now**, **Skip this version** or **Later**. Update now downloads the setup, checks it
is signed with the same certificate as the running Tumbler, closes Tumbler (asking to save first),
installs the update in place and opens Tumbler again. The startup check can be switched off under
**Help > Check for updates when Tumbler starts**.

## Signing (the "Unknown publisher" warning)

Windows shows *Unknown publisher* for any program that is not signed with a
code-signing certificate. There are three levels:

1. **Just for you, free, right now.** Right-click the downloaded zip > Properties >
   tick **Unblock** > OK, then unzip. Windows stops warning about that copy.
2. **Your name as publisher on your own PCs, free.** Double-click **Build and Sign Tumbler.bat**.
   The first time, it makes your certificate (click **Yes** when Windows asks), then it builds,
   signs, and opens the folder with the signed `Tumbler.exe`. Later runs skip the certificate step.
   On another PC, double-click `tools\Tumbler-signing.cer` > Install Certificate >
   Current User > place it in **Trusted Root Certification Authorities**, then again
   in **Trusted Publishers**. Only do this on PCs you control.
3. **Trusted everywhere: buy a certificate.** Options, cheapest first:
   - **Microsoft Azure Trusted Signing** (about US$10/month; individuals in the US/Canada and registered companies). Signs with `signtool` and Microsoft's plug-in.
   - An **OV code-signing certificate** from Sectigo, DigiCert, SSL.com, Certum and others (roughly US$200-500/year). Since 2023 these come on a USB token or cloud key, not a plain file; install the vendor's driver and the certificate shows up in Windows, then run `tools\publish.ps1 -Thumbprint <its thumbprint>`.

   Even with a bought certificate, SmartScreen can still show a warning for a while until enough people have run the signed program.

`publish.ps1` also accepts `-PfxPath file.pfx` if your certificate is a file.

## Versions

Every commit raises the patch version, the last number, by one (1.1.1, 1.1.2, 1.1.3 ...).
For a bigger release, raise the middle number by hand. The version lives in
`src/Tumbler/Tumbler.vbproj`, shows on the About page, and names the installer
(`Tumbler-Setup-1.1.1.exe`). A git hook does the counting; turn it on once in each clone with:

```
git config core.hooksPath .githooks
```

## Libraries

- **[PDFsharp 6.1](https://www.pdfsharp.com/)** (MIT) edits the file: deleting, rotating and inserting pages, writing annotations.
- **[Docnet.Core 2.6](https://github.com/GowenGit/docnet)** (MIT) wraps Google's **PDFium** (BSD-3), the engine inside Chrome, for drawing pages and finding character positions for text highlighting.

Both are free for commercial use. Neither needs anything installed besides the NuGet packages; PDFium's DLL is copied next to the exe automatically.

## Project layout

```
src/Tumbler.Core/    PDF engine, no UI (also runs on Linux/macOS, which is how it is tested)
  PdfEditor.vb         the open document: page edits, comments, undo/redo, save
  AnnotationWriter.vb  builds comments, lines, highlights and their appearance
  PdfRenderer.vb       PDFium rendering and character positions
  PageGeometry.vb      maps screen positions to PDF coordinates on rotated pages
  TextSelection.vb     turns a mouse drag into highlighted lines; page-range parser
src/Tumbler/         WinForms app (Tumbler.ico is the app icon)
  MainForm.vb          menus, toolbar, commands
  DocumentView.vb      the scrolling page view and mouse tools
  ThumbnailView.vb     the page strip
  Dialogs.vb           comment, insert-pages and password dialogs
assets/              logo and icon artwork (SVG sources and PNGs)
tests/Tumbler.Core.Tests/   xUnit tests and sample PDFs (Fixtures/make_fixtures.py regenerates them)
```

The UI is built in code rather than with the Designer, so every form is a plain `.vb` file.

## Known limits

- Comments use Helvetica, so characters outside Western European (Windows-1252) show as `?` on the page. The full text is still stored in the comment.
- Password-protected PDFs open after you enter the password, and PDFs locked against editing (common for government forms) open without one. Either way the saved copy has no password or lock.
- Damaged PDFs that Acrobat or a browser can still show are repaired as they open.
- Saving rewrites the file, which invalidates any digital signatures in it.
- Each edit re-saves the document in memory, so very large files (hundreds of MB) will feel slow to edit.

## Authors

<img src="assets/pacikalus.png" alt="Pacikalus" width="72" align="left" />

**Pacikalus** ([pacikalus@gmail.com](mailto:pacikalus@gmail.com)), with [Claude](https://claude.ai) by Anthropic as co-author.

<br clear="left" />

## License

MIT, see [LICENSE](LICENSE). PDFsharp and Docnet.Core are MIT; PDFium is BSD-3.
