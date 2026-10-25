; Inno Setup script for EdgeShelf.
; The GitHub Actions workflow compiles this into EdgeShelf-Setup.exe

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

#define AppName "EdgeShelf"
#define AppExe "EdgeShelf.exe"

[Setup]
AppId={{B7E2C1A4-5D3F-4E8A-9C21-7F4A6D0E1B52}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=EdgeShelf
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}
SetupIconFile=..\EdgeShelf\Assets\icon.ico
OutputDir=..\installer-out
OutputBaseFilename=EdgeShelf-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Installs just for the current user by default (no admin prompt).
; The user can still choose "for all users" in the dialog that appears.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
; The install location page is shown (this is the default, stated here on purpose)
DisableDirPage=no
DisableProgramGroupPage=yes
; Close the running app automatically during install/uninstall
AppMutex=EdgeShelf_SingleInstance_Mutex
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional options:"; Flags: unchecked
Name: "startwithwindows"; Description: "&Start EdgeShelf with Windows"; GroupDescription: "Additional options:"

[Files]
Source: "..\out\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Start with Windows (current user). Same value name the app itself uses in its tray menu.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "EdgeShelf"; ValueData: """{app}\{#AppExe}"""; Flags: uninsdeletevalue; Tasks: startwithwindows

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch EdgeShelf"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Leave the user's saved items and settings in %AppData%\EdgeShelf (not deleted on uninstall)
Type: filesandordirs; Name: "{app}"
