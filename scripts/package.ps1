param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $projectRoot
if (!$SkipBuild) {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'build.ps1')
    if ($LASTEXITCODE -ne 0) { throw '构建失败，无法打包' }
}
$payloadPath = [IO.Path]::GetFullPath((Join-Path $projectRoot 'dist\package-payload'))
$allowedRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'dist')) + [IO.Path]::DirectorySeparatorChar
if (!$payloadPath.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw '打包目录超出项目 dist 范围' }
if (Test-Path -LiteralPath $payloadPath) { Remove-Item -LiteralPath $payloadPath -Recurse -Force }
New-Item -ItemType Directory -Force -Path $payloadPath,(Join-Path $projectRoot 'release') | Out-Null
foreach ($file in @('VoiceMagic.exe','VoiceMagic.exe.config','NAudio.dll','README.md','THIRD_PARTY_NOTICES.md','LICENSE-NAudio.txt','LICENSE-Fluent.txt')) {
    Copy-Item -LiteralPath (Join-Path 'dist' $file) -Destination $payloadPath
}
Copy-Item 'dist/tools','dist/driver' -Destination $payloadPath -Recurse -Force
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archivePath = Join-Path $projectRoot 'dist\VoiceMagic.payload.zip'
if (Test-Path -LiteralPath $archivePath) { Remove-Item -LiteralPath $archivePath -Force }
[IO.Compression.ZipFile]::CreateFromDirectory($payloadPath, $archivePath, [IO.Compression.CompressionLevel]::Optimal, $false)
$payloadHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
$identityPath = Join-Path $projectRoot 'dist\PackageIdentity.cs'
$identitySource = 'namespace VoiceMagicPackage { static class PackageIdentity { public const string PayloadSha256 = "' + $payloadHash + '"; } }'
[IO.File]::WriteAllText($identityPath, $identitySource, [Text.UTF8Encoding]::new($false))
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$output = Join-Path $projectRoot 'release\VoiceMagic.exe'
& $compiler /nologo /codepage:65001 /target:winexe /platform:x64 /optimize+ ('/out:' + $output) /win32manifest:src\app.manifest /win32icon:assets\icons\VoiceMagic.ico /reference:System.Core.dll /reference:System.Windows.Forms.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll ('/resource:' + $archivePath + ',VoiceMagic.Package.zip') packaging\Launcher.cs $identityPath
if ($LASTEXITCODE -ne 0) { throw '单文件 EXE 打包失败' }
$check = Start-Process -FilePath $output -ArgumentList '--package-check' -WindowStyle Hidden -PassThru -Wait
if ($check.ExitCode -ne 0) { throw '程序包校验失败' }
$checksum = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash
[IO.File]::WriteAllText((Join-Path $projectRoot 'release\SHA256.txt'), $checksum + '  VoiceMagic.exe' + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
Write-Host "单文件 EXE：$output"
Write-Host "Payload SHA256：$payloadHash"
