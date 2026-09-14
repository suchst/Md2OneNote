; Md2OneNote per-user installer (Inno Setup 6).
;
; Compiled by tools\build-release.ps1 with the payload it just packaged:
;   ISCC /DVersion=1.0.0-preview.1 /DFileVersion=1.0.0.0 installer\Md2OneNote.iss
;
; Everything lands under %LOCALAPPDATA%\Md2OneNote and HKCU, so no administrator rights are
; needed (NFR-10). The [Registry] section is tools\register.ps1 line for line; the two must stay
; identical, because the uninstaller of one has to be able to undo the other (NFR-14).

#ifndef Version
  #error Pass /DVersion=<product version> (see tools\build-release.ps1)
#endif
#ifndef FileVersion
  #error Pass /DFileVersion=<assembly version, four parts>
#endif

#define AppName       "Md2OneNote"
#define Publisher     "Stanislav Sucharda"
#define ProjectUrl    "https://github.com/suchst/Md2OneNote"
#define Payload       "..\artifacts\payload"
; A doubled brace is a literal one; a single brace starts an Inno constant.
#define ClsId         "{{04185F61-8636-4BF0-BCB3-941EE717E8C8}"
#define ProgId        "Md2OneNote.AddIn"
#define ClassName     "Md2OneNote.AddIn.Connect"
#define AssemblyName  "Md2OneNote.AddIn, Version=" + FileVersion + ", Culture=neutral, PublicKeyToken=null"
#define ManagedCategory "{{62C8FE65-4EBB-45E7-B440-6E39B2CDBF29}"

