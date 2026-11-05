[CmdletBinding()]
param(
	[Parameter(Position = 0)]
	[string]$GameDir = '',
	[string]$SourceDir = '',
	[string]$ConfigPath = '',
	[switch]$Uninstall,
	[switch]$ResolveOnly,
	[switch]$NonInteractive,
	[switch]$ElevatedRetry,
	[Parameter(ValueFromRemainingArguments = $true)]
	[string[]]$ExtraArgs = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

try {
	[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
	[Console]::InputEncoding = [System.Text.Encoding]::UTF8
} catch {
}

function ConvertFrom-UnicodeEscape {
	param([string]$Text)
	return [System.Text.RegularExpressions.Regex]::Unescape($Text)
}

$Msg = @{
	BannerInstall = ConvertFrom-UnicodeEscape (
		'[Sephiria Optimizer v2.1] ' +
		'\uC138\uD53C\uB9AC\uC544 \uCD5C\uC801\uD654 \uBAA8\uB4DC \uD1B5\uD569 \uC790\uB3D9 \uC124\uCE58\uAE30'
	)
	BannerUninstall = ConvertFrom-UnicodeEscape (
		'[Sephiria Optimizer v2.1] ' +
		'\uC138\uD53C\uB9AC\uC544 \uCD5C\uC801\uD654 \uBAA8\uB4DC \uD1B5\uD569 \uC790\uB3D9 \uC81C\uAC70\uAE30'
	)
	ErrZipTemp = ConvertFrom-UnicodeEscape (
		'\uC555\uCD95 \uD30C\uC77C(.zip) \uB0B4\uBD80\uC5D0\uC11C \uBC14\uB85C \uC2E4\uD589\uB418\uC5B4 ' +
		'SephiriaOptimizer.dll \uD30C\uC77C\uC744 \uCC3E\uC744 \uC218 \uC5C6\uC2B5\uB2C8\uB2E4.\n\n' +
		'\uBA3C\uC800 ZIP \uD30C\uC77C\uC744 \uB9C8\uC6B0\uC2A4 \uC6B0\uD074\uB9AD -> ' +
		'[\uBAA8\uB450 \uC555\uCD95 \uD480\uAE30]\uB85C \uC555\uCD95 \uD574\uC81C\uD55C \uB4A4 ' +
		'Install.bat(Windows) \uB610\uB294 install.sh(Linux/macOS)\uB97C ' +
		'\uC2E4\uD589\uD574 \uC8FC\uC138\uC694.'
	)
	ErrMissingPayload = ConvertFrom-UnicodeEscape (
		'\uC124\uCE58\uD560 \uBAA8\uB4DC \uD30C\uC77C(SephiriaOptimizer.dll, metadata.json)\uC744 ' +
		'\uCC3E\uC744 \uC218 \uC5C6\uC2B5\uB2C8\uB2E4.\n' +
		'\uC18C\uC2A4 \uC800\uC7A5\uC18C\uC5D0\uC11C \uC2E4\uD589 \uC911\uC774\uB77C\uBA74 \uBA3C\uC800 ' +
		'dotnet build -c Release \uB97C \uC2E4\uD589\uD574 \uC8FC\uC138\uC694.'
	)
	TitleError = ConvertFrom-UnicodeEscape 'Sephiria Optimizer \uC624\uB958'
	TitleCancel = ConvertFrom-UnicodeEscape 'Sephiria Optimizer \uCDE8\uC18C'
	TitleDone = ConvertFrom-UnicodeEscape 'Sephiria Optimizer \uC644\uB8CC'
	TitleLocked = ConvertFrom-UnicodeEscape (
		'Sephiria Optimizer \uD30C\uC77C \uC7A0\uAE08 \uAC10\uC9C0'
	)
	PickerDesc = ConvertFrom-UnicodeEscape (
		'\uC138\uD53C\uB9AC\uC544(Sephiria.exe \uB610\uB294 Sephiria.app)\uAC00 ' +
		'\uC124\uCE58\uB41C \uAC8C\uC784 \uD3F4\uB354\uB97C \uC120\uD0DD\uD574 \uC8FC\uC138\uC694.'
	)
	PromptConsolePath = ConvertFrom-UnicodeEscape (
		'\uC790\uB3D9 \uD0D0\uC0C9\uC73C\uB85C \uC138\uD53C\uB9AC\uC544 \uD3F4\uB354\uB97C ' +
		'\uCC3E\uC9C0 \uBABB\uD588\uC2B5\uB2C8\uB2E4. ' +
		'\uAC8C\uC784 \uC124\uCE58 \uACBD\uB85C\uB97C \uC785\uB825\uD558\uC138\uC694 (\uCDE8\uC18C: Enter)'
	)
	ErrInvalidDir = ConvertFrom-UnicodeEscape (
		'\uC720\uD6A8\uD55C \uC138\uD53C\uB9AC\uC544 \uC124\uCE58 \uD3F4\uB354' +
		'(Sephiria_Data/Managed/Assembly-CSharp.dll)\uB97C \uD655\uC778\uD558\uC9C0 \uBABB\uD588\uC2B5\uB2C8\uB2E4.'
	)
	ErrOutdatedGame = ConvertFrom-UnicodeEscape (
		'\uC124\uCE58\uB41C \uC138\uD53C\uB9AC\uC544 \uBC84\uC804\uC5D0 \uACF5\uC2DD \uB0B4\uC7A5 ' +
		'AddOnLoader(HorayModBase)\uAC00 \uD3EC\uD568\uB418\uC5B4 \uC788\uC9C0 \uC54A\uC2B5\uB2C8\uB2E4.\n' +
		'Steam\uC5D0\uC11C \uAC8C\uC784\uC744 \uCD5C\uC2E0 \uBC84\uC804\uC73C\uB85C ' +
		'\uC5C5\uB370\uC774\uD2B8\uD558\uAC70\uB098 ' +
		'\uD30C\uC77C \uBB34\uACB0\uC131 \uAC80\uC0AC\uB97C \uC2E4\uD589\uD574 \uC8FC\uC138\uC694.'
	)
	WarnStaleCache = ConvertFrom-UnicodeEscape (
		'[\uC548\uB0B4] \uC774\uC804\uC5D0 \uC800\uC7A5\uB41C \uACBD\uB85C\uAC00 ' +
		'\uB354 \uC774\uC0C1 \uC720\uD6A8\uD558\uC9C0 \uC54A\uC544 ' +
		'\uC790\uB3D9 \uC7AC\uD0D0\uC0C9\uC744 \uC9C4\uD589\uD569\uB2C8\uB2E4: '
	)
	InfoDetected = ConvertFrom-UnicodeEscape '[\uAC10\uC9C0\uB41C \uAC8C\uC784 \uACBD\uB85C] '
	InfoSavedCache = ConvertFrom-UnicodeEscape '[\uACBD\uB85C \uAE30\uC5B5 \uC644\uB8CC] '
	ErrLocked = ConvertFrom-UnicodeEscape (
		'\uC138\uD53C\uB9AC\uC544(Sephiria) \uAC8C\uC784\uC774 \uD604\uC7AC \uC2E4\uD589 \uC911\uC774\uAC70\uB098 ' +
		'\uBAA8\uB4DC DLL \uD30C\uC77C\uC774 \uC7A0\uACA8 \uC788\uC2B5\uB2C8\uB2E4.\n' +
		'\uAC8C\uC784\uC744 \uC644\uC804\uD788 \uC885\uB8CC\uD55C \uB4A4 ' +
		'\uB2E4\uC2DC \uC2DC\uB3C4\uD574 \uC8FC\uC138\uC694.'
	)
	PromptLockConsole = ConvertFrom-UnicodeEscape (
		'\uAC8C\uC784 \uC885\uB8CC \uD6C4 [R]\uC744 \uC785\uB825\uD558\uBA74 ' +
		'\uC7AC\uC2DC\uB3C4\uD569\uB2C8\uB2E4 (\uCDE8\uC18C: Q)'
	)
	InfoElevating = ConvertFrom-UnicodeEscape (
		'[\uAD8C\uD55C \uC2B9\uACA9] \uBCF4\uD638\uB41C \uD3F4\uB354(Program Files \uB4F1) ' +
		'\uC4F0\uAE30\uB97C \uC704\uD574 \uAD00\uB9AC\uC790 \uAD8C\uD55C(UAC)\uC744 \uC694\uCCAD\uD569\uB2C8\uB2E4...'
	)
	ErrPermission = ConvertFrom-UnicodeEscape (
		'\uB300\uC0C1 \uD3F4\uB354\uC5D0 \uD30C\uC77C\uC744 \uC4F8 \uAD8C\uD55C\uC774 \uC5C6\uC2B5\uB2C8\uB2E4. ' +
		'\uAD00\uB9AC\uC790 \uAD8C\uD55C(Windows: \uC6B0\uD074\uB9AD -> ' +
		'\uAD00\uB9AC\uC790 \uAD8C\uD55C\uC73C\uB85C \uC2E4\uD589 / Linux, macOS: sudo)\uC73C\uB85C ' +
		'\uB2E4\uC2DC \uC2E4\uD589\uD574 \uC8FC\uC138\uC694.\n\uB300\uC0C1 \uACBD\uB85C: '
	)
	DoneInstall = ConvertFrom-UnicodeEscape (
		'\uC138\uD53C\uB9AC\uC544 \uCD5C\uC801\uD654 \uBAA8\uB4DC(SephiriaOptimizer v2.1.0) ' +
		'\uC124\uCE58\uAC00 \uC644\uB8CC\uB418\uC5C8\uC2B5\uB2C8\uB2E4!\n\n' +
		'\uC124\uCE58 \uACBD\uB85C: {0}\n\n' +
		'\uAC8C\uC784 \uC2E4\uD589 \uD6C4 \uC778\uBCA4\uD1A0\uB9AC(I \uB610\uB294 Tab)\uB97C \uC5F4\uBA74 ' +
		'\uC6B0\uCE21 \uCEE8\uD2B8\uB864 \uD328\uB110\uC774 \uD45C\uC2DC\uB429\uB2C8\uB2E4.\n' +
		'- [\uC790\uB3D9 \uC815\uB82C \uC2E4\uD589] \uBC84\uD2BC \uD074\uB9AD\uC73C\uB85C \uC989\uC2DC \uCD5C\uC801 \uC7AC\uBC30\uCE58\n' +
		'- [\uC608\uC0C1 DPS] \uC601\uC5ED \uB9C8\uC6B0\uC2A4 \uC624\uBC84\uB85C DPS \uC0C1\uC138 \uBD84\uC11D \uD234\uD301 \uD655\uC778\n' +
		'- [\uBE4C\uB4DC \uC124\uC815] \uBC84\uD2BC\uC73C\uB85C \uC6B0\uC120\uC21C\uC704 / \uBAA9\uD45C \uCF64\uBCF4 / ' +
		'\uAE08\uBE5B\uCE68 \uC124\uC815'
	)
	DoneUninstall = ConvertFrom-UnicodeEscape (
		'SephiriaOptimizer \uBAA8\uB4DC\uAC00 \uAE54\uB054\uD558\uAC8C ' +
		'\uC81C\uAC70\uB418\uC5C8\uC2B5\uB2C8\uB2E4.\n\n\uC81C\uAC70\uB41C \uACBD\uB85C: {0}'
	)
	InfoAlreadyRemoved = ConvertFrom-UnicodeEscape (
		'\uD574\uB2F9 \uAC8C\uC784 \uD3F4\uB354\uC5D0 \uC124\uCE58\uB41C SephiriaOptimizer ' +
		'\uBAA8\uB4DC\uAC00 \uC5C6\uC2B5\uB2C8\uB2E4.\n\n\uD655\uC778\uD55C \uACBD\uB85C: {0}'
	)
}

for ($idx = 0; $idx -lt $ExtraArgs.Count; $idx++) {
	$token = $ExtraArgs[$idx]
	if ($token -eq '--uninstall' -or $token -eq '-u') {
		$Uninstall = $true
	} elseif ($token -eq '--non-interactive' -or $token -eq '-y') {
		$NonInteractive = $true
	} elseif ($token -eq '--resolve-only') {
		$ResolveOnly = $true
	} elseif (($token -eq '--game-dir' -or $token -eq '-g') -and ($idx + 1 -lt $ExtraArgs.Count)) {
		$idx++
		$GameDir = $ExtraArgs[$idx]
	} elseif (($token -eq '--config-path') -and ($idx + 1 -lt $ExtraArgs.Count)) {
		$idx++
		$ConfigPath = $ExtraArgs[$idx]
	} elseif ([string]::IsNullOrWhiteSpace($GameDir) -and -not $token.StartsWith('-')) {
		$GameDir = $token
	}
}

$HasExplicitConfigOverride = -not [string]::IsNullOrWhiteSpace($ConfigPath)

$IsWinPlatform = [System.Environment]::OSVersion.Platform -eq [System.PlatformID]::Win32NT
$IsMacPlatform = $false
if (-not $IsWinPlatform) {
	try {
		$IsMacPlatform = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
			[System.Runtime.InteropServices.OSPlatform]::OSX
		)
	} catch {
		$IsMacPlatform = (Test-Path -LiteralPath '/System/Library/CoreServices')
	}
}

$CanUseWinForms = $false
if ($IsWinPlatform -and -not $NonInteractive -and -not $ResolveOnly -and [System.Environment]::UserInteractive) {
	try {
		Add-Type -AssemblyName System.Windows.Forms -ErrorAction Stop
		$CanUseWinForms = $true
	} catch {
		$CanUseWinForms = $false
	}
}

function Show-UserNotice {
	param(
		[string]$Message,
		[string]$Title,
		[string]$Level = 'Information'
	)
	$color = 'Cyan'
	if ($Level -eq 'Error') {
		$color = 'Red'
	} elseif ($Level -eq 'Warning') {
		$color = 'Yellow'
	} elseif ($Level -eq 'Success') {
		$color = 'Green'
	}
	if (-not $ResolveOnly) {
		Write-Host $Message -ForegroundColor $color
	}
	if ($CanUseWinForms) {
		$icon = 'Information'
		if ($Level -eq 'Error') {
			$icon = 'Error'
		} elseif ($Level -eq 'Warning') {
			$icon = 'Warning'
		}
		[System.Windows.Forms.MessageBox]::Show($Message, $Title, 'OK', $icon) | Out-Null
	}
}

function Get-ScriptDirectory {
	if (-not [string]::IsNullOrWhiteSpace($PSScriptRoot)) {
		return $PSScriptRoot
	}
	if (-not [string]::IsNullOrWhiteSpace($PSCommandPath)) {
		return [System.IO.Path]::GetDirectoryName($PSCommandPath)
	}
	return (Get-Location).Path
}

function Get-DefaultUserConfigPath {
	if ($HasExplicitConfigOverride) {
		return (Normalize-PathInput -RawPath $ConfigPath)
	}
	if ($IsWinPlatform) {
		$baseDir = $env:LOCALAPPDATA
		if ([string]::IsNullOrWhiteSpace($baseDir)) {
			$baseDir = [System.Environment]::GetFolderPath(
				[System.Environment+SpecialFolder]::LocalApplicationData
			)
		}
		if ([string]::IsNullOrWhiteSpace($baseDir)) {
			$baseDir = Join-Path $HOME 'AppData\Local'
		}
		return Join-Path (Join-Path $baseDir 'SephiriaOptimizer') 'game_path.txt'
	}
	if ($IsMacPlatform) {
		$macSupport = Join-Path (Join-Path $HOME 'Library') 'Application Support'
		return Join-Path (Join-Path $macSupport 'SephiriaOptimizer') 'game_path.txt'
	}
	$xdg = $env:XDG_CONFIG_HOME
	if ([string]::IsNullOrWhiteSpace($xdg)) {
		$xdg = Join-Path $HOME '.config'
	}
	return Join-Path (Join-Path $xdg 'SephiriaOptimizer') 'game_path.txt'
}

function Get-RepoLocalCachePath {
	if ($HasExplicitConfigOverride) {
		return ''
	}
	$scriptDir = Get-ScriptDirectory
	$parentDir = [System.IO.Path]::GetFullPath((Join-Path $scriptDir '..'))
	$slnCandidate = Join-Path $parentDir 'SephiriaOptimizer.sln'
	if ([System.IO.File]::Exists($slnCandidate)) {
		return Join-Path $parentDir '.sephiria-path'
	}
	$cwdSln = Join-Path (Get-Location).Path 'SephiriaOptimizer.sln'
	if ([System.IO.File]::Exists($cwdSln)) {
		return Join-Path (Get-Location).Path '.sephiria-path'
	}
	return ''
}

function Normalize-PathInput {
	param([string]$RawPath)
	if ([string]::IsNullOrWhiteSpace($RawPath)) {
		return ''
	}
	$trimmed = $RawPath.Trim()
	$hasDoubleQuote = $trimmed.StartsWith('"') -and $trimmed.EndsWith('"')
	$hasSingleQuote = $trimmed.StartsWith("'") -and $trimmed.EndsWith("'")
	if (($hasDoubleQuote -or $hasSingleQuote) -and $trimmed.Length -ge 2) {
		$trimmed = $trimmed.Substring(1, $trimmed.Length - 2).Trim()
	}
	if ([string]::IsNullOrWhiteSpace($trimmed)) {
		return ''
	}
	$sep = [System.IO.Path]::DirectorySeparatorChar
	$altSep = [System.IO.Path]::AltDirectorySeparatorChar
	$normalized = $trimmed.Replace($altSep, $sep).Replace([char]'/', $sep).Replace([char]'\', $sep)
	try {
		$full = [System.IO.Path]::GetFullPath($normalized)
		$rootPath = [System.IO.Path]::GetPathRoot($full)
		if ($full.Length -gt $rootPath.Length) {
			$full = $full.TrimEnd([char]'\', [char]'/')
		}
		return $full
	} catch {
		return ''
	}
}

function Resolve-SephiriaLayout {
	param([string]$CandidatePath)
	$full = Normalize-PathInput -RawPath $CandidatePath
	if ([string]::IsNullOrWhiteSpace($full)) {
		return $null
	}

	$startDir = $full
	if ([System.IO.File]::Exists($full)) {
		$startDir = [System.IO.Path]::GetDirectoryName($full)
	}
	if ([string]::IsNullOrWhiteSpace($startDir) -or -not [System.IO.Directory]::Exists($startDir)) {
		return $null
	}

	$probes = New-Object System.Collections.Generic.List[string]
	$probes.Add($startDir)
	$probes.Add((Join-Path $startDir 'Sephiria'))
	$probes.Add((Join-Path (Join-Path $startDir 'common') 'Sephiria'))
	$probes.Add((Join-Path (Join-Path (Join-Path $startDir 'steamapps') 'common') 'Sephiria'))

	$cursor = $startDir
	for ($depth = 0; $depth -lt 4; $depth++) {
		$parent = [System.IO.Directory]::GetParent($cursor)
		if ($null -eq $parent) {
			break
		}
		$cursor = $parent.FullName
		$probes.Add($cursor)
	}

	$seen = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
	foreach ($rawProbe in $probes) {
		$probe = Normalize-PathInput -RawPath $rawProbe
		if ([string]::IsNullOrWhiteSpace($probe) -or -not $seen.Add($probe)) {
			continue
		}
		if (-not [System.IO.Directory]::Exists($probe)) {
			continue
		}

		$winDataDir = Join-Path $probe 'Sephiria_Data'
		$winManagedDir = Join-Path $winDataDir 'Managed'
		$winAssembly = Join-Path $winManagedDir 'Assembly-CSharp.dll'
		if ([System.IO.File]::Exists($winAssembly)) {
			$hasRuntimeBinary = [System.IO.File]::Exists((Join-Path $probe 'Sephiria.exe')) `
				-or [System.IO.File]::Exists((Join-Path $probe 'Sephiria.x86_64')) `
				-or [System.IO.File]::Exists((Join-Path $probe 'UnityPlayer.dll')) `
				-or [System.IO.File]::Exists((Join-Path $winManagedDir 'UnityEngine.CoreModule.dll'))
			if ($hasRuntimeBinary) {
				$addOnsDir = Normalize-PathInput -RawPath (Join-Path $winDataDir (Join-Path '..' 'AddOns'))
				return [PSCustomObject]@{
					GameRoot = $probe
					DataDir = $winDataDir
					ManagedDir = $winManagedDir
					AddOnsDir = $addOnsDir
					ModDir = Join-Path $addOnsDir 'SephiriaOptimizer'
				}
			}
		}

		$appBundle = $probe
		if (-not $appBundle.EndsWith('.app', [System.StringComparison]::OrdinalIgnoreCase)) {
			$appBundle = Join-Path $probe 'Sephiria.app'
		}
		$macContents = Join-Path $appBundle 'Contents'
		$macManaged = Join-Path (Join-Path (Join-Path $macContents 'Resources') 'Data') 'Managed'
		$macAssembly = Join-Path $macManaged 'Assembly-CSharp.dll'
		if ([System.IO.File]::Exists($macAssembly)) {
			$gameRoot = Normalize-PathInput -RawPath ([System.IO.Directory]::GetParent($appBundle).FullName)
			$macAddOns = Normalize-PathInput -RawPath (Join-Path $macContents (Join-Path '..' 'AddOns'))
			return [PSCustomObject]@{
				GameRoot = $gameRoot
				DataDir = $macContents
				ManagedDir = $macManaged
				AddOnsDir = $macAddOns
				ModDir = Join-Path $macAddOns 'SephiriaOptimizer'
			}
		}
	}

	return $null
}

function Test-AddOnLoaderSupport {
	param([string]$ManagedDir)
	$assemblyPath = Join-Path $ManagedDir 'Assembly-CSharp.dll'
	$coreModulePath = Join-Path $ManagedDir 'UnityEngine.CoreModule.dll'
	if (-not [System.IO.File]::Exists($assemblyPath) -or -not [System.IO.File]::Exists($coreModulePath)) {
		return $false
	}
	try {
		$stream = [System.IO.File]::Open(
			$assemblyPath,
			[System.IO.FileMode]::Open,
			[System.IO.FileAccess]::Read,
			[System.IO.FileShare]::ReadWrite
		)
		try {
			$bytes = New-Object byte[] ($stream.Length)
			$offset = 0
			while ($offset -lt $bytes.Length) {
				$read = $stream.Read($bytes, $offset, $bytes.Length - $offset)
				if ($read -le 0) {
					break
				}
				$offset += $read
			}
		} finally {
			$stream.Dispose()
		}
		$latin1 = [System.Text.Encoding]::GetEncoding('ISO-8859-1').GetString($bytes)
		return $latin1.Contains('AddOnLoader') -and $latin1.Contains('HorayModBase')
	} catch {
		return $true
	}
}

function Add-UniqueCandidate {
	param(
		[System.Collections.Generic.List[string]]$List,
		[System.Collections.Generic.HashSet[string]]$Set,
		[string]$PathValue
	)
	$norm = Normalize-PathInput -RawPath $PathValue
	if (-not [string]::IsNullOrWhiteSpace($norm) -and $Set.Add($norm)) {
		$List.Add($norm)
	}
}

function Expand-SteamRootCandidates {
	param(
		[string]$SteamRoot,
		[System.Collections.Generic.List[string]]$CandidateGameDirs,
		[System.Collections.Generic.HashSet[string]]$SeenSet
	)
	$cleanSteam = Normalize-PathInput -RawPath $SteamRoot
	if ([string]::IsNullOrWhiteSpace($cleanSteam) -or -not [System.IO.Directory]::Exists($cleanSteam)) {
		return
	}

	$libraryRoots = New-Object System.Collections.Generic.List[string]
	$libSeen = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
	if ($libSeen.Add($cleanSteam)) {
		$libraryRoots.Add($cleanSteam)
	}

	$vdfPaths = @(
		(Join-Path (Join-Path $cleanSteam 'steamapps') 'libraryfolders.vdf'),
		(Join-Path (Join-Path $cleanSteam 'config') 'libraryfolders.vdf')
	)
	foreach ($vdf in $vdfPaths) {
		if (-not [System.IO.File]::Exists($vdf)) {
			continue
		}
		try {
			$vdfText = [System.IO.File]::ReadAllText($vdf, [System.Text.Encoding]::UTF8)
			$pathMatches = [System.Text.RegularExpressions.Regex]::Matches(
				$vdfText,
				'"(?:path|\d+)"\s+"([^"]+)"',
				[System.Text.RegularExpressions.RegexOptions]::IgnoreCase
			)
			foreach ($m in $pathMatches) {
				$rawLib = $m.Groups[1].Value -replace '\\\\', '\'
				$normLib = Normalize-PathInput -RawPath $rawLib
				if (-not [string]::IsNullOrWhiteSpace($normLib) -and $libSeen.Add($normLib)) {
					$libraryRoots.Add($normLib)
				}
			}
		} catch {
		}
	}

	foreach ($libRoot in $libraryRoots) {
		$steamApps = Join-Path $libRoot 'steamapps'
		$commonSephiria = Join-Path (Join-Path $steamApps 'common') 'Sephiria'
		Add-UniqueCandidate -List $CandidateGameDirs -Set $SeenSet -PathValue $commonSephiria

		if ([System.IO.Directory]::Exists($steamApps)) {
			try {
				$manifests = [System.IO.Directory]::GetFiles($steamApps, 'appmanifest_*.acf')
				foreach ($acf in $manifests) {
					$acfText = [System.IO.File]::ReadAllText($acf, [System.Text.Encoding]::UTF8)
					if ($acfText -match '"installdir"\s+"([^"]*Sephiria[^"]*)"') {
						$customInstallDir = $Matches[1] -replace '\\\\', '\'
						$resolvedCustom = Join-Path (Join-Path $steamApps 'common') $customInstallDir
						Add-UniqueCandidate -List $CandidateGameDirs -Set $SeenSet -PathValue $resolvedCustom
					}
				}
			} catch {
			}
		}
	}
}

function Find-SephiriaLayoutAutomatically {
	$candidates = New-Object System.Collections.Generic.List[string]
	$seen = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)

	if (-not $HasExplicitConfigOverride) {
		foreach ($envVal in @($env:SEPHIRIA_GAME_DIR, $env:SEPHIRIA_PATH)) {
			if (-not [string]::IsNullOrWhiteSpace($envVal)) {
				$envLayout = Resolve-SephiriaLayout -CandidatePath $envVal
				if ($null -ne $envLayout) {
					return $envLayout
				}
			}
		}
	}

	$cacheFiles = New-Object System.Collections.Generic.List[string]
	$repoCache = Get-RepoLocalCachePath
	if (-not [string]::IsNullOrWhiteSpace($repoCache)) {
		$cacheFiles.Add($repoCache)
	}
	$cacheFiles.Add((Get-DefaultUserConfigPath))

	foreach ($cf in $cacheFiles) {
		if (-not [string]::IsNullOrWhiteSpace($cf) -and [System.IO.File]::Exists($cf)) {
			try {
				$cachedText = [System.IO.File]::ReadAllText($cf, [System.Text.Encoding]::UTF8).Trim()
				if (-not [string]::IsNullOrWhiteSpace($cachedText)) {
					$cachedLayout = Resolve-SephiriaLayout -CandidatePath $cachedText
					if ($null -ne $cachedLayout) {
						return $cachedLayout
					}
					if (-not $ResolveOnly) {
						Write-Host ($Msg.WarnStaleCache + $cachedText) -ForegroundColor Yellow
					}
				}
			} catch {
			}
		}
	}

	if ($HasExplicitConfigOverride) {
		return $null
	}

	$steamRoots = New-Object System.Collections.Generic.List[string]
	if ($IsWinPlatform) {
		$regSteamLocations = @(
			@{ Path = 'HKCU:\Software\Valve\Steam'; Name = 'SteamPath' },
			@{ Path = 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam'; Name = 'InstallPath' },
			@{ Path = 'HKLM:\SOFTWARE\Valve\Steam'; Name = 'InstallPath' }
		)
		foreach ($entry in $regSteamLocations) {
			try {
				if (Test-Path -LiteralPath $entry.Path) {
					$prop = Get-ItemProperty -LiteralPath $entry.Path -ErrorAction SilentlyContinue
					$val = $prop.($entry.Name)
					if (-not [string]::IsNullOrWhiteSpace($val)) {
						$steamRoots.Add([string]$val)
					}
				}
			} catch {
			}
		}

		$uninstallRoots = @(
			'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall',
			'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall',
			'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall'
		)
		foreach ($uRoot in $uninstallRoots) {
			try {
				if (Test-Path -LiteralPath $uRoot) {
					foreach ($subKey in (Get-ChildItem -LiteralPath $uRoot -ErrorAction SilentlyContinue)) {
						$uProp = Get-ItemProperty -LiteralPath $subKey.PSPath -ErrorAction SilentlyContinue
						if ($null -eq $uProp) {
							continue
						}
						$dispName = [string]$uProp.DisplayName
						$instLoc = [string]$uProp.InstallLocation
						if (-not [string]::IsNullOrWhiteSpace($instLoc) `
							-and ($dispName -like '*Sephiria*' -or $instLoc -like '*Sephiria*')) {
							Add-UniqueCandidate -List $candidates -Set $seen -PathValue $instLoc
						}
					}
				}
			} catch {
			}
		}

		try {
			foreach ($drive in [System.IO.DriveInfo]::GetDrives()) {
				if ($drive.DriveType -ne [System.IO.DriveType]::Fixed `
					-and $drive.DriveType -ne [System.IO.DriveType]::Removable) {
					continue
				}
				if (-not $drive.IsReady) {
					continue
				}
				$root = $drive.RootDirectory.FullName
				$steamRoots.Add((Join-Path $root 'Program Files (x86)\Steam'))
				$steamRoots.Add((Join-Path $root 'Program Files\Steam'))
				$steamRoots.Add((Join-Path $root 'Steam'))
				$steamRoots.Add((Join-Path $root 'SteamLibrary'))
				$steamRoots.Add((Join-Path (Join-Path $root 'Games') 'Steam'))
				$steamRoots.Add((Join-Path (Join-Path $root 'Games') 'SteamLibrary'))
				$gamesSephiria = Join-Path (Join-Path $root 'Games') 'Sephiria'
				Add-UniqueCandidate -List $candidates -Set $seen -PathValue $gamesSephiria
				Add-UniqueCandidate -List $candidates -Set $seen -PathValue (Join-Path $root 'Sephiria')
			}
		} catch {
		}
	} elseif ($IsMacPlatform) {
		$macSteam = Join-Path (Join-Path (Join-Path $HOME 'Library') 'Application Support') 'Steam'
		$steamRoots.Add($macSteam)
	} else {
		$steamRoots.Add((Join-Path (Join-Path (Join-Path $HOME '.local') 'share') 'Steam'))
		$steamRoots.Add((Join-Path (Join-Path $HOME '.steam') 'steam'))
		$steamRoots.Add((Join-Path (Join-Path $HOME '.steam') 'root'))
		$steamRoots.Add((Join-Path $HOME '.var/app/com.valvesoftware.Steam/.local/share/Steam'))
		$steamRoots.Add((Join-Path $HOME 'snap/steam/common/.local/share/Steam'))
		$steamRoots.Add('/run/media/mmcblk0p1')
		foreach ($mountRoot in @('/run/media', '/mnt', '/media')) {
			if ([System.IO.Directory]::Exists($mountRoot)) {
				try {
					foreach ($d1 in [System.IO.Directory]::GetDirectories($mountRoot)) {
						$steamRoots.Add($d1)
						$steamRoots.Add((Join-Path $d1 'SteamLibrary'))
						foreach ($d2 in [System.IO.Directory]::GetDirectories($d1)) {
							$steamRoots.Add($d2)
							$steamRoots.Add((Join-Path $d2 'SteamLibrary'))
						}
					}
				} catch {
				}
			}
		}
	}

	foreach ($sRoot in $steamRoots) {
		Expand-SteamRootCandidates -SteamRoot $sRoot -CandidateGameDirs $candidates -SeenSet $seen
	}

	foreach ($cand in $candidates) {
		$resolved = Resolve-SephiriaLayout -CandidatePath $cand
		if ($null -ne $resolved) {
			return $resolved
		}
	}

	return $null
}

function Select-GameDirectoryViaStaDialog {
	param([string]$Description)
	if (-not $CanUseWinForms) {
		return ''
	}
	$sharedState = [hashtable]::Synchronized(@{ SelectedPath = '' })
	$scriptBlock = {
		param($State, $Desc)
		try {
			Add-Type -AssemblyName System.Windows.Forms
			$dialog = New-Object System.Windows.Forms.FolderBrowserDialog
			$dialog.Description = $Desc
			$dialog.ShowNewFolderButton = $false
			if ($dialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
				$State.SelectedPath = $dialog.SelectedPath
			}
		} catch {
		}
	}

	$curThread = [System.Threading.Thread]::CurrentThread
	if ($curThread.GetApartmentState() -eq [System.Threading.ApartmentState]::STA) {
		& $scriptBlock $sharedState $Description
		return [string]$sharedState.SelectedPath
	}

	$runspace = [runspacefactory]::CreateRunspace()
	$runspace.ApartmentState = 'STA'
	$runspace.ThreadOptions = 'ReuseThread'
	$runspace.Open()
	try {
		$ps = [powershell]::Create()
		$ps.Runspace = $runspace
		[void]$ps.AddScript($scriptBlock).AddArgument($sharedState).AddArgument($Description)
		[void]$ps.Invoke()
		$ps.Dispose()
	} finally {
		$runspace.Close()
		$runspace.Dispose()
	}
	return [string]$sharedState.SelectedPath
}

function Select-GameDirectoryUnixGui {
	param([string]$Description)
	if ($IsWinPlatform) {
		return ''
	}
	try {
		if ($IsMacPlatform -and $null -ne (Get-Command 'osascript' -ErrorAction SilentlyContinue)) {
			$osaExpr = 'POSIX path of (choose folder with prompt "' + $Description + '")'
			$picked = (& osascript -e $osaExpr 2>$null)
			if (-not [string]::IsNullOrWhiteSpace($picked)) {
				return ([string]$picked).Trim()
			}
		} elseif (-not [string]::IsNullOrWhiteSpace($env:DISPLAY + $env:WAYLAND_DISPLAY)) {
			if ($null -ne (Get-Command 'zenity' -ErrorAction SilentlyContinue)) {
				$picked = (& zenity --file-selection --directory --title=$Description 2>$null)
				if (-not [string]::IsNullOrWhiteSpace($picked)) {
					return ([string]$picked).Trim()
				}
			} elseif ($null -ne (Get-Command 'kdialog' -ErrorAction SilentlyContinue)) {
				$picked = (& kdialog --getexistingdirectory $HOME --title $Description 2>$null)
				if (-not [string]::IsNullOrWhiteSpace($picked)) {
					return ([string]$picked).Trim()
				}
			}
		}
	} catch {
	}
	return ''
}

function Save-CachedGameDir {
	param([string]$ValidGameRoot)
	$targets = New-Object System.Collections.Generic.List[string]
	$defaultCfg = Get-DefaultUserConfigPath
	$targets.Add($defaultCfg)
	$repoCache = Get-RepoLocalCachePath
	if (-not [string]::IsNullOrWhiteSpace($repoCache)) {
		$targets.Add($repoCache)
	}
	$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
	foreach ($target in $targets) {
		if ([string]::IsNullOrWhiteSpace($target)) {
			continue
		}
		try {
			$dir = [System.IO.Path]::GetDirectoryName($target)
			if (-not [string]::IsNullOrWhiteSpace($dir) -and -not [System.IO.Directory]::Exists($dir)) {
				[void][System.IO.Directory]::CreateDirectory($dir)
			}
			[System.IO.File]::WriteAllText($target, ($ValidGameRoot + "`n"), $utf8NoBom)
			if (-not $ResolveOnly -and $target -eq $defaultCfg) {
				Write-Host ($Msg.InfoSavedCache + $target) -ForegroundColor DarkGray
			}
		} catch {
		}
	}
}

function Resolve-PayloadFiles {
	$scriptDir = Get-ScriptDirectory
	$candidateDirs = New-Object System.Collections.Generic.List[string]
	if (-not [string]::IsNullOrWhiteSpace($SourceDir)) {
		$candidateDirs.Add((Normalize-PathInput -RawPath $SourceDir))
	}
	$candidateDirs.Add($scriptDir)

	$repoParent = [System.IO.Path]::GetFullPath((Join-Path $scriptDir '..'))
	$candidateDirs.Add((Join-Path $repoParent 'Dist'))
	$relBin = Join-Path (Join-Path (Join-Path 'SephiriaOptimizer' 'bin') 'Release') 'netstandard2.1'
	$dbgBin = Join-Path (Join-Path (Join-Path 'SephiriaOptimizer' 'bin') 'Debug') 'netstandard2.1'
	$candidateDirs.Add((Join-Path $repoParent $relBin))
	$candidateDirs.Add((Join-Path $repoParent $dbgBin))

	foreach ($dir in $candidateDirs) {
		if ([string]::IsNullOrWhiteSpace($dir) -or -not [System.IO.Directory]::Exists($dir)) {
			continue
		}
		$dllCandidate = Join-Path $dir 'SephiriaOptimizer.dll'
		if (-not [System.IO.File]::Exists($dllCandidate)) {
			continue
		}
		$metaCandidate = Join-Path $dir 'metadata.json'
		if (-not [System.IO.File]::Exists($metaCandidate)) {
			$repoMeta = Join-Path (Join-Path $repoParent 'SephiriaOptimizer') 'metadata.json'
			if ([System.IO.File]::Exists($repoMeta)) {
				$metaCandidate = $repoMeta
			}
		}
		if ([System.IO.File]::Exists($metaCandidate)) {
			return [PSCustomObject]@{
				SourceRoot = $dir
				DllPath = $dllCandidate
				MetadataPath = $metaCandidate
			}
		}
	}
	return $null
}

function Test-IsUnextractedTempExecution {
	$scriptDir = Get-ScriptDirectory
	$tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd('\', '/')
	if ($scriptDir.StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
		$leaf = [System.IO.Path]::GetFileName($scriptDir)
		if ($leaf -match '^(Temp\d*_|7zO|Rar\$|wz|.*\.zip)') {
			return $true
		}
		$parentLeaf = [System.IO.Path]::GetFileName([System.IO.Path]::GetDirectoryName($scriptDir))
		if ($parentLeaf -match '^(Temp\d*_|7zO|Rar\$|wz|.*\.zip)') {
			return $true
		}
	}
	return $false
}

function Test-SephiriaRunningOrFileLocked {
	param(
		[string]$GameRoot,
		[string]$TargetDllPath
	)
	try {
		$procs = @(Get-Process -Name 'Sephiria', 'Sephiria.x86_64' -ErrorAction SilentlyContinue)
		foreach ($p in $procs) {
			try {
				$procPath = $p.MainModule.FileName
				if (-not [string]::IsNullOrWhiteSpace($procPath) `
					-and $procPath.StartsWith($GameRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
					return $true
				}
			} catch {
				return $true
			}
		}
	} catch {
	}

	if ([System.IO.File]::Exists($TargetDllPath)) {
		try {
			$attrs = [System.IO.File]::GetAttributes($TargetDllPath)
			$isReadOnly = ($attrs -band [System.IO.FileAttributes]::ReadOnly) -ne 0
			$accessMode = if ($isReadOnly) {
				[System.IO.FileAccess]::Read
			} else {
				[System.IO.FileAccess]::ReadWrite
			}
			$fs = [System.IO.File]::Open(
				$TargetDllPath,
				[System.IO.FileMode]::Open,
				$accessMode,
				[System.IO.FileShare]::None
			)
			$fs.Dispose()
		} catch [System.UnauthorizedAccessException] {
			return $false
		} catch [System.IO.IOException] {
			return $true
		}
	}
	return $false
}

function Wait-UntilUnlockedOrExit {
	param(
		[string]$GameRoot,
		[string]$TargetDllPath
	)
	while (Test-SephiriaRunningOrFileLocked -GameRoot $GameRoot -TargetDllPath $TargetDllPath) {
		if ($NonInteractive) {
			Write-Host $Msg.ErrLocked -ForegroundColor Red
			exit 3
		}
		if ($CanUseWinForms) {
			$choice = [System.Windows.Forms.MessageBox]::Show(
				$Msg.ErrLocked,
				$Msg.TitleLocked,
				[System.Windows.Forms.MessageBoxButtons]::RetryCancel,
				[System.Windows.Forms.MessageBoxIcon]::Warning
			)
			if ($choice -ne [System.Windows.Forms.DialogResult]::Retry) {
				exit 3
			}
		} else {
			Write-Host $Msg.ErrLocked -ForegroundColor Yellow
			$ans = Read-Host $Msg.PromptLockConsole
			if ([string]::IsNullOrWhiteSpace($ans) -or $ans.Trim().ToUpperInvariant() -ne 'R') {
				exit 3
			}
		}
	}
}

function Test-IsWindowsAdministrator {
	if (-not $IsWinPlatform) {
		return $false
	}
	try {
		$identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
		$principal = New-Object System.Security.Principal.WindowsPrincipal($identity)
		return $principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
	} catch {
		return $false
	}
}

function Test-DirectoryWriteAccess {
	param([string]$TargetPath)
	$probeDir = $TargetPath
	while (-not [string]::IsNullOrWhiteSpace($probeDir) -and -not [System.IO.Directory]::Exists($probeDir)) {
		$parent = [System.IO.Directory]::GetParent($probeDir)
		if ($null -eq $parent) {
			break
		}
		$probeDir = $parent.FullName
	}
	if ([string]::IsNullOrWhiteSpace($probeDir) -or -not [System.IO.Directory]::Exists($probeDir)) {
		return $false
	}
	$probeName = '.sephiria_write_probe_' + [System.Guid]::NewGuid().ToString('N') + '.tmp'
	$probeFile = Join-Path $probeDir $probeName
	try {
		[System.IO.File]::WriteAllText($probeFile, 'ok')
		[System.IO.File]::Delete($probeFile)
		return $true
	} catch {
		return $false
	}
}

function Format-QuotedWinArg {
	param([string]$Value)
	$escaped = $Value
	if ($escaped.EndsWith('\')) {
		$escaped = $escaped + '\'
	}
	return '"' + $escaped.Replace('"', '\"') + '"'
}

function Invoke-WindowsElevationIfNeeded {
	param(
		[string]$ResolvedGameRoot,
		[string]$ResolvedSourceRoot
	)
	$canElevate = $IsWinPlatform -and (-not $NonInteractive) -and (-not $ElevatedRetry)
	if (-not $canElevate -or (Test-IsWindowsAdministrator)) {
		return $false
	}
	if ([string]::IsNullOrWhiteSpace($PSCommandPath) -or -not [System.IO.File]::Exists($PSCommandPath)) {
		return $false
	}

	Write-Host $Msg.InfoElevating -ForegroundColor Yellow
	$psExe = (Get-Process -Id $PID).MainModule.FileName
	if ([string]::IsNullOrWhiteSpace($psExe)) {
		$psExe = 'powershell.exe'
	}

	$argList = New-Object System.Collections.Generic.List[string]
	$argList.Add('-NoProfile')
	$argList.Add('-ExecutionPolicy')
	$argList.Add('Bypass')
	$argList.Add('-File')
	$argList.Add((Format-QuotedWinArg -Value $PSCommandPath))
	$argList.Add('-GameDir')
	$argList.Add((Format-QuotedWinArg -Value $ResolvedGameRoot))
	if (-not [string]::IsNullOrWhiteSpace($ResolvedSourceRoot)) {
		$argList.Add('-SourceDir')
		$argList.Add((Format-QuotedWinArg -Value $ResolvedSourceRoot))
	}
	if (-not [string]::IsNullOrWhiteSpace($ConfigPath)) {
		$argList.Add('-ConfigPath')
		$argList.Add((Format-QuotedWinArg -Value $ConfigPath))
	}
	if ($Uninstall) {
		$argList.Add('-Uninstall')
	}
	$argList.Add('-ElevatedRetry')

	try {
		$joinedArgs = $argList -join ' '
		$proc = Start-Process -FilePath $psExe -ArgumentList $joinedArgs -Verb RunAs -Wait -PassThru
		exit $proc.ExitCode
	} catch {
		return $false
	}
}

function Clear-ReadOnlyAttribute {
	param([string]$TargetPath)
	if ([System.IO.File]::Exists($TargetPath)) {
		try {
			$attrs = [System.IO.File]::GetAttributes($TargetPath)
			if (($attrs -band [System.IO.FileAttributes]::ReadOnly) -ne 0) {
				$cleanAttrs = $attrs -band (-bnot [System.IO.FileAttributes]::ReadOnly)
				[System.IO.File]::SetAttributes($TargetPath, $cleanAttrs)
			}
		} catch {
		}
	}
}

function Clear-DirectoryReadOnlyAttributes {
	param([string]$DirPath)
	if (-not [System.IO.Directory]::Exists($DirPath)) {
		return
	}
	try {
		foreach ($file in [System.IO.Directory]::GetFiles($DirPath, '*', 1)) {
			Clear-ReadOnlyAttribute -TargetPath $file
		}
	} catch {
	}
}

function Clear-SecurityQuarantine {
	param([string]$FilePath)
	if (-not [System.IO.File]::Exists($FilePath)) {
		return
	}
	if ($IsWinPlatform) {
		try {
			Unblock-File -LiteralPath $FilePath -ErrorAction SilentlyContinue
		} catch {
		}
		try {
			Remove-Item -LiteralPath $FilePath -Stream 'Zone.Identifier' -Force -ErrorAction SilentlyContinue
		} catch {
		}
		try {
			$zoneStream = '\\?\' + $FilePath + ':Zone.Identifier'
			if ([System.IO.File]::Exists($zoneStream)) {
				[System.IO.File]::Delete($zoneStream)
			}
		} catch {
		}
	} elseif ($IsMacPlatform) {
		try {
			& /usr/bin/xattr -d com.apple.quarantine $FilePath 2>$null
		} catch {
		}
	}
}

if (-not $ResolveOnly) {
	$banner = if ($Uninstall) { $Msg.BannerUninstall } else { $Msg.BannerInstall }
	Write-Host '============================================================================' -ForegroundColor DarkCyan
	Write-Host ('  ' + $banner) -ForegroundColor Cyan
	Write-Host '============================================================================' -ForegroundColor DarkCyan
}

$payload = $null
if (-not $Uninstall -and -not $ResolveOnly) {
	$payload = Resolve-PayloadFiles
	if ($null -eq $payload) {
		$errMsg = if (Test-IsUnextractedTempExecution) { $Msg.ErrZipTemp } else { $Msg.ErrMissingPayload }
		Show-UserNotice -Message $errMsg -Title $Msg.TitleError -Level 'Error'
		exit 2
	}
}

$layout = $null

if (-not [string]::IsNullOrWhiteSpace($GameDir)) {
	$layout = Resolve-SephiriaLayout -CandidatePath $GameDir
	if ($null -eq $layout) {
		Show-UserNotice -Message ($Msg.ErrInvalidDir + "`n" + $GameDir) -Title $Msg.TitleError -Level 'Error'
		exit 1
	}
} else {
	$layout = Find-SephiriaLayoutAutomatically
}

if ($null -eq $layout) {
	if ($NonInteractive -or $ResolveOnly) {
		if (-not $ResolveOnly) {
			Write-Host $Msg.ErrInvalidDir -ForegroundColor Red
		}
		exit 1
	}

	if ($CanUseWinForms) {
		while ($null -eq $layout) {
			$picked = Select-GameDirectoryViaStaDialog -Description $Msg.PickerDesc
			if ([string]::IsNullOrWhiteSpace($picked)) {
				break
			}
			$layout = Resolve-SephiriaLayout -CandidatePath $picked
			if ($null -eq $layout) {
				Show-UserNotice -Message ($Msg.ErrInvalidDir + "`n" + $picked) -Title $Msg.TitleError -Level 'Warning'
			}
		}
	} else {
		$unixPicked = Select-GameDirectoryUnixGui -Description $Msg.PickerDesc
		if (-not [string]::IsNullOrWhiteSpace($unixPicked)) {
			$layout = Resolve-SephiriaLayout -CandidatePath $unixPicked
		}
		while ($null -eq $layout) {
			$typed = Read-Host $Msg.PromptConsolePath
			if ([string]::IsNullOrWhiteSpace($typed)) {
				break
			}
			$layout = Resolve-SephiriaLayout -CandidatePath $typed
			if ($null -eq $layout) {
				Write-Host ($Msg.ErrInvalidDir + ' -> ' + $typed) -ForegroundColor Yellow
			}
		}
	}

	if ($null -eq $layout) {
		Show-UserNotice -Message $Msg.ErrInvalidDir -Title $Msg.TitleCancel -Level 'Warning'
		exit 1
	}
}

Save-CachedGameDir -ValidGameRoot $layout.GameRoot

if ($ResolveOnly) {
	Write-Output $layout.ManagedDir
	exit 0
}

Write-Host ($Msg.InfoDetected + $layout.GameRoot) -ForegroundColor Cyan

if (-not $Uninstall) {
	if (-not (Test-AddOnLoaderSupport -ManagedDir $layout.ManagedDir)) {
		Show-UserNotice -Message $Msg.ErrOutdatedGame -Title $Msg.TitleError -Level 'Error'
		exit 5
	}
}

$targetModDir = $layout.ModDir
$targetDll = Join-Path $targetModDir 'SephiriaOptimizer.dll'
$targetMeta = Join-Path $targetModDir 'metadata.json'

if ($Uninstall) {
	Wait-UntilUnlockedOrExit -GameRoot $layout.GameRoot -TargetDllPath $targetDll
}

$sourceRootForElevation = if ($null -ne $payload) { $payload.SourceRoot } else { '' }
if (-not (Test-DirectoryWriteAccess -TargetPath $targetModDir)) {
	[void](Invoke-WindowsElevationIfNeeded -ResolvedGameRoot $layout.GameRoot -ResolvedSourceRoot $sourceRootForElevation)
	Show-UserNotice -Message ($Msg.ErrPermission + $targetModDir) -Title $Msg.TitleError -Level 'Error'
	exit 4
}

if ($Uninstall) {
	if ([System.IO.Directory]::Exists($targetModDir)) {
		try {
			Clear-DirectoryReadOnlyAttributes -DirPath $targetModDir
			[System.IO.Directory]::Delete($targetModDir, $true)
			if ([System.IO.Directory]::Exists($layout.AddOnsDir)) {
				$remainingEntries = [System.IO.Directory]::GetFileSystemEntries($layout.AddOnsDir)
				if ($remainingEntries.Length -eq 0) {
					[System.IO.Directory]::Delete($layout.AddOnsDir, $false)
				}
			}
			$doneText = [string]::Format($Msg.DoneUninstall, $targetModDir)
			Show-UserNotice -Message $doneText -Title $Msg.TitleDone -Level 'Success'
			exit 0
		} catch [System.UnauthorizedAccessException] {
			[void](Invoke-WindowsElevationIfNeeded -ResolvedGameRoot $layout.GameRoot -ResolvedSourceRoot '')
			Show-UserNotice -Message ($Msg.ErrPermission + $targetModDir) -Title $Msg.TitleError -Level 'Error'
			exit 4
		}
	} else {
		$noneText = [string]::Format($Msg.InfoAlreadyRemoved, $targetModDir)
		Show-UserNotice -Message $noneText -Title $Msg.TitleDone -Level 'Information'
		exit 0
	}
}

[void][System.IO.Directory]::CreateDirectory($targetModDir)

try {
	foreach ($oldBackup in [System.IO.Directory]::GetFiles($targetModDir, 'SephiriaOptimizer.dll.old*')) {
		try {
			Clear-ReadOnlyAttribute -TargetPath $oldBackup
			[System.IO.File]::Delete($oldBackup)
		} catch {
		}
	}
} catch {
}

$tmpDll = $targetDll + '.tmp'
$tmpMeta = $targetMeta + '.tmp'

try {
	Clear-ReadOnlyAttribute -TargetPath $tmpDll
	Clear-ReadOnlyAttribute -TargetPath $tmpMeta
	Clear-ReadOnlyAttribute -TargetPath $targetDll
	Clear-ReadOnlyAttribute -TargetPath $targetMeta

	Clear-SecurityQuarantine -FilePath $payload.DllPath
	[System.IO.File]::Copy($payload.DllPath, $tmpDll, $true)
	[System.IO.File]::Copy($payload.MetadataPath, $tmpMeta, $true)

	$srcDllLen = (New-Object System.IO.FileInfo($payload.DllPath)).Length
	$tmpDllLen = (New-Object System.IO.FileInfo($tmpDll)).Length
	$tmpMetaLen = (New-Object System.IO.FileInfo($tmpMeta)).Length
	if ($srcDllLen -le 0 -or $tmpDllLen -ne $srcDllLen -or $tmpMetaLen -le 0) {
		throw 'Payload integrity check failed during staging.'
	}

	Clear-SecurityQuarantine -FilePath $tmpDll
	Clear-SecurityQuarantine -FilePath $tmpMeta

	try {
		[System.IO.File]::Copy($tmpDll, $targetDll, $true)
	} catch [System.IO.IOException] {
		$backupDll = $targetDll + '.old'
		if ([System.IO.File]::Exists($backupDll)) {
			try {
				Clear-ReadOnlyAttribute -TargetPath $backupDll
				[System.IO.File]::Delete($backupDll)
			} catch {
				$backupDll = $targetDll + '.old.' + [System.DateTime]::UtcNow.Ticks
			}
		}
		try {
			if ([System.IO.File]::Exists($targetDll)) {
				[System.IO.File]::Move($targetDll, $backupDll)
			}
			[System.IO.File]::Copy($tmpDll, $targetDll, $true)
		} catch [System.IO.IOException] {
			Wait-UntilUnlockedOrExit -GameRoot $layout.GameRoot -TargetDllPath $targetDll
			[System.IO.File]::Copy($tmpDll, $targetDll, $true)
		}
	}
	[System.IO.File]::Copy($tmpMeta, $targetMeta, $true)
	Clear-SecurityQuarantine -FilePath $targetDll
	Clear-SecurityQuarantine -FilePath $targetMeta
} catch [System.UnauthorizedAccessException] {
	[void](Invoke-WindowsElevationIfNeeded -ResolvedGameRoot $layout.GameRoot -ResolvedSourceRoot $payload.SourceRoot)
	Show-UserNotice -Message ($Msg.ErrPermission + $targetModDir) -Title $Msg.TitleError -Level 'Error'
	exit 4
} finally {
	if ([System.IO.File]::Exists($tmpDll)) {
		try {
			Clear-ReadOnlyAttribute -TargetPath $tmpDll
			[System.IO.File]::Delete($tmpDll)
		} catch {
		}
	}
	if ([System.IO.File]::Exists($tmpMeta)) {
		try {
			Clear-ReadOnlyAttribute -TargetPath $tmpMeta
			[System.IO.File]::Delete($tmpMeta)
		} catch {
		}
	}
}

$successMsg = [string]::Format($Msg.DoneInstall, $targetModDir)
Show-UserNotice -Message $successMsg -Title $Msg.TitleDone -Level 'Success'
exit 0
