#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if command -v pwsh >/dev/null 2>&1; then
	exec pwsh -NoProfile -File "${SCRIPT_DIR}/install.ps1" "$@"
fi

if command -v powershell.exe >/dev/null 2>&1 && [[ "$(uname -s)" =~ ^(MINGW|MSYS|CYGWIN) ]]; then
	if command -v cygpath >/dev/null 2>&1; then
		WIN_SCRIPT="$(cygpath -w "${SCRIPT_DIR}/install.ps1")"
	else
		WIN_SCRIPT="${SCRIPT_DIR}/install.ps1"
	fi
	exec powershell.exe -NoProfile -ExecutionPolicy Bypass -File "${WIN_SCRIPT}" "$@"
fi

GAME_DIR=""
SOURCE_DIR=""
UNINSTALL=0
NON_INTERACTIVE=0
RESOLVE_ONLY=0
CONFIG_PATH=""

while [[ $# -gt 0 ]]; do
	case "$1" in
		--game-dir|-GameDir|-g)
			GAME_DIR="${2:-}"
			shift 2
			;;
		--source-dir|-SourceDir)
			SOURCE_DIR="${2:-}"
			shift 2
			;;
		--config-path|-ConfigPath)
			CONFIG_PATH="${2:-}"
			shift 2
			;;
		--uninstall|-Uninstall|-u)
			UNINSTALL=1
			shift
			;;
		--non-interactive|-NonInteractive|-y)
			NON_INTERACTIVE=1
			shift
			;;
		--resolve-only|-ResolveOnly)
			RESOLVE_ONLY=1
			shift
			;;
		*)
			if [[ -z "${GAME_DIR}" && "$1" != -* ]]; then
				GAME_DIR="$1"
			fi
			shift
			;;
	esac
done

OS_NAME="$(uname -s)"
if [[ -z "${CONFIG_PATH}" ]]; then
	if [[ "${OS_NAME}" == "Darwin" ]]; then
		CONFIG_PATH="${HOME}/Library/Application Support/SephiriaOptimizer/game_path.txt"
	else
		CONFIG_PATH="${XDG_CONFIG_HOME:-${HOME}/.config}/SephiriaOptimizer/game_path.txt"
	fi
fi

REPO_CACHE=""
if [[ -f "${SCRIPT_DIR}/../SephiriaOptimizer.sln" ]]; then
	REPO_CACHE="$(cd "${SCRIPT_DIR}/.." && pwd)/.sephiria-path"
fi

LAYOUT_GAME_ROOT=""
LAYOUT_MANAGED_DIR=""
LAYOUT_ADDONS_DIR=""
LAYOUT_MOD_DIR=""

resolve_sephiria_layout() {
	local raw="$1"
	raw="${raw#\"}"
	raw="${raw%\"}"
	raw="${raw#\'}"
	raw="${raw%\'}"
	[[ -z "${raw}" ]] && return 1

	local start_dir="${raw}"
	if [[ -f "${raw}" ]]; then
		start_dir="$(dirname "${raw}")"
	fi
	[[ -d "${start_dir}" ]] || return 1
	start_dir="$(cd "${start_dir}" 2>/dev/null && pwd)" || return 1

	local probes=(
		"${start_dir}"
		"${start_dir}/Sephiria"
		"${start_dir}/common/Sephiria"
		"${start_dir}/steamapps/common/Sephiria"
		"$(dirname "${start_dir}")"
		"$(dirname "$(dirname "${start_dir}")")"
		"$(dirname "$(dirname "$(dirname "${start_dir}")")")"
	)

	local probe
	for probe in "${probes[@]}"; do
		[[ -d "${probe}" ]] || continue
		if [[ -f "${probe}/Sephiria_Data/Managed/Assembly-CSharp.dll" ]]; then
			LAYOUT_GAME_ROOT="${probe}"
			LAYOUT_MANAGED_DIR="${probe}/Sephiria_Data/Managed"
			LAYOUT_ADDONS_DIR="${probe}/AddOns"
			LAYOUT_MOD_DIR="${LAYOUT_ADDONS_DIR}/SephiriaOptimizer"
			return 0
		fi
		local app_bundle="${probe}"
		if [[ "${app_bundle}" != *.app ]]; then
			app_bundle="${probe}/Sephiria.app"
		fi
		if [[ -f "${app_bundle}/Contents/Resources/Data/Managed/Assembly-CSharp.dll" ]]; then
			LAYOUT_GAME_ROOT="$(dirname "${app_bundle}")"
			LAYOUT_MANAGED_DIR="${app_bundle}/Contents/Resources/Data/Managed"
			LAYOUT_ADDONS_DIR="${app_bundle}/AddOns"
			LAYOUT_MOD_DIR="${LAYOUT_ADDONS_DIR}/SephiriaOptimizer"
			return 0
		fi
	done
	return 1
}

