@echo off
chcp 65001 >nul
title Sephiria Optimizer 원클릭 자동 설치기
echo ============================================================================
echo   [Sephiria Optimizer v2.0] 세피리아 인벤토리/석판 최적화 모드 자동 설치기
echo ============================================================================
echo.

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
	"$ErrorActionPreference = 'Stop'; " ^
	"Add-Type -AssemblyName System.Windows.Forms; " ^
	"$scriptDir = '%~dp0'.TrimEnd('\'); " ^
	"$dllPath = Join-Path $scriptDir 'SephiriaOptimizer.dll'; " ^
	"$metaPath = Join-Path $scriptDir 'metadata.json'; " ^
	"if (-not (Test-Path $dllPath)) { " ^
	"    [System.Windows.Forms.MessageBox]::Show('SephiriaOptimizer.dll 파일을 찾을 수 없습니다. 압축을 모두 풀고 실행해주세요.', '설치 오류', 'OK', 'Error') | Out-Null; " ^
	"    exit 1; " ^
	"} " ^
	"$candidates = New-Object System.Collections.Generic.List[string]; " ^
	"try { " ^
	"    $steamPath = (Get-ItemProperty -Path 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue).SteamPath; " ^
	"    if ($steamPath) { " ^
	"        $steamPath = $steamPath -replace '/', '\'; " ^
	"        $candidates.Add((Join-Path $steamPath 'steamapps\common\Sephiria')); " ^
	"        $vdfPath = Join-Path $steamPath 'steamapps\libraryfolders.vdf'; " ^
	"        if (Test-Path $vdfPath) { " ^
	"            $vdfLines = Get-Content $vdfPath -ErrorAction SilentlyContinue; " ^
	"            foreach ($line in $vdfLines) { " ^
	"                if ($line -match '\"path\"\s+\"([^\"]+)\"') { " ^
	"                    $libPath = $matches[1] -replace '\\\\', '\'; " ^
	"                    $candidates.Add((Join-Path $libPath 'steamapps\common\Sephiria')); " ^
	"                } " ^
	"            } " ^
	"        } " ^
	"    } " ^
	"} catch {} " ^
	"foreach ($drive in [System.IO.DriveInfo]::GetDrives() | Where-Object { $_.DriveType -eq 'Fixed' }) { " ^
	"    $root = $drive.RootDirectory.FullName; " ^
	"    $candidates.Add((Join-Path $root 'Steam\steamapps\common\Sephiria')); " ^
	"    $candidates.Add((Join-Path $root 'SteamLibrary\steamapps\common\Sephiria')); " ^
	"    $candidates.Add((Join-Path $root 'Program Files (x86)\Steam\steamapps\common\Sephiria')); " ^
	"    $candidates.Add((Join-Path $root 'Program Files\Steam\steamapps\common\Sephiria')); " ^
	"} " ^
	"$gameDir = $null; " ^
	"foreach ($c in $candidates) { " ^
	"    if (Test-Path (Join-Path $c 'Sephiria.exe')) { $gameDir = $c; break; } " ^
	"} " ^
	"if (-not $gameDir) { " ^
	"    Write-Host '스팀 기본 경로에서 세피리아를 찾지 못했습니다. 폴더 선택 창에서 Sephiria 폴더를 선택해주세요.' -ForegroundColor Yellow; " ^
	"    $dlg = New-Object System.Windows.Forms.FolderBrowserDialog; " ^
	"    $dlg.Description = '세피리아(Sephiria.exe)가 설치된 게임 폴더를 선택해주세요'; " ^
	"    if ($dlg.ShowDialog() -eq 'OK' -and (Test-Path (Join-Path $dlg.SelectedPath 'Sephiria.exe'))) { " ^
	"        $gameDir = $dlg.SelectedPath; " ^
	"    } else { " ^
	"        [System.Windows.Forms.MessageBox]::Show('유효한 세피리아 설치 폴더(Sephiria.exe)가 선택되지 않아 설치를 취소했습니다.', '설치 취소', 'OK', 'Warning') | Out-Null; " ^
	"        exit 1; " ^
	"    } " ^
	"} " ^
	"Write-Host ('[감지된 게임 경로] ' + $gameDir) -ForegroundColor Cyan; " ^
	"$targetDir = Join-Path $gameDir 'AddOns\SephiriaOptimizer'; " ^
	"New-Item -ItemType Directory -Path $targetDir -Force | Out-Null; " ^
	"Unblock-File -Path $dllPath -ErrorAction SilentlyContinue; " ^
	"Copy-Item -Path $dllPath -Destination (Join-Path $targetDir 'SephiriaOptimizer.dll') -Force; " ^
	"if (Test-Path $metaPath) { Copy-Item -Path $metaPath -Destination (Join-Path $targetDir 'metadata.json') -Force; } " ^
	"Unblock-File -Path (Join-Path $targetDir 'SephiriaOptimizer.dll') -ErrorAction SilentlyContinue; " ^
	"Write-Host ''; " ^
	"Write-Host '[설치 완료!] ' + $targetDir -ForegroundColor Green; " ^
	"[System.Windows.Forms.MessageBox]::Show(\"세피리아 최적화 모드(SephiriaOptimizer) 설치가 완료되었습니다!`n`n설치 경로: $targetDir`n`n게임을 실행하고 인벤토리(I)를 열면 바로 동작합니다.`n- [F5] 자동 최적 재배치`n- [F6] 가이드 오버레이 켜기/끄기`n- [F7] 아티팩트 우선순위 / 목표 콤보 / 금빛침 설정\", 'Sephiria Optimizer 설치 완료', 'OK', 'Information') | Out-Null;"
