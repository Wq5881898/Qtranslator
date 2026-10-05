#define MyAppName "Qtranslator"
#define MyAppVersion "1.2.1"
#define MyAppPublisher "Wq5881898"

[Setup]
AppId={{D9373B62-6B6A-4BFE-9728-398AE02878EA}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={%LOCALAPPDATA}\Programs\Qtranslator
DefaultGroupName=Qtranslator
DisableProgramGroupPage=yes
OutputDir=..\installer-output
OutputBaseFilename=Qtranslator-Setup-v1.2.1
Compression=lzma2/max
SolidCompression=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\desktop\Qtranslator.exe
WizardStyle=modern

[Files]
Source: "..\staging\desktop\*"; DestDir: "{app}\desktop"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\staging\bridge-host\*"; DestDir: "{app}\bridge-host"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\staging\extension\*"; DestDir: "{app}\extension"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\staging\README_ZH.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\staging\TEST_REPORT_ZH.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "INSTALL_AFTER_SETUP_ZH.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Qtranslator"; Filename: "{app}\desktop\Qtranslator.exe"; WorkingDir: "{app}\desktop"
Name: "{autodesktop}\Qtranslator"; Filename: "{app}\desktop\Qtranslator.exe"; WorkingDir: "{app}\desktop"
Name: "{autoprograms}\Qtranslator 浏览器插件文件夹"; Filename: "{app}\extension"
Name: "{autoprograms}\Qtranslator 安装说明"; Filename: "{app}\INSTALL_AFTER_SETUP_ZH.txt"

[Registry]
Root: HKCU64; Subkey: "Software\Google\Chrome\NativeMessagingHosts\com.wq5881898.qtranslator"; ValueType: string; ValueName: ""; ValueData: "{%LOCALAPPDATA}\Qtranslator\bridge\com.wq5881898.qtranslator.json"; Flags: uninsdeletekey
Root: HKCU32; Subkey: "Software\Google\Chrome\NativeMessagingHosts\com.wq5881898.qtranslator"; ValueType: string; ValueName: ""; ValueData: "{%LOCALAPPDATA}\Qtranslator\bridge\com.wq5881898.qtranslator.json"; Flags: uninsdeletekey

[Run]
Filename: "{app}\desktop\Qtranslator.exe"; Description: "启动 Qtranslator"; Flags: nowait postinstall skipifsilent
Filename: "{app}\extension"; Description: "打开浏览器插件文件夹"; Flags: shellexec postinstall unchecked skipifsilent
Filename: "{app}\INSTALL_AFTER_SETUP_ZH.txt"; Description: "查看浏览器插件安装说明"; Flags: shellexec postinstall unchecked skipifsilent

[UninstallDelete]
Type: files; Name: "{%LOCALAPPDATA}\Qtranslator\bridge\com.wq5881898.qtranslator.json"
Type: dirifempty; Name: "{%LOCALAPPDATA}\Qtranslator\bridge"

[Code]
procedure RunHiddenAndWait(FileName: String; Parameters: String);
var
  ResultCode: Integer;
begin
  Exec(
    FileName,
    Parameters,
    '',
    SW_HIDE,
    ewWaitUntilTerminated,
    ResultCode);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Attempt: Integer;
begin
  { Chrome may cache the manifest and immediately restart an old Native Host.
    Temporarily remove both registry views before terminating it. The [Registry]
    entries and post-install registration restore them after files are replaced. }
  RegDeleteKeyIncludingSubkeys(
    HKCU64,
    'Software\Google\Chrome\NativeMessagingHosts\com.wq5881898.qtranslator');
  RegDeleteKeyIncludingSubkeys(
    HKCU32,
    'Software\Google\Chrome\NativeMessagingHosts\com.wq5881898.qtranslator');
  DeleteFile(
    ExpandConstant(
      '{%LOCALAPPDATA}\Qtranslator\bridge\com.wq5881898.qtranslator.json'));

  { Release application and runtime files before an in-place upgrade. }
  for Attempt := 1 to 3 do
  begin
    RunHiddenAndWait(
      ExpandConstant('{sys}\taskkill.exe'),
      '/f /t /im Qtranslator.BridgeHost.exe');
    Sleep(500);
  end;
  RunHiddenAndWait(
    ExpandConstant('{sys}\taskkill.exe'),
    '/f /t /im Qtranslator.exe');
  Sleep(1000);
  Result := '';
end;

procedure WriteNativeMessagingManifest;
var
  ManifestDirectory: String;
  ManifestPath: String;
  HostPath: String;
  EscapedHostPath: String;
  Manifest: String;
  RegistrationResult: Integer;
begin
  ManifestDirectory := ExpandConstant('{%LOCALAPPDATA}\Qtranslator\bridge');
  ManifestPath := ManifestDirectory + '\com.wq5881898.qtranslator.json';
  HostPath := ExpandConstant('{app}\bridge-host\Qtranslator.BridgeHost.exe');
  EscapedHostPath := HostPath;
  StringChangeEx(EscapedHostPath, '\', '\\', True);
  ForceDirectories(ManifestDirectory);
  Manifest :=
    '{' + #13#10 +
    '  "name": "com.wq5881898.qtranslator",' + #13#10 +
    '  "description": "Qtranslator Stage 2 browser bridge",' + #13#10 +
    '  "path": "' + EscapedHostPath + '",' + #13#10 +
    '  "type": "stdio",' + #13#10 +
    '  "allowed_origins": [' + #13#10 +
    '    "chrome-extension://kjgoachjhbfkijaopcmallakpfnalkdo/"' + #13#10 +
    '  ]' + #13#10 +
    '}' + #13#10;
  if not SaveStringToFile(ManifestPath, Manifest, False) then
    RaiseException('无法创建 Chrome Bridge 配置文件：' + ManifestPath);

  if not Exec(
    ExpandConstant('{app}\desktop\Qtranslator.exe'),
    '--register-bridge',
    ExpandConstant('{app}\desktop'),
    SW_HIDE,
    ewWaitUntilTerminated,
    RegistrationResult) or (RegistrationResult <> 0) then
    RaiseException(
      'Chrome Bridge 自动注册失败，错误代码：' +
      IntToStr(RegistrationResult) + '。');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    WriteNativeMessagingManifest;
end;
