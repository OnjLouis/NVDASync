$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
& (Join-Path $root "Build.ps1")

function Assert-UniqueMainWindowMnemonics {
	$source = Get-Content -LiteralPath (Join-Path $root "src\MainForm.cs") -Raw
	$matches = [regex]::Matches($source, '\.Text\s*=\s*"([^"]*&[^"]*)"')
	$seen = @{}
	foreach ($match in $matches) {
		$text = $match.Groups[1].Value
		$index = $text.IndexOf('&')
		while ($index -ge 0 -and $index + 1 -lt $text.Length -and $text[$index + 1] -eq '&') {
			$index = $text.IndexOf('&', $index + 2)
		}
		if ($index -lt 0 -or $index + 1 -ge $text.Length) {
			continue
		}
		$key = ([string]$text[$index + 1]).ToUpperInvariant()
		if ($seen.ContainsKey($key)) {
			throw "Duplicate main-window mnemonic Alt+$key`: '$($seen[$key])' and '$text'."
		}
		$seen[$key] = $text
	}

	$expected = @{
		P = '&Primary NVDA folder'
		B = '&Browse...'
		D = '&Detect...'
		S = '&Sync now'
		C = '&Cancel sync'
		V = '&Validate'
		N = 'Mi&nimise to tray'
	}
	foreach ($key in $expected.Keys) {
		if (-not $seen.ContainsKey($key) -or $seen[$key] -ne $expected[$key]) {
			throw "Main-window mnemonic Alt+$key missing or changed. Expected '$($expected[$key])'."
		}
	}
	Write-Host "main-window-mnemonics-ok"
}

function Assert-UniquePreferencesMnemonics {
	$source = Get-Content -LiteralPath (Join-Path $root "src\PreferencesForm.cs") -Raw
	$matches = [regex]::Matches($source, '\.Text\s*=\s*"([^"]*&[^"]*)"')
	$seen = @{}
	foreach ($match in $matches) {
		$text = $match.Groups[1].Value
		$index = $text.IndexOf('&')
		while ($index -ge 0 -and $index + 1 -lt $text.Length -and $text[$index + 1] -eq '&') {
			$index = $text.IndexOf('&', $index + 2)
		}
		if ($index -lt 0 -or $index + 1 -ge $text.Length) {
			continue
		}
		$key = ([string]$text[$index + 1]).ToUpperInvariant()
		if ($seen.ContainsKey($key)) {
			throw "Duplicate Preferences mnemonic Alt+$key`: '$($seen[$key])' and '$text'."
		}
		$seen[$key] = $text
	}

	$expected = @{
		C = '&Components to sync'
		W = '&Watch primary folder and sync when it changes'
		D = '&Delete stale items from secondary folders'
		X = 'E&xclude Python cache files'
		S = 'Run NVDA Sync at Windows &startup'
		M = 'Start &minimized to the notification area'
		U = 'Check for &updates'
		I = '&Install updates silently when possible'
	}
	foreach ($key in $expected.Keys) {
		if (-not $seen.ContainsKey($key) -or $seen[$key] -ne $expected[$key]) {
			throw "Preferences mnemonic Alt+$key missing or changed. Expected '$($expected[$key])'."
		}
	}
	Write-Host "preferences-mnemonics-ok"
}

function Assert-ShortcutSource {
	$mainSource = Get-Content -LiteralPath (Join-Path $root "src\MainForm.cs") -Raw
	foreach ($required in @(
		'Keys.F1',
		'Keys.Shift | Keys.F1',
		'Keys.Control | Keys.F1',
		'Keys.Control | Keys.Oemcomma',
		'keyData == Keys.Delete && secondaryListBox.Focused'
	)) {
		if ($mainSource -notlike "*$required*") {
			throw "MainForm.cs missing expected shortcut source: $required"
		}
	}
	Write-Host "shortcut-source-ok"
}

function Read-ManualChangelogEntry([string]$version) {
	$manual = Get-Content -LiteralPath (Join-Path $root "Manual.html") -Raw
	$escapedVersion = [regex]::Escape($version)
	$match = [regex]::Match($manual, "<h3>$escapedVersion</h3>(?<entry>.*?)(<h3>|<h2 id=""license"">)", [System.Text.RegularExpressions.RegexOptions]::Singleline)
	if (-not $match.Success) {
		throw "Manual.html does not contain a changelog entry for $version."
	}
	return $match.Groups["entry"].Value
}