[Setup]
; The installer's own identity in Apps & features; not the add-in's CLSID.
AppId={{6E1D4C0B-3D8B-4B2E-9A5A-3F0B6C4A9E71}
AppName={#AppName}
AppVersion={#Version}
AppVerName={#AppName} {#Version}
AppPublisher={#Publisher}
AppPublisherURL={#ProjectUrl}
AppSupportURL={#ProjectUrl}/issues
AppUpdatesURL={#ProjectUrl}/releases
VersionInfoVersion={#FileVersion}
VersionInfoDescription={#AppName} setup

; Fixed location: the CodeBase written to the registry must match, and the log and README
; both name this folder.
DefaultDirName={localappdata}\{#AppName}
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=no
PrivilegesRequired=lowest
MinVersion=10.0
LicenseFile=..\LICENSE
SetupIconFile=..\assets\brand\icon.ico
UninstallDisplayIcon={app}\bin\Md2OneNote.AddIn.dll
UninstallDisplayName={#AppName} (Markdown to OneNote)
OutputDir=..\artifacts
OutputBaseFilename={#AppName}-{#Version}-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; OneNote is checked by the code below with a clear message; the generic prompt is not wanted.
CloseApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#Payload}\*"; DestDir: "{app}\bin"; Excludes: "register.ps1"; Flags: ignoreversion recursesubdirs createallsubdirs

[Registry]
; COM class, hosted out of process in the system surrogate (docs\IMPLEMENTATION.md §10).
Root: HKCU; Subkey: "Software\Classes\CLSID\{#ClsId}"; ValueType: string; ValueName: ""; ValueData: "{#ClassName}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\CLSID\{#ClsId}"; ValueType: string; ValueName: "AppID"; ValueData: "{#ClsId}"
Root: HKCU; Subkey: "Software\Classes\AppID\{#ClsId}"; ValueType: string; ValueName: "DllSurrogate"; ValueData: ""; Flags: uninsdeletekey

Root: HKCU; Subkey: "Software\Classes\CLSID\{#ClsId}\InprocServer32"; ValueType: string; ValueName: ""; ValueData: "mscoree.dll"
Root: HKCU; Subkey: "Software\Classes\CLSID\{#ClsId}\InprocServer32"; ValueType: string; ValueName: "ThreadingModel"; ValueData: "Both"
Root: HKCU; Subkey: "Software\Classes\CLSID\{#ClsId}\InprocServer32"; ValueType: string; ValueName: "Class"; ValueData: "{#ClassName}"
Root: HKCU; Subkey: "Software\Classes\CLSID\{#ClsId}\InprocServer32"; ValueType: string; ValueName: "Assembly"; ValueData: "{#AssemblyName}"
Root: HKCU; Subkey: "Software\Classes\CLSID\{#ClsId}\InprocServer32"; ValueType: string; ValueName: "RuntimeVersion"; ValueData: "v4.0.30319"
Root: HKCU; Subkey: "Software\Classes\CLSID\{#ClsId}\InprocServer32"; ValueType: string; ValueName: "CodeBase"; ValueData: "{app}\bin\Md2OneNote.AddIn.dll"

Root: HKCU; Subkey: "Software\Classes\CLSID\{#ClsId}\InprocServer32\{#FileVersion}"; ValueType: string; ValueName: ""; ValueData: "mscoree.dll"
Root: HKCU; Subkey: "Software\Classes\CLSID\{#ClsId}\InprocServer32\{#FileVersion}"; ValueType: string; ValueName: "ThreadingModel"; ValueData: "Both"
Root: HKCU; Subkey: "Software\Classes\CLSID\{#ClsId}\InprocServer32\{#FileVersion}"; ValueType: string; ValueName: "Class"; ValueData: "{#ClassName}"
Root: HKCU; Subkey: "Software\Classes\CLSID\{#ClsId}\InprocServer32\{#FileVersion}"; ValueType: string; ValueName: "Assembly"; ValueData: "{#AssemblyName}"
Root: HKCU; Subkey: "Software\Classes\CLSID\{#ClsId}\InprocServer32\{#FileVersion}"; ValueType: string; ValueName: "RuntimeVersion"; ValueData: "v4.0.30319"
Root: HKCU; Subkey: "Software\Classes\CLSID\{#ClsId}\InprocServer32\{#FileVersion}"; ValueType: string; ValueName: "CodeBase"; ValueData: "{app}\bin\Md2OneNote.AddIn.dll"

Root: HKCU; Subkey: "Software\Classes\CLSID\{#ClsId}\ProgId"; ValueType: string; ValueName: ""; ValueData: "{#ProgId}"
Root: HKCU; Subkey: "Software\Classes\CLSID\{#ClsId}\VersionIndependentProgID"; ValueType: string; ValueName: ""; ValueData: "{#ProgId}"
Root: HKCU; Subkey: "Software\Classes\CLSID\{#ClsId}\Implemented Categories\{#ManagedCategory}"; ValueType: none
Root: HKCU; Subkey: "Software\Classes\CLSID\{#ClsId}\Programmable"; ValueType: none

Root: HKCU; Subkey: "Software\Classes\{#ProgId}"; ValueType: string; ValueName: ""; ValueData: "{#ClassName}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\{#ProgId}\CLSID"; ValueType: string; ValueName: ""; ValueData: "{#ClsId}"

; What OneNote reads at startup. The path is unversioned, unlike Word's and Excel's.
Root: HKCU; Subkey: "Software\Microsoft\Office\OneNote\AddIns\{#ProgId}"; ValueType: string; ValueName: "FriendlyName"; ValueData: "Markdown to OneNote"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Microsoft\Office\OneNote\AddIns\{#ProgId}"; ValueType: string; ValueName: "Description"; ValueData: "Imports Markdown files as styled OneNote pages"
Root: HKCU; Subkey: "Software\Microsoft\Office\OneNote\AddIns\{#ProgId}"; ValueType: dword; ValueName: "LoadBehavior"; ValueData: 3

[UninstallDelete]
; Everything the add-in writes at run time lives here too (log, extracted diagram shell,
; WebView2 user data); the uninstaller asks before removing it, see below.
Type: filesandordirs; Name: "{app}\bin"

[Code]
// ---- process checks --------------------------------------------------------------------------
// OneNote holds the add-in registration from startup, and its dllhost.exe surrogate holds the DLL
// itself for a few seconds after OneNote exits. Installing under either leaves a stale add-in.

function ProcessRunning(const Script: String): Boolean;
var
  ResultCode: Integer;
begin
  // Exit code 0 when at least one matching process exists, 1 otherwise.
  Result := Exec('powershell.exe',
    '-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "' + Script + '"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

function OneNoteRunning: Boolean;
begin
  Result := ProcessRunning('exit [int](-not (Get-Process -Name ONENOTE -ErrorAction SilentlyContinue))');
end;

function SurrogateRunning: Boolean;
begin
  Result := ProcessRunning(
    'exit [int](-not (Get-CimInstance Win32_Process -Filter \"Name=''dllhost.exe''\" | ' +
    'Where-Object { $_.CommandLine -like ''*04185F61-8636-4BF0-BCB3-941EE717E8C8*'' }))');
end;

function WaitForOneNoteToClose(const Verb: String): Boolean;
begin
  Result := True;
  while OneNoteRunning do
  begin
    if MsgBox('OneNote is running. Close it (File > Exit), then click Retry to ' + Verb + '.',
              mbError, MB_RETRYCANCEL) <> IDRETRY then
    begin
      Result := False;
      Exit;
    end;
  end;

  while SurrogateRunning do
  begin
    if MsgBox('OneNote has closed but its add-in host (dllhost.exe) is still shutting down. ' +
              'Wait a few seconds, then click Retry.', mbInformation, MB_RETRYCANCEL) <> IDRETRY then
    begin
      Result := False;
      Exit;
    end;
  end;
end;

function InitializeSetup: Boolean;
begin
  Result := WaitForOneNoteToClose('install');
end;

function InitializeUninstall: Boolean;
begin
  Result := WaitForOneNoteToClose('uninstall');
end;

// ---- resiliency -------------------------------------------------------------------------------
// When an add-in throws during startup OneNote sets LoadBehavior to 2 and lists it under
// Resiliency\DisabledItems, and never loads it again. A disabled add-in cannot repair itself,
// so re-running this installer is the supported recovery path (NFR-3). LoadBehavior is rewritten
// by the [Registry] section; the DisabledItems entries are removed here.

function ContainsUtf16(const Data, Text: AnsiString): Boolean;
var
  Wide: AnsiString;
  I: Integer;
begin
  Wide := '';
  for I := 1 to Length(Text) do
    Wide := Wide + Text[I] + #0;
  Result := Pos(Wide, Data) > 0;
end;

procedure ClearDisabledItems;
var
  Versions, Names: TArrayOfString;
  Key: String;
  Value: AnsiString;
  I, J: Integer;
begin
  if not RegGetSubkeyNames(HKCU, 'Software\Microsoft\Office', Versions) then Exit;
  for I := 0 to GetArrayLength(Versions) - 1 do
  begin
    Key := 'Software\Microsoft\Office\' + Versions[I] + '\OneNote\Resiliency\DisabledItems';
    if not RegGetValueNames(HKCU, Key, Names) then Continue;
    for J := 0 to GetArrayLength(Names) - 1 do
    begin
      if RegQueryBinaryValue(HKCU, Key, Names[J], Value) and ContainsUtf16(Value, 'Md2OneNote') then
      begin
        RegDeleteValue(HKCU, Key, Names[J]);
        Log('Removed DisabledItems entry ' + Names[J] + ' under ' + Key);
      end;
    end;
  end;
end;

// ---- WebView2 ---------------------------------------------------------------------------------
// Diagrams need the Evergreen WebView2 runtime; everything else works without it (NFR-12). The
// installer says so and does not download anything (NFR-16).

const
  WebView2Client = '{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';

function WebView2Installed: Boolean;
var
  Version: String;
begin
  Result :=
    RegQueryStringValue(HKCU, 'Software\Microsoft\EdgeUpdate\Clients\' + WebView2Client, 'pv', Version) or
    RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\' + WebView2Client, 'pv', Version) or
    RegQueryStringValue(HKLM, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\' + WebView2Client, 'pv', Version);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    ClearDisabledItems;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpFinished then
  begin
    WizardForm.FinishedLabel.Caption :=
      'Md2OneNote is installed. Start OneNote and look for the Markdown group on the Insert tab.';
    if not WebView2Installed then
      WizardForm.FinishedLabel.Caption := WizardForm.FinishedLabel.Caption + #13#10#13#10 +
        'The Microsoft Edge WebView2 runtime was not found on this computer. Everything imports ' +
        'except diagrams, which will appear as code. To render diagrams, install the runtime from ' +
        'https://developer.microsoft.com/microsoft-edge/webview2/ and import the file again.';
  end;
end;

// ---- uninstall --------------------------------------------------------------------------------

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    if DirExists(ExpandConstant('{app}')) then
    begin
      if MsgBox('Also delete the Md2OneNote log and cache folder?' + #13#10 +
                ExpandConstant('{app}'), mbConfirmation, MB_YESNO) = IDYES then
        DelTree(ExpandConstant('{app}'), True, True, True);
    end;
  end;
end;
