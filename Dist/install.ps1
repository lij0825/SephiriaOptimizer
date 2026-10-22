$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Add-Type -AssemblyName System.Windows.Forms

$candidates = New-Object System.Collections.Generic.List[string]
try {
	$steamPath = (Get-ItemProperty -Path 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue).SteamPath
	if ($steamPath) {
		$steamPath = $steamPath -replace '/', '\'
		$candidates.Add((Join-Path $steamPath 'steamapps\common\Sephiria'))
		$vdfPath = Join-Path $steamPath 'steamapps\libraryfolders.vdf'
		if (Test-Path $vdfPath) {
			$vdfLines = Get-Content $vdfPath -ErrorAction SilentlyContinue
			foreach ($line in $vdfLines) {
				if ($line -match '"path"\s+"([^"]+)"') {
					$libPath = $matches[1] -replace '\\\\', '\'
					$candidates.Add((Join-Path $libPath 'steamapps\common\Sephiria'))
				}
			}
		}
	}
} catch {}

foreach ($drive in [System.IO.DriveInfo]::GetDrives() | Where-Object { $_.DriveType -eq 'Fixed' }) {
	$root = $drive.RootDirectory.FullName
	$candidates.Add((Join-Path $root 'Steam\steamapps\common\Sephiria'))
	$candidates.Add((Join-Path $root 'SteamLibrary\steamapps\common\Sephiria'))
	$candidates.Add((Join-Path $root 'Program Files (x86)\Steam\steamapps\common\Sephiria'))
	$candidates.Add((Join-Path $root 'Program Files\Steam\steamapps\common\Sephiria'))
}

$gameDir = $null
foreach ($c in $candidates) {
	if (Test-Path (Join-Path $c 'Sephiria.exe')) {
		$gameDir = $c
		break
	}
}

if (-not $gameDir) {
	$dlg = New-Object System.Windows.Forms.FolderBrowserDialog
	$dlg.Description = '세피리아(Sephiria.exe)가 설치된 게임 폴더를 선택해주세요'
	if ($dlg.ShowDialog() -eq 'OK' -and (Test-Path (Join-Path $dlg.SelectedPath 'Sephiria.exe'))) {
		$gameDir = $dlg.SelectedPath
	} else {
		[System.Windows.Forms.MessageBox]::Show(
			'세피리아 설치 폴더(Sephiria.exe)를 찾지 못해 설치를 취소했습니다.',
			'Sephiria Optimizer 설치 취소',
			'OK',
			'Warning'
		) | Out-Null
		exit 1
	}
}

$targetDir = Join-Path $gameDir 'AddOns\SephiriaOptimizer'
New-Item -ItemType Directory -Path $targetDir -Force | Out-Null

$baseUrl = 'https://raw.githubusercontent.com/lij0825/SephiriaOptimizer/main/Dist'
Write-Host ('[감지된 게임 경로] ' + $gameDir) -ForegroundColor Cyan
Write-Host 'GitHub에서 최신 SephiriaOptimizer 다운로드 중...' -ForegroundColor Yellow

Invoke-WebRequest -Uri ($baseUrl + '/SephiriaOptimizer.dll') -OutFile (Join-Path $targetDir 'SephiriaOptimizer.dll') -UseBasicParsing
Invoke-WebRequest -Uri ($baseUrl + '/metadata.json') -OutFile (Join-Path $targetDir 'metadata.json') -UseBasicParsing
Unblock-File -Path (Join-Path $targetDir 'SephiriaOptimizer.dll') -ErrorAction SilentlyContinue

Write-Host ('[설치 완료!] ' + $targetDir) -ForegroundColor Green
[System.Windows.Forms.MessageBox]::Show(
	"세피리아 최적화 모드(SephiriaOptimizer) 최신 버전 설치가 완료되었습니다!`n`n설치 경로: $targetDir`n`n게임을 실행하고 인벤토리(I)를 열면 상단에 컨트롤 바가 나타납니다.`n- [F5] 자동 최적 재배치`n- [F6] 가이드 오버레이 켜기/끄기`n- [F7] 아티팩트 우선순위 / 목표 콤보 / 금빛침 설정",
	'Sephiria Optimizer 설치 완료',
	'OK',
	'Information'
) | Out-Null
