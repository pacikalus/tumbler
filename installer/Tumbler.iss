; Inno Setup script for the Tumbler installer.
; Built by "Make Installer.bat" (tools\build-installer.ps1), which passes in
; the version and the folder of published files.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish-installer"
#endif

[Setup]
; Keep this AppId the same in every release, so new versions upgrade old ones.
AppId={{6251F262-DCD2-4F5A-9DBA-003D4FB84BF6}
AppName=Tumbler
AppVersion={#AppVersion}
AppVerName=Tumbler {#AppVersion}
AppPublisher=Pacikalus
AppPublisherURL=https://github.com/pacikalus/tumbler
AppSupportURL=https://github.com/pacikalus/tumbler/issues
AppUpdatesURL=https://github.com/pacikalus/tumbler/releases
AppContact=pacikalus@gmail.com
AppCopyright=Copyright (c) 2026 Pacikalus
VersionInfoVersion={#AppVersion}
VersionInfoDescription=Tumbler Setup
DefaultDirName={autopf}\Tumbler
DefaultGroupName=Tumbler
DisableProgramGroupPage=yes
; Installs for the current user without an admin prompt; people can still
; choose "install for all users" on the first page.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=..\dist
OutputBaseFilename=Tumbler-Setup-{#AppVersion}
SetupIconFile=..\src\Tumbler\Tumbler.ico
UninstallDisplayIcon={app}\Tumbler.exe
UninstallDisplayName=Tumbler
LicenseFile=..\LICENSE
WizardStyle=modern
WizardImageFile=wizard-164.bmp,wizard-328.bmp
WizardSmallImageFile=wizard-small-55.bmp,wizard-small-110.bmp
Compression=lzma2/ultra64
SolidCompression=yes
CloseApplications=yes
ChangesAssociations=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "pdfopenwith"; Description: "Add Tumbler to the ""Open with"" menu for PDF files"; GroupDescription: "PDF files:"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Tumbler"; Filename: "{app}\Tumbler.exe"
Name: "{autodesktop}\Tumbler"; Filename: "{app}\Tumbler.exe"; Tasks: desktopicon

[Registry]
; Offers Tumbler for PDFs in "Open with" without taking over the default PDF app.
Root: HKA; Subkey: "Software\Classes\Tumbler.PDF"; ValueType: string; ValueName: ""; ValueData: "PDF document"; Flags: uninsdeletekey; Tasks: pdfopenwith
Root: HKA; Subkey: "Software\Classes\Tumbler.PDF\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\Tumbler.exe"",0"; Tasks: pdfopenwith
Root: HKA; Subkey: "Software\Classes\Tumbler.PDF\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\Tumbler.exe"" ""%1"""; Tasks: pdfopenwith
Root: HKA; Subkey: "Software\Classes\.pdf\OpenWithProgids"; ValueType: string; ValueName: "Tumbler.PDF"; ValueData: ""; Flags: uninsdeletevalue; Tasks: pdfopenwith
Root: HKA; Subkey: "Software\Classes\Applications\Tumbler.exe"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "Tumbler"; Flags: uninsdeletekey; Tasks: pdfopenwith
Root: HKA; Subkey: "Software\Classes\Applications\Tumbler.exe\SupportedTypes"; ValueType: string; ValueName: ".pdf"; ValueData: ""; Tasks: pdfopenwith
Root: HKA; Subkey: "Software\Classes\Applications\Tumbler.exe\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\Tumbler.exe"" ""%1"""; Tasks: pdfopenwith

[Run]
Filename: "{app}\Tumbler.exe"; Description: "{cm:LaunchProgram,Tumbler}"; Flags: nowait postinstall skipifsilent
; Tumbler's own updater runs this setup silently; open Tumbler again afterwards.
Filename: "{app}\Tumbler.exe"; Flags: nowait runasoriginaluser; Check: WizardSilent
