$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
& (Join-Path $root "Build.ps1")

function Remove-TreeWithRetry($Path) {
	for ($attempt = 1; $attempt -le 10; $attempt++) {
		try {
			if (Test-Path -LiteralPath $Path) {
				Remove-Item -LiteralPath $Path -Recurse -Force
			}
			return
		}
		catch {
			if ($attempt -eq 10) {
				throw
			}
			Start-Sleep -Milliseconds 500
		}
	}
}

function Remove-NotifyIconEntriesForPathPrefix($Prefix) {
	$rootPath = 'HKCU:\Control Panel\NotifyIconSettings'
	if (-not (Test-Path -LiteralPath $rootPath)) {
		return
	}
	Get-ChildItem -LiteralPath $rootPath -ErrorAction SilentlyContinue | ForEach-Object {
		$props = Get-ItemProperty -LiteralPath $_.PSPath -ErrorAction SilentlyContinue
		if ($props -and $props.ExecutablePath -and $props.ExecutablePath.IndexOf($Prefix, [StringComparison]::OrdinalIgnoreCase) -eq 0) {
			Remove-Item -LiteralPath $_.PSPath -Recurse -Force
		}
	}
}

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
		W = '&Watch primary folder and sync changes after 1.5 seconds'
		D = '&Delete stale items from secondary folders'
		X = 'E&xclude Python cache files'
		B = 'Create &backup ZIP before program-file updates'
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
		'keyData == Keys.Enter && secondaryListBox.Focused',
		'keyData == Keys.Delete && secondaryListBox.Focused',
		'InstanceStateStore.IsSameRunningFolder',
		'AppCommands.Signal(AppCommand.Close, previous.AppFolder)'
	)) {
		$source = if ($required -like 'InstanceStateStore*' -or $required -like 'AppCommands.Signal*') { Get-Content -LiteralPath (Join-Path $root "src\Program.cs") -Raw } else { $mainSource }
		if ($source -notlike "*$required*") {
			throw "Source missing expected shortcut or instance-control source: $required"
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
Assert-ChangelogClean "1.3.5"

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
$addonInstallSource = Join-Path $tempRoot "addonInstallSource"
$addonInstallTarget = Join-Path $tempRoot "addonInstallTarget"
$iniSource = Join-Path $tempRoot "iniSource"
$iniDestination = Join-Path $tempRoot "iniDestination"
$iniFreshDestination = Join-Path $tempRoot "iniFreshDestination"
$gestureSource = Join-Path $tempRoot "gestureSource"
$gestureDestination = Join-Path $tempRoot "gestureDestination"
$gestureFreshDestination = Join-Path $tempRoot "gestureFreshDestination"
$packExport = Join-Path $tempRoot "addon-pack.json"
$usedDrives = [IO.DriveInfo]::GetDrives() | ForEach-Object { $_.Name.Substring(0, 1).ToUpperInvariant() }
$missingDriveLetter = ([char[]](90..68 | ForEach-Object { [char]$_ }) | Where-Object { $usedDrives -notcontains ([string]$_) } | Select-Object -First 1)
if (-not $missingDriveLetter) {
	throw "Could not find an unused drive letter for unavailable-drive smoke test."
}
$missingSecondary = "$missingDriveLetter`:\nvda"

try {
	New-Item -ItemType Directory -Path (Join-Path $primary "addons\addonA") -Force | Out-Null
	New-Item -ItemType Directory -Path (Join-Path $primary "addons\addonA\__pycache__") -Force | Out-Null
	New-Item -ItemType Directory -Path (Join-Path $primary "profiles") -Force | Out-Null
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
	New-Item -ItemType Directory -Path (Join-Path $addonInstallSource "folderAddon\__pycache__") -Force | Out-Null
	New-Item -ItemType Directory -Path (Join-Path $addonInstallSource "archiveAddonBuild") -Force | Out-Null
	New-Item -ItemType Directory -Path (Join-Path $addonInstallTarget "userConfig\addons\oldName") -Force | Out-Null
	New-Item -ItemType Directory -Path $iniSource -Force | Out-Null
	New-Item -ItemType Directory -Path $iniDestination -Force | Out-Null
	New-Item -ItemType Directory -Path $iniFreshDestination -Force | Out-Null
	New-Item -ItemType Directory -Path $gestureSource -Force | Out-Null
	New-Item -ItemType Directory -Path $gestureDestination -Force | Out-Null
	New-Item -ItemType Directory -Path $gestureFreshDestination -Force | Out-Null
	Set-Content -LiteralPath (Join-Path $portableNvda "nvda.exe") -Value "" -Encoding UTF8
	Set-Content -LiteralPath (Join-Path $cliPrimary "nvda.exe") -Value "" -Encoding UTF8
	Set-Content -LiteralPath (Join-Path $cliPrimary "userConfig\gestures.ini") -Value "cli gestures" -Encoding UTF8
	Set-Content -LiteralPath (Join-Path $primary "gestures.ini") -Value "gestures" -Encoding UTF8
	Set-Content -LiteralPath (Join-Path $primary "addons\addonA\manifest.ini") -Value "name = addonA" -Encoding UTF8
	Set-Content -LiteralPath (Join-Path $primary "addons\addonA\__pycache__\module.pyc") -Value "cache" -Encoding UTF8
	Set-Content -LiteralPath (Join-Path $primary "profiles\Work.ini") -Value "[speech]`nrate = 40" -Encoding UTF8
	Set-Content -LiteralPath (Join-Path $primary "profileTriggers.ini") -Value "[triggersToProfiles]`napp:notepad = Work" -Encoding UTF8
	Set-Content -LiteralPath (Join-Path $primary "sonata\voices\kirsten.txt") -Value "voice data" -Encoding UTF8
	Set-Content -LiteralPath (Join-Path $secondary "addons\oldAddon\manifest.ini") -Value "stale" -Encoding UTF8
	Set-Content -LiteralPath (Join-Path $secondary "addons\addonA\__pycache__\old.pyc") -Value "old cache" -Encoding UTF8
	Set-Content -LiteralPath (Join-Path $secondary "profileTriggers.ini") -Value "[triggersToProfiles]`napp:stale = Old" -Encoding UTF8
	Set-Content -LiteralPath (Join-Path $secondary "oldAddonData\stale.txt") -Value "old data" -Encoding UTF8
	Set-Content -LiteralPath (Join-Path $cliPrimaryAddons "addonB\manifest.ini") -Value "name = addonB" -Encoding UTF8
	Set-Content -LiteralPath (Join-Path $cliPrimary "userConfig\sonata\voice.txt") -Value "cli voice" -Encoding UTF8
	Set-Content -LiteralPath (Join-Path $componentPrimary "gestures.ini") -Value "component gestures" -Encoding UTF8
	Set-Content -LiteralPath (Join-Path $addonInstallSource "folderAddon\manifest.ini") -Value "name = folderAddon`nsummary = Folder Add-on`nversion = 1.0`nauthor = Tester" -Encoding UTF8
	Set-Content -LiteralPath (Join-Path $addonInstallSource "folderAddon\globalPlugin.py") -Value "plugin" -Encoding UTF8
	Set-Content -LiteralPath (Join-Path $addonInstallSource "folderAddon\__pycache__\globalPlugin.pyc") -Value "cache" -Encoding UTF8
	Set-Content -LiteralPath (Join-Path $addonInstallSource "archiveAddonBuild\manifest.ini") -Value "name = archiveAddon`nsummary = Archive Add-on`nversion = 1.0" -Encoding UTF8
	Set-Content -LiteralPath (Join-Path $addonInstallSource "archiveAddonBuild\addon.py") -Value "archive" -Encoding UTF8
	[IO.File]::WriteAllText((Join-Path $iniSource "nvda.ini"), "schemaVersion = 10`r`n[keep]`r`na = 1`r`n[parent]`r`n[[child]]`r`nvalue = yes`r`n[deleteMe]`r`nold = yes`r`n[moveMe]`r`nsource = yes`r`n[freshMove]`r`nz = 9`r`n", [Text.UTF8Encoding]::new($false))
	[IO.File]::WriteAllText((Join-Path $iniDestination "nvda.ini"), "[moveMe]`ndestination = old`n[destKeep]`nb = 2`n", [Text.UTF8Encoding]::new($false))
	[IO.File]::WriteAllText((Join-Path $gestureSource "gestures.ini"), "[globalPlugins.test.GlobalPlugin]`r`nscript_read = kb:NVDA+r`r`n[globalPlugins.unbound.GlobalPlugin]`r`nNone = kb:h`r`n[globalPlugins.multi.GlobalPlugin]`r`nscript_jump = kb:a, kb:b`r`n[deleteGesture]`r`nold = yes`r`n[moveGesture]`r`nsource = yes`r`n[freshGesture]`r`nz = 9`r`n", [Text.UTF8Encoding]::new($false))
	[IO.File]::WriteAllText((Join-Path $gestureDestination "gestures.ini"), "[moveGesture]`ndestination = old`n[destGestureKeep]`nb = 2`n", [Text.UTF8Encoding]::new($false))
	$archiveZip = Join-Path $addonInstallSource "archiveAddon.zip"
	Compress-Archive -Path (Join-Path $addonInstallSource "archiveAddonBuild\*") -DestinationPath $archiveZip -Force
	Move-Item -LiteralPath $archiveZip -Destination (Join-Path $addonInstallSource "archiveAddon.nvda-addon") -Force
	Remove-Item -LiteralPath (Join-Path $addonInstallSource "archiveAddonBuild") -Recurse -Force
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
		var profileResult = engine.Sync(args[0], new[] { args[1] }, new SyncOptions { DeleteStaleItems = true, Components = new System.Collections.Generic.List<SyncComponent> { SyncComponent.ConfigProfiles } });
		if (profileResult.Errors != 0 || !System.IO.File.Exists(System.IO.Path.Combine(args[1], "profiles", "Work.ini")) || !System.IO.File.Exists(System.IO.Path.Combine(args[1], "profileTriggers.ini")))
		{
			Console.WriteLine("profile-trigger-sync-failed errors=" + profileResult.Errors);
			return 27;
		}
		System.IO.File.Delete(System.IO.Path.Combine(args[0], "profileTriggers.ini"));
		var profileStaleResult = engine.Sync(args[0], new[] { args[1] }, new SyncOptions { DeleteStaleItems = true, Components = new System.Collections.Generic.List<SyncComponent> { SyncComponent.ConfigProfiles } });
		if (profileStaleResult.Errors != 0 || System.IO.File.Exists(System.IO.Path.Combine(args[1], "profileTriggers.ini")))
		{
			Console.WriteLine("profile-trigger-stale-delete-failed errors=" + profileStaleResult.Errors);
			return 28;
		}
		Console.WriteLine("profile-trigger-sync-ok");
		AddonPackService.ExportPackFile(args[0], args[7]);
		if (!System.IO.File.Exists(args[7]) || System.IO.File.ReadAllText(args[7]).IndexOf("addonA", StringComparison.OrdinalIgnoreCase) < 0)
		{
			Console.WriteLine("addon-pack-export-failed");
			return 8;
		}
		Console.WriteLine("addon-pack-export-ok");
		var installResult = AddonPackService.InstallFromDirectory(args[8], new[] { args[9] }, true, System.Threading.CancellationToken.None);
		if (installResult.Errors != 0 || installResult.AddonsInstalled != 2 || installResult.TargetsUpdated != 1)
		{
			Console.WriteLine("addon-install-failed errors=" + installResult.Errors + " installed=" + installResult.AddonsInstalled + " targets=" + installResult.TargetsUpdated);
			return 9;
		}
		Console.WriteLine("addon-install-ok");
		var sourceIni = System.IO.Path.Combine(args[10], "nvda.ini");
		var destinationIni = System.IO.Path.Combine(args[11], "nvda.ini");
		var freshDestinationIni = System.IO.Path.Combine(args[12], "nvda.ini");
		var parsed = IniSectionService.ParseFile(sourceIni);
		if (parsed.PreambleLines.Count != 1 || parsed.Sections.Count != 5)
		{
			Console.WriteLine("ini-parse-count-failed preamble=" + parsed.PreambleLines.Count + " sections=" + parsed.Sections.Count);
			return 10;
		}
		var parent = parsed.Sections.Find(section => section.Name == "parent");
		if (parent == null || !parent.RawLines.Contains("[[child]]"))
		{
			Console.WriteLine("ini-nested-section-failed");
			return 11;
		}
		IniSectionService.DeleteSections(sourceIni, new[] { "deleteMe", "parent" });
		var afterDelete = System.IO.File.ReadAllText(sourceIni);
		if (afterDelete.IndexOf("[deleteMe]", StringComparison.Ordinal) >= 0 || afterDelete.IndexOf("[parent]", StringComparison.Ordinal) >= 0 || afterDelete.IndexOf("schemaVersion = 10\r\n", StringComparison.Ordinal) < 0)
		{
			Console.WriteLine("ini-delete-failed");
			return 12;
		}
		var sourceBackupCount = System.IO.Directory.GetFiles(args[10], "nvda*.ini").Length - 1;
		if (sourceBackupCount != 1)
		{
			Console.WriteLine("ini-backup-after-delete-failed count=" + sourceBackupCount);
			return 15;
		}
		IniSectionService.CopySection(sourceIni, destinationIni, "keep", true);
		var afterCopySource = System.IO.File.ReadAllText(sourceIni);
		var afterCopyDestination = System.IO.File.ReadAllText(destinationIni);
		if (afterCopySource.IndexOf("[keep]", StringComparison.Ordinal) < 0 || afterCopyDestination.IndexOf("[keep]", StringComparison.Ordinal) < 0)
		{
			Console.WriteLine("ini-copy-failed");
			return 16;
		}
		IniSectionService.MoveSection(sourceIni, destinationIni, "moveMe", true);
		var destinationText = System.IO.File.ReadAllText(destinationIni);
		var sourceAfterMove = System.IO.File.ReadAllText(sourceIni);
		if (sourceAfterMove.IndexOf("[moveMe]", StringComparison.Ordinal) >= 0 || destinationText.IndexOf("source = yes", StringComparison.Ordinal) < 0 || destinationText.IndexOf("destination = old", StringComparison.Ordinal) >= 0 || destinationText.IndexOf("[destKeep]", StringComparison.Ordinal) < 0)
		{
			Console.WriteLine("ini-move-overwrite-failed");
			return 13;
		}
		IniSectionService.MoveSection(sourceIni, freshDestinationIni, "freshMove", false);
		var freshText = System.IO.File.ReadAllText(freshDestinationIni);
		if (freshText.IndexOf("schemaVersion", StringComparison.Ordinal) >= 0 || freshText.IndexOf("[freshMove]", StringComparison.Ordinal) < 0)
		{
			Console.WriteLine("ini-fresh-move-failed");
			return 14;
		}
		var destinationBackupCount = System.IO.Directory.GetFiles(args[11], "nvda*.ini").Length - 1;
		if (destinationBackupCount == 0)
		{
			Console.WriteLine("ini-destination-backup-failed");
			return 17;
		}
		Console.WriteLine("ini-section-service-ok");
		var gestureSourceIni = System.IO.Path.Combine(args[13], "gestures.ini");
		var gestureDestinationIni = System.IO.Path.Combine(args[14], "gestures.ini");
		var gestureFreshDestinationIni = System.IO.Path.Combine(args[15], "gestures.ini");
		var gestureParsed = IniSectionService.ParseFile(gestureSourceIni);
		if (gestureParsed.PreambleLines.Count != 0 || gestureParsed.Sections.Count != 6)
		{
			Console.WriteLine("gesture-parse-count-failed preamble=" + gestureParsed.PreambleLines.Count + " sections=" + gestureParsed.Sections.Count);
			return 18;
		}
		var unbound = gestureParsed.Sections.Find(section => section.Name == "globalPlugins.unbound.GlobalPlugin");
		if (unbound == null || !unbound.RawLines.Contains("None = kb:h"))
		{
			Console.WriteLine("gesture-none-line-failed");
			return 19;
		}
		var multi = gestureParsed.Sections.Find(section => section.Name == "globalPlugins.multi.GlobalPlugin");
		if (multi == null || !multi.RawLines.Contains("script_jump = kb:a, kb:b"))
		{
			Console.WriteLine("gesture-comma-list-failed");
			return 20;
		}
		IniSectionService.DeleteSections(gestureSourceIni, new[] { "deleteGesture" });
		var gestureAfterDelete = System.IO.File.ReadAllText(gestureSourceIni);
		if (gestureAfterDelete.IndexOf("[deleteGesture]", StringComparison.Ordinal) >= 0 || gestureAfterDelete.IndexOf("None = kb:h", StringComparison.Ordinal) < 0)
		{
			Console.WriteLine("gesture-delete-failed");
			return 21;
		}
		var gestureSourceBackupCount = System.IO.Directory.GetFiles(args[13], "gestures*.ini").Length - 1;
		if (gestureSourceBackupCount != 1 || System.IO.Directory.GetFiles(args[13], "nvda*.ini").Length != 0)
		{
			Console.WriteLine("gesture-backup-after-delete-failed count=" + gestureSourceBackupCount);
			return 22;
		}
		IniSectionService.CopySection(gestureSourceIni, gestureDestinationIni, "globalPlugins.multi.GlobalPlugin", true);
		var gestureAfterCopyDestination = System.IO.File.ReadAllText(gestureDestinationIni);
		if (gestureAfterCopyDestination.IndexOf("script_jump = kb:a, kb:b", StringComparison.Ordinal) < 0)
		{
			Console.WriteLine("gesture-copy-failed");
			return 23;
		}
		IniSectionService.MoveSection(gestureSourceIni, gestureDestinationIni, "moveGesture", true);
		var gestureDestinationText = System.IO.File.ReadAllText(gestureDestinationIni);
		var gestureSourceAfterMove = System.IO.File.ReadAllText(gestureSourceIni);
		if (gestureSourceAfterMove.IndexOf("[moveGesture]", StringComparison.Ordinal) >= 0 || gestureDestinationText.IndexOf("source = yes", StringComparison.Ordinal) < 0 || gestureDestinationText.IndexOf("destination = old", StringComparison.Ordinal) >= 0 || gestureDestinationText.IndexOf("[destGestureKeep]", StringComparison.Ordinal) < 0)
		{
			Console.WriteLine("gesture-move-overwrite-failed");
			return 24;
		}
		var gestureDestinationBackupCount = System.IO.Directory.GetFiles(args[14], "gestures*.ini").Length - 1;
		if (gestureDestinationBackupCount == 0 || System.IO.Directory.GetFiles(args[14], "nvda*.ini").Length != 0)
		{
			Console.WriteLine("gesture-destination-backup-failed count=" + gestureDestinationBackupCount);
			return 26;
		}
		IniSectionService.MoveSection(gestureSourceIni, gestureFreshDestinationIni, "freshGesture", false);
		var gestureFreshText = System.IO.File.ReadAllText(gestureFreshDestinationIni);
		if (gestureFreshText.IndexOf("[freshGesture]", StringComparison.Ordinal) < 0 || gestureFreshText.IndexOf("[globalPlugins.test.GlobalPlugin]", StringComparison.Ordinal) >= 0)
		{
			Console.WriteLine("gesture-fresh-move-failed");
			return 25;
		}
		Console.WriteLine("gesture-section-service-ok");
		return result.Errors == 0 ? 0 : 1;
	}
}
"@ | Set-Content -LiteralPath $harness -Encoding UTF8

	$compiler = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
	if (-not (Test-Path $compiler)) {
		$compiler = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe"
	}
	$testExe = Join-Path $tempRoot "Harness.exe"
	& $compiler /nologo /target:exe /out:$testExe /reference:System.dll /reference:System.Core.dll /reference:System.Runtime.Serialization.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll (Join-Path $root "src\AppSettings.cs") (Join-Path $root "src\SecondaryFolderProfile.cs") (Join-Path $root "src\SyncEngine.cs") (Join-Path $root "src\PortableNvdaUpdateService.cs") (Join-Path $root "src\AddonPackService.cs") (Join-Path $root "src\NvdaIniSectionService.cs") $harness
	if ($LASTEXITCODE -ne 0) {
		throw "Harness build failed."
	}
	& $testExe $primary $secondary $nestedSecondary $portableNvda $missingSecondary $componentSecondaryAddons $componentPrimary $packExport $addonInstallSource $addonInstallTarget $iniSource $iniDestination $iniFreshDestination $gestureSource $gestureDestination $gestureFreshDestination
	if ($LASTEXITCODE -ne 0) {
		throw "Harness failed."
	}
	if (-not (Test-Path -LiteralPath (Join-Path $secondary "addons\addonA\manifest.ini"))) {
		throw "Expected copied file was missing."
	}
	$sourceManifest = Get-Item -LiteralPath (Join-Path $primary "addons\addonA\manifest.ini")
	$targetManifest = Get-Item -LiteralPath (Join-Path $secondary "addons\addonA\manifest.ini")
	if ([Math]::Abs(($sourceManifest.LastWriteTimeUtc - $targetManifest.LastWriteTimeUtc).TotalSeconds) -gt 2) {
		throw "Copied file timestamp was not preserved."
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
	if (-not (Test-Path -LiteralPath (Join-Path $addonInstallTarget "userConfig\addons\folderAddon\manifest.ini"))) {
		throw "Local folder add-on was not installed."
	}
	if (-not (Test-Path -LiteralPath (Join-Path $addonInstallTarget "userConfig\addons\archiveAddon\manifest.ini"))) {
		throw "Archive add-on was not installed."
	}
	if (Test-Path -LiteralPath (Join-Path $addonInstallTarget "userConfig\addons\folderAddon\__pycache__\globalPlugin.pyc")) {
		throw "Local add-on install should exclude Python cache files."
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
		'<h2 id="secondary-properties">Secondary Folder Properties</h2>',
		'<h2 id="ini-section-cleanup">INI Section Cleanup</h2>',
		'<h2 id="addon-packs">Add-on Packs and Local Installs</h2>',
		'<h2 id="command-line">Command Line</h2>',
		'<h2 id="changelog">Changelog</h2>',
		'<h3>1.3.5</h3>',
		'<h3>1.3.4</h3>',
		'<h3>1.3.3</h3>',
		'<h3>1.3.2</h3>',
		'<h3>1.3.1</h3>',
		'<h3>1.3.0</h3>',
		'<h3>1.2.0</h3>',
		'<h3>1.1.0</h3>',
		'<h3>1.0.0</h3>',
		'<h2 id="credits">Credits</h2>'
	)) {
		if ($manual -notlike "*$requiredManualText*") {
			throw "Manual.html missing expected section: $requiredManualText"
		}
	}

	$builtExe = Join-Path $root "portable\NVDASync.exe"
	if (-not (Test-Path -LiteralPath (Join-Path $root "portable\NvdaAddonSync.exe"))) {
		throw "Legacy updater compatibility executable missing from portable output."
	}
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
	$cliPack = Join-Path $tempRoot "cli-pack.json"
	$cliPackRun = Start-Process -FilePath $builtExe -ArgumentList @(
		"--primary", $primary,
		"--export-addon-pack", $cliPack
	) -Wait -PassThru
	if ($cliPackRun.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $cliPack)) {
		throw "Command-line add-on pack export failed."
	}
	$cliInstallTarget = Join-Path $tempRoot "cliInstallTarget"
	New-Item -ItemType Directory -Path $cliInstallTarget -Force | Out-Null
	$cliInstallRun = Start-Process -FilePath $builtExe -ArgumentList @(
		"--install-addons", $addonInstallSource,
		"--secondary", $cliInstallTarget,
		"--exclude-python-cache"
	) -Wait -PassThru
	if ($cliInstallRun.ExitCode -ne 0) {
		throw "Command-line add-on install failed with exit code $($cliInstallRun.ExitCode)."
	}
	if (-not (Test-Path -LiteralPath (Join-Path $cliInstallTarget "userConfig\addons\folderAddon\manifest.ini"))) {
		throw "Command-line add-on install did not copy folderAddon."
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
	$archiveCreated = $false
	for ($archiveAttempt = 1; $archiveAttempt -le 5; $archiveAttempt++) {
		try {
			Compress-Archive -Path (Join-Path $root "portable\*") -DestinationPath $updateZip -Force
			$archiveCreated = $true
			break
		}
		catch {
			if ($archiveAttempt -eq 5) { throw }
			Start-Sleep -Milliseconds 500
		}
	}
	if (-not $archiveCreated) { throw "Could not create update ZIP." }
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
	if (Test-Path -LiteralPath (Join-Path $updateTarget "NvdaAddonSync.exe")) {
		throw "Local updater did not remove legacy NvdaAddonSync.exe."
	}
	if (-not (Test-Path -LiteralPath (Join-Path $updateTarget "NVDASync.exe"))) {
		throw "Local updater did not install NVDASync.exe."
	}

	$guiProcess = Start-Process -FilePath $builtExe -PassThru
	Start-Sleep -Seconds 2
	$runningSync = Start-Process -FilePath $builtExe -ArgumentList @("--sync-running") -Wait -PassThru
	if ($runningSync.ExitCode -ne 0) {
		throw "--sync-running did not signal the running app."
	}
	$takeoverFolder = Join-Path $tempRoot "takeoverPortable"
	New-Item -ItemType Directory -Path $takeoverFolder -Force | Out-Null
	Copy-Item -LiteralPath (Join-Path $root "portable\NVDASync.exe") -Destination (Join-Path $takeoverFolder "NVDASync.exe") -Force
	Copy-Item -LiteralPath (Join-Path $root "portable\Manual.html") -Destination (Join-Path $takeoverFolder "Manual.html") -Force
	Copy-Item -LiteralPath (Join-Path $root "portable\LICENSE.txt") -Destination (Join-Path $takeoverFolder "LICENSE.txt") -Force
	$takeoverExe = Join-Path $takeoverFolder "NVDASync.exe"
	$takeoverProcess = Start-Process -FilePath $takeoverExe -PassThru
	if (-not $guiProcess.WaitForExit(10000)) {
		$guiProcess.Kill()
		throw "Different-folder launch did not close the previous running instance."
	}
	Start-Sleep -Seconds 2
	if ($takeoverProcess.HasExited) {
		throw "Different-folder takeover instance exited unexpectedly."
	}
	$closeRun = Start-Process -FilePath $takeoverExe -ArgumentList @("--close") -Wait -PassThru
	if ($closeRun.ExitCode -ne 0) {
		throw "--close did not signal the takeover app."
	}
	if (-not $takeoverProcess.WaitForExit(10000)) {
		$takeoverProcess.Kill()
		throw "Takeover app did not close after --close."
	}
	if (-not (Test-Path -LiteralPath (Join-Path $root "portable\Logs\NVDASync.log"))) {
		throw "GUI run did not create Logs\NVDASync.log."
	}
	foreach ($runtimeFolder in @("Logs", "Settings")) {
		$runtimePath = Join-Path $root ("portable\" + $runtimeFolder)
		Remove-TreeWithRetry $runtimePath
	}
	Write-Host "Smoke test passed."
}
finally {
	Remove-NotifyIconEntriesForPathPrefix $tempRoot
	Remove-NotifyIconEntriesForPathPrefix (Join-Path $root "portable")
	if (Test-Path -LiteralPath $tempRoot) {
		Remove-TreeWithRetry $tempRoot
	}
}
