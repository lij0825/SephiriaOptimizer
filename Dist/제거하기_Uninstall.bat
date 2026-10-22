@echo off
chcp 65001 >nul
title Sephiria Optimizer 원클릭 제거기
echo ============================================================================
echo   [Sephiria Optimizer] 세피리아 최적화 모드 자동 제거기
echo ============================================================================
echo.

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
	"$ErrorActionPreference = 'Stop'; " ^
	"Add-Type -AssemblyName System.Windows.Forms; " ^
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
	"} " ^
	"$removed = $false; " ^
	"foreach ($c in $candidates) { " ^
	"    $modDir = Join-Path $c 'AddOns\SephiriaOptimizer'; " ^
	"    if (Test-Path $modDir) { " ^
	"        Remove-Item -Path $modDir -Recurse -Force; " ^
	"        Write-Host ('[제거 완료] ' + $modDir) -ForegroundColor Green; " ^
	"        $removed = $true; " ^
	"    } " ^
	"} " ^
	"if ($removed) { " ^
	"    [System.Windows.Forms.MessageBox]::Show('SephiriaOptimizer 모드가 깔끔하게 제거되었습니다.', '제거 완료', 'OK', 'Information') | Out-Null; " ^
	"} else { " ^
	"    [System.Windows.Forms.MessageBox]::Show('설치된 SephiriaOptimizer 모드를 찾을 수 없습니다.', '알림', 'OK', 'Information') | Out-Null; " ^
	"}"
