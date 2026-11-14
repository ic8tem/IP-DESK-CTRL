$ErrorActionPreference = 'Stop'
$k = Get-UninstallRegistryKey -SoftwareName 'IP-DESK-CTRL*'
if ($k) {
  $exe = ($k | Select-Object -First 1).UninstallString -replace '"', ''
  Uninstall-ChocolateyPackage -PackageName 'ip-desk-ctrl' -FileType 'exe' -SilentArgs '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -File $exe
}
