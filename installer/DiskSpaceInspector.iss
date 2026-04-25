#define MyAppName "Disk Space Inspector"
#ifndef MyAppVersion
#define MyAppVersion "1.1.0"
#endif
#ifndef SourceDir
#define SourceDir "..\artifacts\publish\DiskSpaceInspector-1.1.0-win-x64"
#endif
#ifndef OutputDir
#define OutputDir "..\artifacts\release"
#endif

[Setup]
AppId={{B1D65256-8308-4C61-9F3D-78460E63C3B3}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=Andrew Rainsberger
AppPublisherURL=https://github.com/CurioCrafter/Disk-Space-Inspector
AppSupportURL=https://github.com/CurioCrafter/Disk-Space-Inspector/issues
AppUpdatesURL=https://github.com/CurioCrafter/Disk-Space-Inspector/releases
DefaultDirName={localappdata}\Programs\Disk Space Inspector
DefaultGroupName=Disk Space Inspector
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
SetupIconFile=..\src\DiskSpaceInspector.App\Assets\app.ico
UninstallDisplayIcon={app}\DiskSpaceInspector.App.exe
OutputDir={#OutputDir}
OutputBaseFilename=DiskSpaceInspectorSetup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Disk Space Inspector"; Filename: "{app}\DiskSpaceInspector.App.exe"
Name: "{autodesktop}\Disk Space Inspector"; Filename: "{app}\DiskSpaceInspector.App.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\DiskSpaceInspector.App.exe"; Description: "Launch Disk Space Inspector"; Flags: nowait postinstall skipifsilent
