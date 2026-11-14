$ErrorActionPreference = 'Stop'
# La compilazione sostituisce VERSIONE e le impronte SHA-256 dei setup.
$v = 'VERSIONE'
$url = "https://github.com/ic8tem/IP-DESK-CTRL/releases/download/v$v/IP-DESK-CTRL-$v-win-x86-setup.exe"
$url64 = "https://github.com/ic8tem/IP-DESK-CTRL/releases/download/v$v/IP-DESK-CTRL-$v-win-x64-setup.exe"
if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { $url64 = "https://github.com/ic8tem/IP-DESK-CTRL/releases/download/v$v/IP-DESK-CTRL-$v-win-arm64-setup.exe" }
Install-ChocolateyPackage -PackageName 'ip-desk-ctrl' -FileType 'exe' `
  -Url $url -Checksum 'SHA_X86' -ChecksumType 'sha256' `
  -Url64bit $url64 -Checksum64 $(if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'SHA_ARM64' } else { 'SHA_X64' }) -ChecksumType64 'sha256' `
  -SilentArgs '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /ALLUSERS' -ValidExitCodes @(0)
