; softkeys — Inno Setup script (F-16/F-17/F-18, Amendment A2)
; Compile via installer\build.ps1 (NF-08), which injects AppVersion from the
; csproj (D-17) and points PublishDir at the published single-file exe.
; NF-09: no elevation anywhere — PrivilegesRequired=lowest keeps the setup and
; uninstall stubs asInvoker; everything below is per-user (HKCU / {localappdata}).

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\bin\Release\net8.0-windows\win-x64\publish"
#endif

[Setup]
AppId={{31641528-C159-4C55-92A6-621AC5B6E0E5}
AppName=softkeys
AppVersion={#AppVersion}
AppVerName=softkeys {#AppVersion}
AppPublisher=Nasser Al-Husayan
AppPublisherURL=https://github.com/NasserAlh/softkeys
AppSupportURL=https://github.com/NasserAlh/softkeys
; D-14: per-user install root, zero elevation
DefaultDirName={localappdata}\Programs\softkeys
PrivilegesRequired=lowest
DisableProgramGroupPage=yes
DisableDirPage=yes
OutputDir=output
OutputBaseFilename=softkeys-setup
SetupIconFile=..\assets\softkeys.ico
UninstallDisplayIcon={app}\softkeys.exe
UninstallDisplayName=softkeys
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; F-18: a running instance holds this mutex; setup asks the user to close it
; instead of failing mid-copy on a locked exe
AppMutex=Local\softkeys-single-instance

[Tasks]
; Both optional and unchecked by default (F-16)
Name: "desktopicon"; Description: "Create a &desktop shortcut"; Flags: unchecked
Name: "autostart"; Description: "&Start softkeys when I sign in"; Flags: unchecked

[Files]
Source: "{#PublishDir}\softkeys.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
; Start Menu always; desktop only on opt-in
Name: "{userprograms}\softkeys"; Filename: "{app}\softkeys.exe"
Name: "{userdesktop}\softkeys"; Filename: "{app}\softkeys.exe"; Tasks: desktopicon

[Registry]
; D-16: per-user autostart, written only on opt-in, removed on uninstall
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
  ValueType: string; ValueName: "softkeys"; ValueData: """{app}\softkeys.exe"""; \
  Flags: uninsdeletevalue; Tasks: autostart

[Code]
// F-17 / D-18: settings are KEPT by default; deleting %APPDATA%\softkeys is an
// explicit opt-in at uninstall time. Silent uninstalls always keep settings.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  SettingsDir: string;
begin
  if CurUninstallStep = usUninstall then
  begin
    SettingsDir := ExpandConstant('{userappdata}\softkeys');
    if (not UninstallSilent) and DirExists(SettingsDir) then
    begin
      if MsgBox('Remove saved softkeys settings as well?' + #13#10 +
                '(' + SettingsDir + ')' + #13#10#13#10 +
                'Choose No to keep them for a future install.',
                mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(SettingsDir, True, True, True);
    end;
  end;
end;
