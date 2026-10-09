; Se compila con:  ISCC.exe /DMyVersion=0.1.0 installer\schoolguard.iss
#ifndef MyVersion
  #define MyVersion "0.1.0"
#endif

[Setup]
AppName=SchoolGuard
AppVersion={#MyVersion}
DefaultDirName={autopf}\SchoolGuard
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=SchoolGuard-Setup-{#MyVersion}
Compression=lzma2
SolidCompression=yes

[Files]
Source: "..\publish\agent\*";   DestDir: "{app}\agent";   Flags: recursesubdirs ignoreversion
Source: "..\publish\updater\*"; DestDir: "{app}\updater"; Flags: recursesubdirs ignoreversion

[Run]
Filename: "sc.exe"; Parameters: "create SchoolGuardAgent binPath= ""{app}\agent\SchoolGuard.Agent.exe"" start= auto DisplayName= ""SchoolGuard Agent"""; Flags: runhidden
Filename: "sc.exe"; Parameters: "failure SchoolGuardAgent reset= 86400 actions= restart/5000/restart/5000/restart/5000"; Flags: runhidden
Filename: "sc.exe"; Parameters: "start SchoolGuardAgent"; Flags: runhidden
Filename: "sc.exe"; Parameters: "create SchoolGuardUpdater binPath= ""{app}\updater\SchoolGuard.Updater.exe"" start= delayed-auto DisplayName= ""SchoolGuard Updater"""; Flags: runhidden
Filename: "sc.exe"; Parameters: "failure SchoolGuardUpdater reset= 86400 actions= restart/5000/restart/5000/restart/5000"; Flags: runhidden
Filename: "sc.exe"; Parameters: "start SchoolGuardUpdater"; Flags: runhidden
Filename: "http://127.0.0.1:8787"; Description: "Abrir el panel de administración"; Flags: shellexec postinstall nowait skipifsilent

[UninstallRun]
Filename: "sc.exe"; Parameters: "stop SchoolGuardUpdater"; Flags: runhidden; RunOnceId: "StopUpd"
Filename: "sc.exe"; Parameters: "delete SchoolGuardUpdater"; Flags: runhidden; RunOnceId: "DelUpd"
Filename: "sc.exe"; Parameters: "stop SchoolGuardAgent"; Flags: runhidden; RunOnceId: "StopAgent"
Filename: "sc.exe"; Parameters: "delete SchoolGuardAgent"; Flags: runhidden; RunOnceId: "DelAgent"
Filename: "powershell.exe"; Parameters: "-NoProfile -Command ""Get-NetAdapter | ForEach-Object {{ Set-DnsClientServerAddress -InterfaceIndex $_.ifIndex -ResetServerAddresses }"""; Flags: runhidden; RunOnceId: "ResetDns"

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var R: Integer;
begin
  { Si se reinstala encima de una versión existente, frena los servicios para poder reemplazar archivos }
  Exec('sc.exe', 'stop SchoolGuardUpdater', '', SW_HIDE, ewWaitUntilTerminated, R);
  Exec('sc.exe', 'stop SchoolGuardAgent', '', SW_HIDE, ewWaitUntilTerminated, R);
  Sleep(2000);
  Result := '';
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    SaveStringToFile(ExpandConstant('{app}\version.txt'), '{#MyVersion}', False);
end;
