param([string]$Proxy = 'http://127.0.0.1:10808')
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$projectRoot = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $projectRoot
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
foreach ($folder in @('downloads', 'lib', 'tools', 'dist')) {
    New-Item -ItemType Directory -Force -Path $folder | Out-Null
}
function Get-Dependency([string]$Url, [string]$Path, [string]$Hash) {
    if (!(Test-Path -LiteralPath $Path)) {
        $parameters = @{ Uri = $Url; OutFile = $Path; UseBasicParsing = $true }
        if ($Proxy) { $parameters.Proxy = $Proxy }
        Invoke-WebRequest @parameters
    }
    if ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ne $Hash) {
        throw "依赖文件校验失败，请检查下载来源：$Path"
    }
}
Get-Dependency 'https://api.nuget.org/v3-flatcontainer/naudio/1.10.0/naudio.1.10.0.nupkg' 'downloads/naudio.zip' 'E4A80EED41DAD794695F9C114432DE4667D20BDFA80825AA94D159708CF29217'
Get-Dependency 'https://www.nirsoft.net/utils/soundvolumeview-x64.zip' 'downloads/soundvolumeview.zip' 'EE2C45553FB9FB31B71DB88B537C18E25C1A387A4A9009D081BDB38265C68E1F'
Get-Dependency 'https://download.vb-audio.com/Download_CABLE/VBCABLE_Driver_Pack45.zip' 'downloads/vbcable.zip' 'B950E39F01AF1D04EA623C8F6D8EB9B6EA5C477C637295FABF20631C85116BFB'
Expand-Archive 'downloads/naudio.zip' 'downloads/naudio' -Force
Expand-Archive 'downloads/soundvolumeview.zip' 'tools/SoundVolumeView' -Force
Expand-Archive 'downloads/vbcable.zip' 'tools/VBCable' -Force
$driverSignature = Get-AuthenticodeSignature 'tools/VBCable/VBCABLE_Setup_x64.exe'
if ($driverSignature.Status -ne 'Valid') { throw '音频驱动安装程序签名校验未通过' }
Copy-Item 'downloads/naudio/lib/net35/NAudio.dll' 'lib/NAudio.dll' -Force
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '需要 Windows 自带的 .NET Framework 4.x' }
& $compiler /nologo /codepage:65001 /target:winexe /platform:x64 /optimize+ /out:dist\VoiceMagic.exe /win32manifest:src\app.manifest /reference:lib\NAudio.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Core.dll src\AudioEngine.cs src\Program.cs
if ($LASTEXITCODE -ne 0) { throw '程序编译失败' }
Copy-Item 'lib/NAudio.dll' 'dist/NAudio.dll' -Force
Copy-Item 'src/VoiceMagic.exe.config' 'dist/VoiceMagic.exe.config' -Force
New-Item -ItemType Directory -Force 'dist/tools','dist/driver' | Out-Null
Copy-Item 'tools/SoundVolumeView/*' 'dist/tools' -Recurse -Force
Copy-Item 'tools/VBCable/*' 'dist/driver' -Recurse -Force
Copy-Item 'README.md','THIRD_PARTY_NOTICES.md','LICENSE-NAudio.txt' 'dist' -Force
& $compiler /nologo /codepage:65001 /target:exe /platform:x64 /out:dist\VoiceMagic.Tests.exe /reference:lib\NAudio.dll /reference:System.Core.dll src\AudioEngine.cs tests\AudioTests.cs
if ($LASTEXITCODE -ne 0) { throw '验证程序编译失败' }
& 'dist/VoiceMagic.Tests.exe'
if ($LASTEXITCODE -ne 0) { throw '音频处理验证失败' }
Write-Host "已生成：$projectRoot\dist\VoiceMagic.exe"
