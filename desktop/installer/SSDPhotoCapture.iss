; Build with Inno Setup 6:  iscc SSDPhotoCapture.iss   (after running build-installer.ps1 publishes ..\publish)
#define AppName "SSD Photo Capture"
#define AppVersion "1.1.0"
#define AppExe "SSDPhotoCapture.exe"

[Setup]
AppId={{6B1F0E5C-4C8E-4F1A-9B0D-5D1E7A52C9A1}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=SSD
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=output
OutputBaseFilename=SSDPhotoCapture-Setup-{#AppVersion}
SetupIconFile=..\..\assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Detects the running tray app (see Program.cs) and offers to close it before upgrading.
AppMutex=Local\SSDPhotoCapture.Host
CloseApplications=yes

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"
Name: "autostart"; Description: "Start automatically when I sign in to Windows (phones can sync any time)"; GroupDescription: "Startup:"
Name: "tailscale"; Description: "Open the Tailscale download page (needed so phones can reach this PC)"; GroupDescription: "Phone connection:"; Check: NeedsTailscale

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion createallsubdirs

[Icons]
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "SSDPhotoCapture"; ValueData: """{app}\{#AppExe}"" --background"; Tasks: autostart; Flags: uninsdeletevalue

[Run]
Filename: "https://tailscale.com/download/windows"; Description: "Tailscale download"; Flags: shellexec nowait postinstall skipifsilent; Tasks: tailscale
Filename: "{app}\{#AppExe}"; Description: "Open {#AppName} now"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Photos and the database live in the user's data folder and are intentionally never removed.
Type: files; Name: "{localappdata}\SSDPhotoCapture\datafolder.txt"

[Code]
var
  DataPage: TInputDirWizardPage;

function NeedsTailscale: Boolean;
begin
  Result := not (FileExists(ExpandConstant('{commonpf64}\Tailscale\tailscale.exe'))
              or FileExists(ExpandConstant('{commonpf32}\Tailscale\tailscale.exe')));
end;

function PointerFile: String;
begin
  Result := ExpandConstant('{localappdata}\SSDPhotoCapture\datafolder.txt');
end;

procedure InitializeWizard;
var
  Previous: AnsiString;
  Suggested: String;
begin
  Suggested := ExpandConstant('{userdocs}\SSDPhotoData');
  if LoadStringFromFile(PointerFile, Previous) and (Trim(String(Previous)) <> '') then
    Suggested := Trim(String(Previous));

  DataPage := CreateInputDirPage(wpSelectDir,
    'Where should the photos and database be stored?',
    'All data lives in one folder.',
    'Pick an empty folder for a new setup, or pick the folder you copied from another PC to carry on with the same data.' + #13#10 +
    'The folder is never deleted when you uninstall.',
    False, '');
  DataPage.Add('');
  DataPage.Values[0] := Suggested;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Folder: String;
begin
  Result := True;
  if CurPageID = DataPage.ID then
  begin
    Folder := Trim(DataPage.Values[0]);
    if Folder = '' then
    begin
      MsgBox('Please choose a data folder.', mbError, MB_OK);
      Result := False;
    end
    else if FileExists(Folder + '\photos.db') then
      MsgBox('Existing data was found in this folder. It will be used as it is.', mbInformation, MB_OK);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Folder: String;
begin
  if CurStep = ssPostInstall then
  begin
    Folder := Trim(DataPage.Values[0]);
    ForceDirectories(Folder);
    ForceDirectories(ExtractFileDir(PointerFile));
    SaveStringToFile(PointerFile, AnsiString(Folder), False);
  end;
end;