function Assert-ChangelogClean([string]$version) {
	$entry = Read-ManualChangelogEntry $version
	foreach ($term in @(
		"smoke test",
		"self-test",
		"test-only",
		"debug-only",
		"internal",
		"behind the scenes",
		"implementation",
		"developer-only",
		"private",
		"build process"
	)) {
		if ($entry.IndexOf($term, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
			throw "Changelog for $version contains development/private wording: $term"
		}
	}
	Write-Host "changelog-clean-ok"
}

Assert-UniqueMainWindowMnemonics
Assert-UniquePreferencesMnemonics
Assert-ShortcutSource
Assert-ChangelogClean "1.0.0"

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("nvda-addon-sync-smoke-" + [guid]::NewGuid())
$primary = Join-Path $tempRoot "primary"
$secondary = Join-Path $tempRoot "secondary"
$nestedSecondary = Join-Path $primary "bad-secondary"
$portableNvda = Join-Path $tempRoot "portableNvda"
$cliPrimary = Join-Path $tempRoot "cliNvda"
	$cliPrimaryAddons = Join-Path $cliPrimary "userConfig\addons"
	$cliSecondary = Join-Path $tempRoot "cliSecondary"
	$cliSecondaryGestures = Join-Path $tempRoot "cliSecondaryGestures"
$componentPrimary = Join-Path $tempRoot "componentPrimary\userConfig"
$componentSecondaryAddons = Join-Path $tempRoot "componentSecondary\userConfig\addons"
$usedDrives = [IO.DriveInfo]::GetDrives() | ForEach-Object { $_.Name.Substring(0, 1).ToUpperInvariant() }
$missingDriveLetter = ([char[]](90..68 | ForEach-Object { [char]$_ }) | Where-Object { $usedDrives -notcontains ([string]$_) } | Select-Object -First 1)
if (-not $missingDriveLetter) {
	throw "Could not find an unused drive letter for unavailable-drive smoke test."
}
$missingSecondary = "$missingDriveLetter`:\nvda"

try {
	New-Item -ItemType Directory -Path (Join-Path $primary "addons\addonA") -Force | Out-Null
	New-Item -ItemType Directory -Path (Join-Path $primary "addons\addonA\__pycache__") -Force | Out-Null
	New-Item -ItemType Directory -Path (Join-Path $primary "sonata\voices") -Force | Out-Null
	New-Item -ItemType Directory -Path (Join-Path $secondary "addons\oldAddon") -Force | Out-Null
	New-Item -ItemType Directory -Path (Join-Path $secondary "addons\addonA\__pycache__") -Force | Out-Null
	New-Item -ItemType Directory -Path (Join-Path $secondary "oldAddonData") -Force | Out-Null
	New-Item -ItemType Directory -Path (Join-Path $portableNvda "userConfig\addons") -Force | Out-Null
	New-Item -ItemType Directory -Path (Join-Path $cliPrimaryAddons "addonB") -Force | Out-Null
	New-Item -ItemType Directory -Path (Join-Path $cliPrimary "userConfig\sonata") -Force | Out-Null
	New-Item -ItemType Directory -Path $cliSecondary -Force | Out-Null
	New-Item -ItemType Directory -Path $cliSecondaryGestures -Force | Out-Null
	New-Item -ItemType Directory -Path (Join-Path $componentPrimary "addons") -Force | Out-Null
	New-Item -ItemType Directory -Path $componentSecondaryAddons -Force | Out-Null
	Set-Content -LiteralPath (Join-Path $portableNvda "nvda.exe") -Value "" -Encoding UTF8
	Set-Content -LiteralPath (Join-Path $cliPrimary "nvda.exe") -Value "" -Encoding UTF8
	Set-Content -LiteralPath (Join-Path $cliPrimary "userConfig\gestures.ini") -Value "cli gestures" -Encoding UTF8
	Set-Content -LiteralPath (Join-Path $primary "gestures.ini") -Value "gestures" -Encoding UTF8
	Set-Content -LiteralPath (Join-Path $primary "addons\addonA\manifest.ini") -Value "name = addonA" -Encoding UTF8
	Set-Content -LiteralPath (Join-Path $primary "addons\addonA\__pycache__\module.pyc") -Value "cache" -Encoding UTF8
	Set-Content -LiteralPath (Join-Path $primary "sonata\voices\kirsten.txt") -Value "voice data" -Encoding UTF8
	Set-Content -LiteralPath (Join-Path $secondary "addons\oldAddon\manifest.ini") -Value "stale" -Encoding UTF8
	Set-Content -LiteralPath (Join-Path $secondary "addons\addonA\__pycache__\old.pyc") -Value "old cache" -Encoding UTF8
	Set-Content -LiteralPath (Join-Path $secondary "oldAddonData\stale.txt") -Value "old data" -Encoding UTF8
	Set-Content -LiteralPath (Join-Path $cliPrimaryAddons "addonB\manifest.ini") -Value "name = addonB" -Encoding UTF8
	Set-Content -LiteralPath (Join-Path $cliPrimary "userConfig\sonata\voice.txt") -Value "cli voice" -Encoding UTF8
	Set-Content -LiteralPath (Join-Path $componentPrimary "gestures.ini") -Value "component gestures" -Encoding UTF8
	New-Item -ItemType Directory -Path $nestedSecondary -Force | Out-Null

	$harness = Join-Path $tempRoot "Harness.cs"
	@"
using System;
using NvdaAddonSync;

class Harness
{
	static int Main(string[] args)
	{
		var engine = new SyncEngine();
		var result = engine.Sync(args[0], new[] { args[1] }, new SyncOptions { DeleteStaleItems = true, ExcludePythonCache = true, Components = new System.Collections.Generic.List<SyncComponent> { SyncComponent.Addons, SyncComponent.InputGestures, SyncComponent.OtherConfigFolders } });
		Console.WriteLine("components=" + result.ComponentsSynced + " copied=" + result.FilesCopied + " ignored=" + result.ItemsIgnored + " deleted=" + result.ItemsDeleted + " errors=" + result.Errors);
		if (result.ItemsIgnored == 0)
		{
			Console.WriteLine("python-cache-exclusion-failed");
			return 5;
		}
		var missingResult = engine.Sync(args[0], new[] { args[4] }, new SyncOptions { DeleteStaleItems = true, Components = new System.Collections.Generic.List<SyncComponent> { SyncComponent.Addons } });
		if (missingResult.Errors != 0 || missingResult.UnavailableTargets != 1)
		{
			Console.WriteLine("missing-secondary-check-failed errors=" + missingResult.Errors + " unavailable=" + missingResult.UnavailableTargets);
			return 4;
		}
		Console.WriteLine("missing-secondary-check-ok");
		var unsafeResult = engine.Sync(args[0], new[] { args[2] }, new SyncOptions { DeleteStaleItems = true, Components = new System.Collections.Generic.List<SyncComponent> { SyncComponent.Addons } });
		if (unsafeResult.Errors == 0)
		{
			Console.WriteLine("nested-folder-check-failed");
			return 2;
		}
		Console.WriteLine("nested-folder-check-ok");
		var resolved = SyncEngine.ResolveAddonsDirectory(args[3], "Portable NVDA folder", true);
		if (!resolved.EndsWith(@"userConfig\addons", StringComparison.OrdinalIgnoreCase))
		{
			Console.WriteLine("folder-resolution-failed: " + resolved);
			return 3;
		}
		Console.WriteLine("folder-resolution-ok");
		var componentResolved = SyncEngine.ResolveNvdaConfigDirectory(args[5], "Component folder", true);
		if (!componentResolved.EndsWith(@"userConfig", StringComparison.OrdinalIgnoreCase))
		{
			Console.WriteLine("component-folder-resolution-failed: " + componentResolved);
			return 6;
		}
		Console.WriteLine("component-folder-resolution-ok");
		var componentFolderResult = engine.Sync(args[6], new[] { args[5] }, new SyncOptions { DeleteStaleItems = true, Components = new System.Collections.Generic.List<SyncComponent> { SyncComponent.InputGestures } });
		if (componentFolderResult.Errors != 0)
		{
			Console.WriteLine("component-folder-sync-failed errors=" + componentFolderResult.Errors);
			return 7;
		}
		Console.WriteLine("component-folder-sync-ok");
		return result.Errors == 0 ? 0 : 1;
	}
}
"@ | Set-Content -LiteralPath $harness -Encoding UTF8

	$compiler = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
	if (-not (Test-Path $compiler)) {
		$compiler = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe"
	}
	$testExe = Join-Path $tempRoot "Harness.exe"
	& $compiler /nologo /target:exe /out:$testExe /reference:System.dll /reference:System.Core.dll /reference:System.Runtime.Serialization.dll (Join-Path $root "src\AppSettings.cs") (Join-Path $root "src\SyncEngine.cs") $harness
	if ($LASTEXITCODE -ne 0) {
		throw "Harness build failed."
	}
	& $testExe $primary $secondary $nestedSecondary $portableNvda $missingSecondary $componentSecondaryAddons $componentPrimary
	if ($LASTEXITCODE -ne 0) {
		throw "Harness failed."
	}
	if (-not (Test-Path -LiteralPath (Join-Path $secondary "addons\addonA\manifest.ini"))) {
		throw "Expected copied file was missing."
	}
	if (-not (Test-Path -LiteralPath (Join-Path $secondary "gestures.ini"))) {
		throw "Expected gestures.ini to be copied."
	}
	if (Test-Path -LiteralPath (Join-Path $secondary "addons\addonA\__pycache__\module.pyc")) {
		throw "Python cache file should not be copied by default."
	}
	if (Test-Path -LiteralPath (Join-Path $secondary "addons\oldAddon")) {
		throw "Expected stale folder to be deleted."
	}
	if (-not (Test-Path -LiteralPath (Join-Path $secondary "sonata\voices\kirsten.txt"))) {
		throw "Other root configuration folders did not copy Sonata-style data."
	}
	if (Test-Path -LiteralPath (Join-Path $secondary "oldAddonData")) {
		throw "Other root configuration folders did not delete stale root folder."
	}
	if (-not (Test-Path -LiteralPath (Join-Path (Split-Path -Parent $componentSecondaryAddons) "gestures.ini"))) {
		throw "Component-folder secondary did not sync gestures.ini beside addons."
	}
	if (Test-Path -LiteralPath (Join-Path $componentSecondaryAddons "gestures.ini")) {
		throw "Component-folder secondary incorrectly synced gestures.ini inside addons."
	}
	if (Test-Path -LiteralPath (Join-Path $root "portable\README.md")) {
		throw "README.md should not be in portable output."
	}
	if (-not (Test-Path -LiteralPath (Join-Path $root "portable\Manual.html"))) {
		throw "Manual.html missing from portable output."
	}
	if (-not (Test-Path -LiteralPath (Join-Path $root "portable\LICENSE.txt"))) {
		throw "LICENSE.txt missing from portable output."
	}
	$manual = Get-Content -LiteralPath (Join-Path $root "portable\Manual.html") -Raw
	foreach ($requiredManualText in @(
		'<h2 id="keyboard-shortcuts">Keyboard Shortcuts</h2>',
		'<h2 id="menus">Menus</h2>',
		'<h2 id="preferences">Preferences</h2>',
		'<h2 id="command-line">Command Line</h2>',
		'<h2 id="changelog">Changelog</h2>',
		'<h3>1.0.0</h3>',
		'<h2 id="credits">Credits</h2>'
	)) {
		if ($manual -notlike "*$requiredManualText*") {
			throw "Manual.html missing expected section: $requiredManualText"
		}
	}

	$builtExe = Join-Path $root "portable\NvdaAddonSync.exe"
	$cliRun = Start-Process -FilePath $builtExe -ArgumentList @(
		"--sync",
		"--primary", $cliPrimary,
		"--secondary", $cliSecondary,
		"--delete-stale"
	) -Wait -PassThru
	if ($cliRun.ExitCode -ne 0) {
		throw "Command-line sync failed with exit code $($cliRun.ExitCode)."
	}
	if (-not (Test-Path -LiteralPath (Join-Path $cliSecondary "userConfig\addons\addonB\manifest.ini"))) {
		throw "Command-line sync did not copy addonB."
	}
	$cliComponentRun = Start-Process -FilePath $builtExe -ArgumentList @(
		"--sync",
		"--primary", $cliPrimary,
		"--secondary", $cliSecondaryGestures,
		"--component", "inputGestures",
		"--component", "otherConfigFolders",
		"--no-delete-stale"
	) -Wait -PassThru
	if ($cliComponentRun.ExitCode -ne 0) {
		throw "Command-line component sync failed with exit code $($cliComponentRun.ExitCode)."
	}
	if (-not (Test-Path -LiteralPath (Join-Path $cliSecondaryGestures "userConfig\gestures.ini"))) {
		throw "Command-line component sync did not copy gestures.ini."
	}
	if (-not (Test-Path -LiteralPath (Join-Path $cliSecondaryGestures "userConfig\sonata\voice.txt"))) {
		throw "Command-line otherConfigFolders sync did not copy Sonata-style data."
	}

	$updateTarget = Join-Path $tempRoot "updateTarget"
	$updateTemp = Join-Path $tempRoot "updateTemp"
	$updateZip = Join-Path $tempRoot "NVDASync-update.zip"
	Copy-Item -LiteralPath (Join-Path $root "portable") -Destination $updateTarget -Recurse
	New-Item -ItemType Directory -Path (Join-Path $updateTarget "Settings") -Force | Out-Null
	New-Item -ItemType Directory -Path (Join-Path $updateTarget "Logs") -Force | Out-Null
	Set-Content -LiteralPath (Join-Path $updateTarget "Settings\sentinel.txt") -Value "keep settings" -Encoding UTF8
	Set-Content -LiteralPath (Join-Path $updateTarget "Logs\sentinel.log") -Value "keep logs" -Encoding UTF8
	if (Test-Path -LiteralPath $updateZip) {
		Remove-Item -LiteralPath $updateZip -Force
	}
	Compress-Archive -Path (Join-Path $root "portable\*") -DestinationPath $updateZip -Force
	$updateRun = Start-Process -FilePath $builtExe -ArgumentList @(
		"--apply-update",
		"--update-url", ([Uri]$updateZip).AbsoluteUri,
		"--update-target", $updateTarget,
		"--update-exe", (Join-Path $updateTarget "NvdaAddonSync.exe"),
		"--update-temp", $updateTemp,
		"--update-wait-pid", "0",
		"--update-no-restart"
	) -Wait -PassThru
	if ($updateRun.ExitCode -ne 0) {
		throw "Local updater smoke failed with exit code $($updateRun.ExitCode)."
	}
	if (-not (Test-Path -LiteralPath (Join-Path $updateTarget "Settings\sentinel.txt"))) {
		throw "Local updater did not preserve Settings."
	}
	if (-not (Test-Path -LiteralPath (Join-Path $updateTarget "Logs\sentinel.log"))) {
		throw "Local updater did not preserve Logs."
	}
	if (Test-Path -LiteralPath (Join-Path $updateTarget "Logs\Updater.log")) {
		throw "Local updater wrote an error log."
	}
	if (-not (Test-Path -LiteralPath (Join-Path $updateTarget "Logs\Update.log"))) {
		throw "Local updater did not write Update.log."
	}

	$guiProcess = Start-Process -FilePath $builtExe -PassThru
	Start-Sleep -Seconds 2
	$runningSync = Start-Process -FilePath $builtExe -ArgumentList @("--sync-running") -Wait -PassThru
	if ($runningSync.ExitCode -ne 0) {
		throw "--sync-running did not signal the running app."
	}
	$closeRun = Start-Process -FilePath $builtExe -ArgumentList @("--close") -Wait -PassThru
	if ($closeRun.ExitCode -ne 0) {
		throw "--close did not signal the running app."
	}
	if (-not $guiProcess.WaitForExit(10000)) {
		$guiProcess.Kill()
		throw "Running app did not close after --close."
	}
	if (-not (Test-Path -LiteralPath (Join-Path $root "portable\Logs\NVDASync.log"))) {
		throw "GUI run did not create Logs\NVDASync.log."
	}
	foreach ($runtimeFolder in @("Logs", "Settings")) {
		$runtimePath = Join-Path $root ("portable\" + $runtimeFolder)
		if (Test-Path -LiteralPath $runtimePath) {
			Remove-Item -LiteralPath $runtimePath -Recurse -Force
		}
	}
	Write-Host "Smoke test passed."
}
finally {
	if (Test-Path -LiteralPath $tempRoot) {
		Remove-Item -LiteralPath $tempRoot -Recurse -Force
	}
}
