# Build both per-user installers with Inno Setup and smoke-test them on this machine (CI: windows-latest):
#   KeySwitch-Setup.exe       framework-dependent build, checks for the .NET 8 Desktop Runtime
#   KeySwitch-Setup-full.exe  self-contained build
# Each is installed silently, checked (files, Start menu shortcut, autostart value, uninstall entry), and
# uninstalled again (everything gone). Run from the repository root after tools/build.sh.
$ErrorActionPreference = 'Stop'

$version = ([xml](Get-Content src/KeySwitch.App/KeySwitch.App.csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$iscc = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
if (-not (Test-Path $iscc)) {
  Write-Host 'Inno Setup not found; installing it with Chocolatey'
  choco install innosetup -y --no-progress
  if ($LASTEXITCODE -ne 0) { throw "choco install innosetup failed ($LASTEXITCODE)" }
}
if (-not (Test-Path $iscc)) { throw "ISCC.exe not found at $iscc" }

$dist = Join-Path $PWD 'dist'
New-Item -ItemType Directory -Force artifacts | Out-Null
$packages = @(
  @{ Name = 'KeySwitch-Setup';      Exe = Join-Path $dist 'small\KeySwitch.exe'; Defines = @('/DNeedsRuntime=1') },
  @{ Name = 'KeySwitch-Setup-full'; Exe = Join-Path $dist 'KeySwitch.exe';       Defines = @() }
)
foreach ($package in $packages) {
  $arguments = @("/DAppVersion=$version", "/DSourceExe=$($package.Exe)") + $package.Defines + @("/O$dist", "/F$($package.Name)", 'tools\installer\KeySwitch.iss')
  Write-Host "::group::ISCC $($package.Name)"
  & $iscc @arguments
  if ($LASTEXITCODE -ne 0) { throw "ISCC failed for $($package.Name) ($LASTEXITCODE)" }
  Write-Host '::endgroup::'
}

$app = Join-Path $env:LOCALAPPDATA 'Programs\KeySwitch'
$shortcut = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\KeySwitch.lnk'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$settings = Join-Path $env:APPDATA 'KeySwitch'
function Get-Autostart { (Get-ItemProperty -Path $runKey -ErrorAction SilentlyContinue).KeySwitch }
function Get-UninstallEntry {
  Get-ChildItem 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall' -ErrorAction SilentlyContinue |
    Where-Object { $_.GetValue('DisplayName') -eq 'KeySwitch' }
}
function Assert($condition, $message) { if (-not $condition) { throw "Installer check failed: $message" } }

foreach ($package in $packages) {
  $setup = Join-Path $dist "$($package.Name).exe"
  Write-Host "::group::install / uninstall $($package.Name)"
  $log = Join-Path $PWD "artifacts\$($package.Name).log"
  $install = Start-Process -FilePath $setup -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/TASKS=autostart', "/LOG=$log" -PassThru -Wait
  if (Test-Path $log) { Get-Content $log -Tail 15 }
  Assert ($install.ExitCode -eq 0) "setup exit code $($install.ExitCode)"
  Assert (Test-Path (Join-Path $app 'KeySwitch.exe')) 'KeySwitch.exe was not installed'
  Assert ((Get-FileHash (Join-Path $app 'KeySwitch.exe')).Hash -eq (Get-FileHash $package.Exe).Hash) 'installed KeySwitch.exe differs from the build'
  Assert (Test-Path (Join-Path $app 'README_RU.md')) 'README_RU.md was not installed'
  Assert (Test-Path (Join-Path $app 'licenses\DATASETS.md')) 'licenses were not installed'
  Assert (Test-Path $shortcut) 'Start menu shortcut is missing'
  Assert ((Get-Autostart) -eq "`"$(Join-Path $app 'KeySwitch.exe')`"") "autostart value is '$(Get-Autostart)'"
  $entry = Get-UninstallEntry
  Assert ($null -ne $entry) 'uninstall entry is missing'
  Assert ($entry.GetValue('DisplayVersion') -eq $version) "uninstall entry version is '$($entry.GetValue('DisplayVersion'))'"
  # The installed copy passes the same headless selftest as the portable builds.
  $selftest = Start-Process -FilePath (Join-Path $app 'KeySwitch.exe') -ArgumentList '--selftest', '--quiet' -PassThru
  $null = $selftest.Handle
  if (-not $selftest.WaitForExit(120000)) { $selftest.Kill(); throw 'Installer check failed: selftest of the installed copy timed out' }
  Assert ($selftest.ExitCode -eq 0) "selftest of the installed copy exited with $($selftest.ExitCode)"
  Assert (Test-Path $settings) 'the selftest should have created the settings folder'

  $uninstaller = Get-ChildItem $app -Filter 'unins*.exe' | Select-Object -First 1
  Assert ($null -ne $uninstaller) 'uninstaller is missing'
  Start-Process -FilePath $uninstaller.FullName -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -Wait
  # The uninstaller finishes in a copy of itself: wait for the folder to go.
  for ($i = 0; $i -lt 60 -and (Test-Path $app); $i++) { Start-Sleep -Seconds 1 }
  Assert (-not (Test-Path $app)) 'the program folder is still there after uninstall'
  Assert (-not (Test-Path $shortcut)) 'the Start menu shortcut is still there after uninstall'
  Assert ($null -eq (Get-Autostart)) 'the autostart value is still there after uninstall'
  Assert ($null -eq (Get-UninstallEntry)) 'the uninstall entry is still there after uninstall'
  Assert (-not (Test-Path $settings)) 'settings are still there after a silent (clean) uninstall'
  Write-Host '::endgroup::'
  Write-Host "$($package.Name): install, selftest and uninstall passed"
}

# Checksums next to the zips.
foreach ($package in $packages) {
  $file = "$($package.Name).exe"
  $hash = (Get-FileHash (Join-Path $dist $file) -Algorithm SHA256).Hash.ToLowerInvariant()
  Add-Content -Path (Join-Path $dist 'SHA256SUMS-all') -Value "$hash  $file" -Encoding ascii
  Write-Host "$file $((Get-Item (Join-Path $dist $file)).Length) bytes SHA256 $hash"
}
