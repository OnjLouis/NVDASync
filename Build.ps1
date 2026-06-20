$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $root "src"
$portable = Join-Path $root "portable"
$exe = Join-Path $portable "NVDASync.exe"
$legacyExe = Join-Path $portable "NvdaAddonSync.exe"
$compiler = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if (-not (Test-Path $compiler)) {
	$compiler = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe"
}
if (-not (Test-Path $compiler)) {
	throw "Could not find csc.exe for .NET Framework."
}

if (Test-Path $portable) {
	$removed = $false
	for ($attempt = 1; $attempt -le 5; $attempt++) {
		try {
			Remove-Item -LiteralPath $portable -Recurse -Force
			$removed = $true
			break
		}
		catch {
			if ($attempt -eq 5) {
				throw
			}
			Start-Sleep -Milliseconds 500
		}
	}
}
New-Item -ItemType Directory -Path $portable | Out-Null

$sources = Get-ChildItem -LiteralPath $src -Filter *.cs | ForEach-Object { $_.FullName }
& $compiler /nologo /target:winexe /optimize+ /out:$exe /reference:System.dll /reference:System.Core.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Runtime.Serialization.dll /reference:System.Web.Extensions.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll $sources
if ($LASTEXITCODE -ne 0) {
	throw "Build failed."
}

Copy-Item -LiteralPath $exe -Destination $legacyExe
Copy-Item -LiteralPath (Join-Path $root "Manual.html") -Destination (Join-Path $portable "Manual.html")
Copy-Item -LiteralPath (Join-Path $root "LICENSE.txt") -Destination (Join-Path $portable "LICENSE.txt")

Write-Host "Built $exe"