scan_vdf_and_root() {
	local steam_root="$1"
	[[ -d "${steam_root}" ]] || return 1

	local libs=("${steam_root}")
	local vdf
	for vdf in "${steam_root}/steamapps/libraryfolders.vdf" "${steam_root}/config/libraryfolders.vdf"; do
		[[ -f "${vdf}" ]] || continue
		while IFS= read -r line; do
			if [[ "${line}" =~ \"(path|[0-9]+)\"[[:space:]]+\"([^\"]+)\" ]]; then
				local parsed_lib="${BASH_REMATCH[2]//\\\\//}"
				[[ -d "${parsed_lib}" ]] && libs+=("${parsed_lib}")
			fi
		done < "${vdf}"
	done

	local lib
	for lib in "${libs[@]}"; do
		if resolve_sephiria_layout "${lib}/steamapps/common/Sephiria"; then
			return 0
		fi
	done
	return 1
}

auto_find_layout() {
	local env_cand
	for env_cand in "${SEPHIRIA_GAME_DIR:-}" "${SEPHIRIA_PATH:-}"; do
		if [[ -n "${env_cand}" ]] && resolve_sephiria_layout "${env_cand}"; then
			return 0
		fi
	done

	local cf
	for cf in "${REPO_CACHE}" "${CONFIG_PATH}"; do
		if [[ -n "${cf}" && -f "${cf}" ]]; then
			local cached
			cached="$(head -n 1 "${cf}" | tr -d '\r')"
			if [[ -n "${cached}" ]] && resolve_sephiria_layout "${cached}"; then
				return 0
			fi
		fi
	done

	local roots=()
	if [[ "${OS_NAME}" == "Darwin" ]]; then
		roots+=("${HOME}/Library/Application Support/Steam")
	else
		roots+=(
			"${HOME}/.local/share/Steam"
			"${HOME}/.steam/steam"
			"${HOME}/.steam/root"
			"${HOME}/.var/app/com.valvesoftware.Steam/.local/share/Steam"
			"${HOME}/snap/steam/common/.local/share/Steam"
			"/run/media/mmcblk0p1"
		)
		local mdir
		for mdir in /run/media/*/* /mnt/* /media/*/*; do
			[[ -d "${mdir}" ]] || continue
			roots+=("${mdir}" "${mdir}/SteamLibrary")
		done
	fi

	local r
	for r in "${roots[@]}"; do
		if scan_vdf_and_root "${r}"; then
			return 0
		fi
	done
	return 1
}

save_cached_path() {
	local valid_root="$1"
	mkdir -p "$(dirname "${CONFIG_PATH}")" 2>/dev/null || true
	printf '%s\n' "${valid_root}" > "${CONFIG_PATH}" 2>/dev/null || true
	if [[ -n "${REPO_CACHE}" ]]; then
		printf '%s\n' "${valid_root}" > "${REPO_CACHE}" 2>/dev/null || true
	fi
}

DLL_SRC=""
META_SRC=""
if [[ "${UNINSTALL}" -eq 0 && "${RESOLVE_ONLY}" -eq 0 ]]; then
	for sdir in \
		"${SOURCE_DIR}" \
		"${SCRIPT_DIR}" \
		"${SCRIPT_DIR}/../Dist" \
		"${SCRIPT_DIR}/../SephiriaOptimizer/bin/Release/netstandard2.1" \
		"${SCRIPT_DIR}/../SephiriaOptimizer/bin/Debug/netstandard2.1"; do
		[[ -z "${sdir}" ]] && continue
		if [[ -f "${sdir}/SephiriaOptimizer.dll" ]]; then
			DLL_SRC="${sdir}/SephiriaOptimizer.dll"
			if [[ -f "${sdir}/metadata.json" ]]; then
				META_SRC="${sdir}/metadata.json"
			elif [[ -f "${SCRIPT_DIR}/../SephiriaOptimizer/metadata.json" ]]; then
				META_SRC="${SCRIPT_DIR}/../SephiriaOptimizer/metadata.json"
			fi
			break
		fi
	done
	if [[ -z "${DLL_SRC}" || -z "${META_SRC}" ]]; then
		echo "[오류] SephiriaOptimizer.dll 또는 metadata.json을 찾을 수 없습니다. 압축을 모두 푼 뒤 실행하거나 dotnet build -c Release를 먼저 실행하세요." >&2
		exit 2
	fi
fi

if [[ -n "${GAME_DIR}" ]]; then
	if ! resolve_sephiria_layout "${GAME_DIR}"; then
		echo "[오류] 지정한 경로에서 세피리아(Sephiria_Data/Managed/Assembly-CSharp.dll)를 찾을 수 없습니다: ${GAME_DIR}" >&2
		exit 1
	fi
elif ! auto_find_layout; then
	if [[ "${NON_INTERACTIVE}" -eq 1 || "${RESOLVE_ONLY}" -eq 1 ]]; then
		[[ "${RESOLVE_ONLY}" -eq 0 ]] && echo "[오류] 세피리아 설치 경로 자동 탐색 실패 (--game-dir 경로를 지정하세요)." >&2
		exit 1
	fi

	PICKED=""
	if [[ "${OS_NAME}" == "Darwin" ]] && command -v osascript >/dev/null 2>&1; then
		PICKED="$(osascript -e 'POSIX path of (choose folder with prompt "세피리아(Sephiria)가 설치된 폴더를 선택해 주세요.")' 2>/dev/null || true)"
	elif command -v zenity >/dev/null 2>&1 && [[ -n "${DISPLAY:-}${WAYLAND_DISPLAY:-}" ]]; then
		PICKED="$(zenity --file-selection --directory --title="세피리아 설치 폴더 선택" 2>/dev/null || true)"
	elif command -v kdialog >/dev/null 2>&1 && [[ -n "${DISPLAY:-}${WAYLAND_DISPLAY:-}" ]]; then
		PICKED="$(kdialog --getexistingdirectory "${HOME}" --title "세피리아 설치 폴더 선택" 2>/dev/null || true)"
	fi

	if [[ -n "${PICKED}" ]]; then
		resolve_sephiria_layout "${PICKED}" || true
	fi

	while [[ -z "${LAYOUT_MANAGED_DIR}" ]]; do
		printf "세피리아 설치 폴더 경로를 입력하세요 (취소: Enter): "
		IFS= read -r TYPED || break
		[[ -z "${TYPED}" ]] && break
		resolve_sephiria_layout "${TYPED}" || echo "[경고] 유효한 세피리아 폴더가 아닙니다: ${TYPED}" >&2
	done

	if [[ -z "${LAYOUT_MANAGED_DIR}" ]]; then
		echo "[취소] 세피리아 설치 폴더가 확인되지 않아 작업을 종료합니다." >&2
		exit 1
	fi
fi

save_cached_path "${LAYOUT_GAME_ROOT}"

if [[ "${RESOLVE_ONLY}" -eq 1 ]]; then
	printf '%s\n' "${LAYOUT_MANAGED_DIR}"
	exit 0
fi

if command -v pgrep >/dev/null 2>&1; then
	if pgrep -f "Sephiria(\.exe|\.x86_64|\.app)" >/dev/null 2>&1; then
		echo "[오류] 세피리아(Sephiria) 게임이 실행 중입니다. 게임을 종료한 뒤 다시 실행해 주세요." >&2
		exit 3
	fi
fi

if [[ "${UNINSTALL}" -eq 1 ]]; then
	if [[ -d "${LAYOUT_MOD_DIR}" ]]; then
		if [[ ! -w "${LAYOUT_MOD_DIR}" ]]; then
			echo "[권한 오류] 쓰기 권한이 없습니다. 다음 명령어로 실행하세요: sudo \"$0\" --uninstall --game-dir \"${LAYOUT_GAME_ROOT}\"" >&2
			exit 4
		fi
		rm -rf "${LAYOUT_MOD_DIR}"
		rmdir "${LAYOUT_ADDONS_DIR}" 2>/dev/null || true
		echo "[제거 완료] ${LAYOUT_MOD_DIR}"
	else
		echo "[안내] 설치된 SephiriaOptimizer 모드가 없습니다: ${LAYOUT_MOD_DIR}"
	fi
	exit 0
fi

if ! grep -aq "AddOnLoader" "${LAYOUT_MANAGED_DIR}/Assembly-CSharp.dll" 2>/dev/null; then
	echo "[오류] 현재 세피리아 버전에 공식 내장 AddOnLoader가 없습니다. Steam에서 게임을 최신 버전으로 업데이트해 주세요." >&2
	exit 5
fi

mkdir -p "${LAYOUT_MOD_DIR}" 2>/dev/null || {
	echo "[권한 오류] 디렉터리 생성 권한이 없습니다. 다음 명령어로 실행하세요: sudo \"$0\" --game-dir \"${LAYOUT_GAME_ROOT}\"" >&2
	exit 4
}

if [[ ! -w "${LAYOUT_MOD_DIR}" ]]; then
	echo "[권한 오류] 쓰기 권한이 없습니다. 다음 명령어로 실행하세요: sudo \"$0\" --game-dir \"${LAYOUT_GAME_ROOT}\"" >&2
	exit 4
fi

cp -f "${DLL_SRC}" "${LAYOUT_MOD_DIR}/SephiriaOptimizer.dll.tmp"
cp -f "${META_SRC}" "${LAYOUT_MOD_DIR}/metadata.json.tmp"
mv -f "${LAYOUT_MOD_DIR}/SephiriaOptimizer.dll.tmp" "${LAYOUT_MOD_DIR}/SephiriaOptimizer.dll"
mv -f "${LAYOUT_MOD_DIR}/metadata.json.tmp" "${LAYOUT_MOD_DIR}/metadata.json"

if [[ "${OS_NAME}" == "Darwin" ]] && command -v xattr >/dev/null 2>&1; then
	xattr -d com.apple.quarantine "${LAYOUT_MOD_DIR}/SephiriaOptimizer.dll" 2>/dev/null || true
fi

echo "[설치 완료] ${LAYOUT_MOD_DIR}"
