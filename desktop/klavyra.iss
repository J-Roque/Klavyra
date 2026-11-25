; Inno Setup script for Klavyra-Setup-<version>.exe. desktop\build.ps1 runs it after building desktop\out\Klavyra:
;   ISCC.exe /DAppVersion=0.1.0 /DSourceDir=<desktop\out\Klavyra> desktop\klavyra.iss
; Installs for the current user only (no administrator rights) in %LOCALAPPDATA%\Programs\Klavyra.
; The user's data (projects, Python environment, models) lives apart in %LOCALAPPDATA%\Klavyra.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "out\Klavyra"
#endif

[Setup]
AppId={{6F2C8B7E-4A1D-4C3E-9B5F-8D7A2E1C0F93}
AppName=Klavyra
AppVersion={#AppVersion}
AppVerName=Klavyra {#AppVersion}
AppPublisher=Johel Roque
AppCopyright=© 2026 Johel Roque
AppPublisherURL=https://github.com/J-Roque/Klavyra
AppSupportURL=https://github.com/J-Roque/Klavyra/issues
AppUpdatesURL=https://github.com/J-Roque/Klavyra/releases
VersionInfoVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\Klavyra
DefaultGroupName=Klavyra
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
; Klavyra.exe holds this mutex while it runs: setup and uninstall ask to close it first.
AppMutex=Local\Klavyra.Desktop
CloseApplications=yes
LicenseFile={#SourceDir}\LICENSE.txt
SetupIconFile=..\src\Klavyra.Desktop\Klavyra.ico
UninstallDisplayIcon={app}\Klavyra.exe
UninstallDisplayName=Klavyra
OutputDir=out
OutputBaseFilename=Klavyra-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
; Files from an older version that the new one no longer ships (the data folder is not touched).
Type: filesandordirs; Name: "{app}\api"
Type: filesandordirs; Name: "{app}\worker"

[Icons]
Name: "{autoprograms}\Klavyra"; Filename: "{app}\Klavyra.exe"
Name: "{autodesktop}\Klavyra"; Filename: "{app}\Klavyra.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Klavyra.exe"; Description: "{cm:LaunchProgram,Klavyra}"; Flags: nowait postinstall skipifsilent

[CustomMessages]
es.DeleteData=¿Borrar también tus proyectos, los modelos descargados y el entorno de Python?%n%n%1%n%nSi vas a reinstalar Klavyra, elige No y no tendrás que volver a descargarlos.
en.DeleteData=Also delete your projects, the downloaded models and the Python environment?%n%n%1%n%nIf you plan to reinstall Klavyra, choose No so you do not have to download them again.

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Data: String;
begin
  if CurUninstallStep <> usPostUninstall then Exit;
  Data := ExpandConstant('{localappdata}\Klavyra');
  if not DirExists(Data) then Exit;
  { Silent uninstalls keep the data (default answer: No). }
  if SuppressibleMsgBox(FmtMessage(CustomMessage('DeleteData'), [Data]), mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES then
    DelTree(Data, True, True, True);
end;
