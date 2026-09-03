[CmdletBinding()]
param(
	[string]$OutputRoot = (Join-Path ([IO.Path]::GetTempPath()) 'NVDASync-build'),
	[string]$SigningKeyPath
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $root 'src'
$portable = Join-Path $OutputRoot 'portable'
$archive = Join-Path $OutputRoot 'NVDASync.zip'
$signature = $archive + '.sig'
$exe = Join-Path $portable 'NVDASync.exe'
$legacyExe = Join-Path $portable 'NvdaAddonSync.exe'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'

if (-not (Test-Path -LiteralPath $compiler)) {
	$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) {
	throw 'Could not find csc.exe for .NET Framework.'
}

if (Test-Path -LiteralPath $OutputRoot) {
	Remove-Item -LiteralPath $OutputRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $portable -Force | Out-Null

$sources = Get-ChildItem -LiteralPath $src -Filter '*.cs' | ForEach-Object FullName
& $compiler /nologo /target:winexe /optimize+ "/out:$exe" /reference:System.dll /reference:System.Core.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Runtime.Serialization.dll /reference:System.Web.Extensions.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll $sources
if ($LASTEXITCODE -ne 0) {
	throw 'Build failed.'
}

Copy-Item -LiteralPath $exe -Destination $legacyExe
Copy-Item -LiteralPath (Join-Path $root 'Manual.html') -Destination $portable
Copy-Item -LiteralPath (Join-Path $root 'LICENSE.txt') -Destination $portable
@"
[InternetShortcut]
URL=https://github.com/OnjLouis/NVDASync/releases/latest
"@ | Set-Content -LiteralPath (Join-Path $portable 'Get latest NVDA Sync.url') -Encoding ASCII

Get-ChildItem -LiteralPath $portable | Compress-Archive -DestinationPath $archive -CompressionLevel Optimal
if ($SigningKeyPath) {
	if (-not (Test-Path -LiteralPath $SigningKeyPath -PathType Leaf)) {
		throw "Signing key not found: $SigningKeyPath"
	}
	$rsa = New-Object System.Security.Cryptography.RSACryptoServiceProvider
	try {
		$rsa.FromXmlString([IO.File]::ReadAllText($SigningKeyPath))
		$bytes = [IO.File]::ReadAllBytes($archive)
		$hash = [Security.Cryptography.SHA256]::Create()
		try { $signed = $rsa.SignData($bytes, $hash) } finally { $hash.Dispose() }
		[IO.File]::WriteAllText($signature, [Convert]::ToBase64String($signed), (New-Object Text.UTF8Encoding($false)))
	}
	finally {
		$rsa.PersistKeyInCsp = $false
		$rsa.Dispose()
	}
}

Write-Host "Built $portable"
Write-Host "Packaged $archive"
if (Test-Path -LiteralPath $signature) { Write-Host "Signed $signature" }
