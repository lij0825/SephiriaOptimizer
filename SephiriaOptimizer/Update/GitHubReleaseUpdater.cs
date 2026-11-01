using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine.Networking;

namespace SephiriaOptimizer.Update {
	public enum UpdateCheckState {
		Idle = 0,
		Checking = 1,
		UpToDate = 2,
		UpdateAvailable = 3,
		Downloading = 4,
		AppliedPendingRestart = 5,
		Failed = 6
	}

	public enum UpdateApplyOutcome {
		Failed = 0,
		AppliedInPlacePendingRestart = 1,
		ScheduledOnExit = 2
	}

	public sealed class GitHubReleaseAssetInfo {
		public string Name { get; }
		public string DownloadUrl { get; }
		public long SizeBytes { get; }

		public GitHubReleaseAssetInfo(string name, string downloadUrl, long sizeBytes) {
			Name = name;
			DownloadUrl = downloadUrl;
			SizeBytes = sizeBytes;
		}
	}

	public sealed class GitHubReleaseInfo {
		public string TagName { get; }
		public string NormalizedVersion { get; }
		public string Title { get; }
		public string HtmlUrl { get; }
		public bool IsDraft { get; }
		public bool IsPrerelease { get; }
		public string PreferredAssetName { get; }
		public string PreferredAssetUrl { get; }

		public bool HasDownloadableAsset =>
			!string.IsNullOrEmpty(PreferredAssetUrl) && !string.IsNullOrEmpty(PreferredAssetName);

		public GitHubReleaseInfo(
			string tagName,
			string normalizedVersion,
			string title,
			string htmlUrl,
			bool isDraft,
			bool isPrerelease,
			string preferredAssetName,
			string preferredAssetUrl
		) {
			TagName = tagName;
			NormalizedVersion = normalizedVersion;
			Title = title;
			HtmlUrl = htmlUrl;
			IsDraft = isDraft;
			IsPrerelease = isPrerelease;
			PreferredAssetName = preferredAssetName;
			PreferredAssetUrl = preferredAssetUrl;
		}
	}

	public sealed class UpdateApplyResult {
		public UpdateApplyOutcome Outcome { get; }
		public string Message { get; }

		public bool IsSuccess =>
			Outcome == UpdateApplyOutcome.AppliedInPlacePendingRestart
			|| Outcome == UpdateApplyOutcome.ScheduledOnExit;

		public UpdateApplyResult(UpdateApplyOutcome outcome, string message) {
			Outcome = outcome;
			Message = message;
		}
	}

	public static class GitHubReleaseParser {
		public static bool IsTrustedGitHubUrl(string? candidateUrl) {
			if (string.IsNullOrWhiteSpace(candidateUrl)) {
				return false;
			}
			if (!Uri.TryCreate(candidateUrl.Trim(), UriKind.Absolute, out Uri? parsedUri)) {
				return false;
			}
			if (!string.Equals(parsedUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) {
				return false;
			}

			string host = parsedUri.Host;
			if (string.Equals(host, "github.com", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(host, "api.github.com", StringComparison.OrdinalIgnoreCase)) {
				return true;
			}
			if (host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase)
				|| host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase)) {
				return true;
			}
			return false;
		}

		public static bool TryParseLatestRelease(string? jsonText, out GitHubReleaseInfo? releaseInfo) {
			releaseInfo = null;
			if (string.IsNullOrWhiteSpace(jsonText)) {
				return false;
			}

			string tagName = string.Empty;
			string releaseTitle = string.Empty;
			string htmlUrl = ModVersionConstants.GitHubReleasesPageUrl;
			bool isDraft = false;
			bool isPrerelease = false;
			List<GitHubReleaseAssetInfo> assets = new List<GitHubReleaseAssetInfo>(4);

			int cursor = 0;
			SkipWhitespace(jsonText, ref cursor);
			if (cursor >= jsonText.Length || jsonText[cursor] != '{') {
				return false;
			}
			cursor++;

			while (cursor < jsonText.Length) {
				SkipWhitespace(jsonText, ref cursor);
				if (cursor >= jsonText.Length || jsonText[cursor] == '}') {
					break;
				}
				if (jsonText[cursor] == ',') {
					cursor++;
					continue;
				}
				if (!TryReadJsonString(jsonText, ref cursor, out string propName)) {
					return false;
				}
				SkipWhitespace(jsonText, ref cursor);
				if (cursor >= jsonText.Length || jsonText[cursor] != ':') {
					return false;
				}
				cursor++;
				SkipWhitespace(jsonText, ref cursor);

				if (string.Equals(propName, "tag_name", StringComparison.Ordinal)) {
					if (TryReadJsonStringOrNull(jsonText, ref cursor, out string? parsedTag)) {
						tagName = parsedTag ?? string.Empty;
					} else {
						SkipJsonValue(jsonText, ref cursor);
					}
				} else if (string.Equals(propName, "name", StringComparison.Ordinal)) {
					if (TryReadJsonStringOrNull(jsonText, ref cursor, out string? parsedTitle)) {
						releaseTitle = parsedTitle ?? string.Empty;
					} else {
						SkipJsonValue(jsonText, ref cursor);
					}
				} else if (string.Equals(propName, "html_url", StringComparison.Ordinal)) {
					if (TryReadJsonStringOrNull(jsonText, ref cursor, out string? parsedUrl)
						&& !string.IsNullOrWhiteSpace(parsedUrl)
						&& IsTrustedGitHubUrl(parsedUrl)) {
						htmlUrl = parsedUrl;
					} else {
						SkipJsonValue(jsonText, ref cursor);
					}
				} else if (string.Equals(propName, "draft", StringComparison.Ordinal)) {
					if (TryReadJsonBoolean(jsonText, ref cursor, out bool parsedDraft)) {
						isDraft = parsedDraft;
					} else {
						SkipJsonValue(jsonText, ref cursor);
					}
				} else if (string.Equals(propName, "prerelease", StringComparison.Ordinal)) {
					if (TryReadJsonBoolean(jsonText, ref cursor, out bool parsedPre)) {
						isPrerelease = parsedPre;
					} else {
						SkipJsonValue(jsonText, ref cursor);
					}
				} else if (string.Equals(propName, "assets", StringComparison.Ordinal)) {
					ParseAssetsArray(jsonText, ref cursor, assets);
				} else {
					SkipJsonValue(jsonText, ref cursor);
				}
			}

			if (string.IsNullOrWhiteSpace(tagName)) {
				return false;
			}

			string normalizedVer = VersionCompatibilityChecker.NormalizeVersionText(tagName);
			SelectPreferredAsset(assets, out string bestAssetName, out string bestAssetUrl);

			releaseInfo = new GitHubReleaseInfo(
				tagName.Trim(),
				normalizedVer,
				string.IsNullOrWhiteSpace(releaseTitle) ? tagName.Trim() : releaseTitle.Trim(),
				htmlUrl,
				isDraft,
				isPrerelease,
				bestAssetName,
				bestAssetUrl
			);
			return true;
		}

		private static void SelectPreferredAsset(
			List<GitHubReleaseAssetInfo> assets,
			out string assetName,
			out string assetUrl
		) {
			assetName = string.Empty;
			assetUrl = string.Empty;
			int bestPriority = 0;

			for (int i = 0; i < assets.Count; i++) {
				GitHubReleaseAssetInfo candidate = assets[i];
				if (string.IsNullOrWhiteSpace(candidate.Name) || !IsTrustedGitHubUrl(candidate.DownloadUrl)) {
					continue;
				}

				int priority = 0;
				bool isZip = candidate.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
				bool isModPrefix = candidate.Name.StartsWith(
					"SephiriaOptimizer",
					StringComparison.OrdinalIgnoreCase
				);
				if (isZip && isModPrefix) {
					priority = 3;
				} else if (isZip) {
					priority = 2;
				} else if (string.Equals(
					candidate.Name,
					ModVersionConstants.ModDllFileName,
					StringComparison.OrdinalIgnoreCase
				)) {
					priority = 1;
				}

				if (priority > bestPriority) {
					bestPriority = priority;
					assetName = candidate.Name;
					assetUrl = candidate.DownloadUrl;
				}
			}
		}

		private static void ParseAssetsArray(
			string jsonText,
			ref int cursor,
			List<GitHubReleaseAssetInfo> assets
		) {
			SkipWhitespace(jsonText, ref cursor);
			if (cursor >= jsonText.Length || jsonText[cursor] != '[') {
				SkipJsonValue(jsonText, ref cursor);
				return;
			}
			cursor++;

			while (cursor < jsonText.Length) {
				SkipWhitespace(jsonText, ref cursor);
				if (cursor >= jsonText.Length) {
					return;
				}
				if (jsonText[cursor] == ']') {
					cursor++;
					return;
				}
				if (jsonText[cursor] == ',') {
					cursor++;
					continue;
				}
				if (jsonText[cursor] != '{') {
					SkipJsonValue(jsonText, ref cursor);
					continue;
				}

				cursor++;
				string assetName = string.Empty;
				string downloadUrl = string.Empty;
				long sizeBytes = 0;

				while (cursor < jsonText.Length) {
					SkipWhitespace(jsonText, ref cursor);
					if (cursor >= jsonText.Length) {
						return;
					}
					if (jsonText[cursor] == '}') {
						cursor++;
						break;
					}
					if (jsonText[cursor] == ',') {
						cursor++;
						continue;
					}
					if (!TryReadJsonString(jsonText, ref cursor, out string fieldKey)) {
						SkipJsonValue(jsonText, ref cursor);
						continue;
					}
					SkipWhitespace(jsonText, ref cursor);
					if (cursor >= jsonText.Length || jsonText[cursor] != ':') {
						break;
					}
					cursor++;
					SkipWhitespace(jsonText, ref cursor);

					if (string.Equals(fieldKey, "name", StringComparison.Ordinal)) {
						if (TryReadJsonStringOrNull(jsonText, ref cursor, out string? parsedName)) {
							assetName = parsedName ?? string.Empty;
						} else {
							SkipJsonValue(jsonText, ref cursor);
						}
					} else if (string.Equals(fieldKey, "browser_download_url", StringComparison.Ordinal)) {
						if (TryReadJsonStringOrNull(jsonText, ref cursor, out string? parsedDownload)) {
							downloadUrl = parsedDownload ?? string.Empty;
						} else {
							SkipJsonValue(jsonText, ref cursor);
						}
					} else if (string.Equals(fieldKey, "size", StringComparison.Ordinal)) {
						if (TryReadJsonLong(jsonText, ref cursor, out long parsedSize)) {
							sizeBytes = parsedSize;
						} else {
							SkipJsonValue(jsonText, ref cursor);
						}
					} else {
						SkipJsonValue(jsonText, ref cursor);
					}
				}

				if (!string.IsNullOrWhiteSpace(assetName) && !string.IsNullOrWhiteSpace(downloadUrl)) {
					assets.Add(new GitHubReleaseAssetInfo(assetName.Trim(), downloadUrl.Trim(), sizeBytes));
				}
			}
		}

		private static void SkipWhitespace(string text, ref int cursor) {
			while (cursor < text.Length && char.IsWhiteSpace(text[cursor])) {
				cursor++;
			}
		}

		private static bool TryReadJsonStringOrNull(string text, ref int cursor, out string? result) {
			SkipWhitespace(text, ref cursor);
			if (cursor + 4 <= text.Length
				&& string.CompareOrdinal(text, cursor, "null", 0, 4) == 0) {
				cursor += 4;
				result = null;
				return true;
			}
			if (TryReadJsonString(text, ref cursor, out string nonNull)) {
				result = nonNull;
				return true;
			}
			result = null;
			return false;
		}

		private static bool TryReadJsonString(string text, ref int cursor, out string result) {
			result = string.Empty;
			SkipWhitespace(text, ref cursor);
			if (cursor >= text.Length || text[cursor] != '"') {
				return false;
			}
			cursor++;
			StringBuilder builder = new StringBuilder(32);

			while (cursor < text.Length) {
				char currentChar = text[cursor];
				if (currentChar == '\\') {
					cursor++;
					if (cursor >= text.Length) {
						return false;
					}
					char esc = text[cursor];
					if (esc == '"' || esc == '\\' || esc == '/') {
						builder.Append(esc);
					} else if (esc == 'n') {
						builder.Append('\n');
					} else if (esc == 'r') {
						builder.Append('\r');
					} else if (esc == 't') {
						builder.Append('\t');
					} else if (esc == 'u' && cursor + 4 < text.Length) {
						string hexSlice = text.Substring(cursor + 1, 4);
						if (int.TryParse(
							hexSlice,
							NumberStyles.HexNumber,
							CultureInfo.InvariantCulture,
							out int codePoint
						)) {
							builder.Append((char)codePoint);
							cursor += 4;
						}
					} else {
						builder.Append(esc);
					}
					cursor++;
					continue;
				}
				if (currentChar == '"') {
					cursor++;
					result = builder.ToString();
					return true;
				}
				builder.Append(currentChar);
				cursor++;
			}
			return false;
		}

		private static bool TryReadJsonBoolean(string text, ref int cursor, out bool value) {
			value = false;
			SkipWhitespace(text, ref cursor);
			if (cursor + 4 <= text.Length
				&& string.CompareOrdinal(text, cursor, "true", 0, 4) == 0) {
				cursor += 4;
				value = true;
				return true;
			}
			if (cursor + 5 <= text.Length
				&& string.CompareOrdinal(text, cursor, "false", 0, 5) == 0) {
				cursor += 5;
				value = false;
				return true;
			}
			return false;
		}

		private static bool TryReadJsonLong(string text, ref int cursor, out long value) {
			value = 0;
			SkipWhitespace(text, ref cursor);
			int start = cursor;
			if (cursor < text.Length && text[cursor] == '-') {
				cursor++;
			}
			while (cursor < text.Length && char.IsDigit(text[cursor])) {
				cursor++;
			}
			if (cursor <= start) {
				return false;
			}
			string slice = text.Substring(start, cursor - start);
			return long.TryParse(slice, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
		}

		private static void SkipJsonValue(string text, ref int cursor) {
			SkipWhitespace(text, ref cursor);
			if (cursor >= text.Length) {
				return;
			}

			char token = text[cursor];
			if (token == '"') {
				TryReadJsonString(text, ref cursor, out _);
				return;
			}

			if (token == '{' || token == '[') {
				int depth = 0;
				bool inString = false;
				while (cursor < text.Length) {
					char currentChar = text[cursor];
					if (inString) {
						if (currentChar == '\\') {
							cursor += 2;
							continue;
						}
						if (currentChar == '"') {
							inString = false;
						}
						cursor++;
						continue;
					}

					if (currentChar == '"') {
						inString = true;
					} else if (currentChar == '{' || currentChar == '[') {
						depth++;
					} else if (currentChar == '}' || currentChar == ']') {
						depth--;
						if (depth <= 0) {
							cursor++;
							return;
						}
					}
					cursor++;
				}
				return;
			}

			while (cursor < text.Length) {
				char currentChar = text[cursor];
				if (currentChar == ',' || currentChar == '}' || currentChar == ']' || char.IsWhiteSpace(currentChar)) {
					return;
				}
				cursor++;
			}
		}
	}

	public sealed class GitHubReleaseUpdater {
		private const int MinValidPeSizeBytes = 4096;
		private const int MaxAllowedDllSizeBytes = 32 * 1024 * 1024;
		private const int MaxAllowedMetadataSizeBytes = 256 * 1024;
		private const int WebRequestTimeoutSeconds = 15;

		public UpdateCheckState CurrentState { get; private set; } = UpdateCheckState.Idle;
		public GitHubReleaseInfo? LatestRelease { get; private set; }
		public string StatusMessage { get; private set; } = string.Empty;
		public float DownloadProgress { get; private set; }

		public static int CleanupOldBackupFiles(string? modDirectory = null) {
			string resolvedDir = VersionCompatibilityChecker.ResolveModDirectory(modDirectory);
			if (!Directory.Exists(resolvedDir)) {
				return 0;
			}

			int deletedCount = 0;
			string[] stalePatterns = new[] { "*.dll.old*", "*.dll.new", "*.metadata.new", "metadata.json.new" };
			for (int p = 0; p < stalePatterns.Length; p++) {
				string[] candidates;
				try {
					candidates = Directory.GetFiles(resolvedDir, stalePatterns[p], SearchOption.TopDirectoryOnly);
				} catch (IOException) {
					continue;
				} catch (UnauthorizedAccessException) {
					continue;
				}

				for (int i = 0; i < candidates.Length; i++) {
					try {
						File.SetAttributes(candidates[i], FileAttributes.Normal);
						File.Delete(candidates[i]);
						deletedCount++;
					} catch (IOException) {
						continue;
					} catch (UnauthorizedAccessException) {
						continue;
					}
				}
			}
			return deletedCount;
		}

		public static bool IsValidPeDllBytes(byte[]? dllBytes) {
			if (dllBytes == null
				|| dllBytes.Length < MinValidPeSizeBytes
				|| dllBytes.Length > MaxAllowedDllSizeBytes) {
				return false;
			}
			if (dllBytes[0] != 0x4D || dllBytes[1] != 0x5A) {
				return false;
			}

			int peHeaderOffset = BitConverter.ToInt32(dllBytes, 0x3C);
			if (peHeaderOffset < 0x40 || peHeaderOffset > dllBytes.Length - 4) {
				return false;
			}
			return dllBytes[peHeaderOffset] == 0x50
				&& dllBytes[peHeaderOffset + 1] == 0x45
				&& dllBytes[peHeaderOffset + 2] == 0x00
				&& dllBytes[peHeaderOffset + 3] == 0x00;
		}

		public static UpdateApplyResult ApplyDownloadedPayload(
			byte[]? payloadBytes,
			string? assetFileName,
			string modDirectory,
			string? targetVersion = null,
			bool allowExternalHelperFallback = true
		) {
			if (payloadBytes == null || payloadBytes.Length == 0) {
				return new UpdateApplyResult(UpdateApplyOutcome.Failed, "다운로드된 파일 데이터가 비어 있습니다.");
			}
			if (string.IsNullOrWhiteSpace(modDirectory)) {
				return new UpdateApplyResult(UpdateApplyOutcome.Failed, "모드 설치 경로를 확인할 수 없습니다.");
			}

			if (!TryExtractPayloadEntries(
				payloadBytes,
				assetFileName,
				out byte[]? extractedDllBytes,
				out byte[]? extractedMetadataBytes,
				out string extractError
			)) {
				return new UpdateApplyResult(UpdateApplyOutcome.Failed, extractError);
			}

			if (!IsValidPeDllBytes(extractedDllBytes)) {
				return new UpdateApplyResult(
					UpdateApplyOutcome.Failed,
					"다운로드된 SephiriaOptimizer.dll의 PE 헤더 무결성 검증에 실패했습니다."
				);
			}

			string fullModDir = Path.GetFullPath(modDirectory);
			Directory.CreateDirectory(fullModDir);
			CleanupOldBackupFiles(fullModDir);

			byte[]? finalMetadataBytes = BuildUpdatedMetadataBytes(
				fullModDir,
				extractedMetadataBytes,
				targetVersion
			);

			string targetDllPath = Path.Combine(fullModDir, ModVersionConstants.ModDllFileName);
			string stagedDllPath = targetDllPath + ".new";
			string targetMetaPath = Path.Combine(fullModDir, ModVersionConstants.MetadataFileName);
			string stagedMetaPath = targetMetaPath + ".new";

			try {
				WriteAllBytesFlushed(stagedDllPath, extractedDllBytes!);
				if (finalMetadataBytes != null) {
					WriteAllBytesFlushed(stagedMetaPath, finalMetadataBytes);
				}
			} catch (Exception writeEx) when (writeEx is IOException || writeEx is UnauthorizedAccessException) {
				TryDeleteQuietly(stagedDllPath);
				TryDeleteQuietly(stagedMetaPath);
				return new UpdateApplyResult(
					UpdateApplyOutcome.Failed,
					$"모드 폴더 쓰기 권한이 부족합니다: {writeEx.Message}"
				);
			}

			string? movedBackupPath = null;
			try {
				if (File.Exists(targetDllPath)) {
					movedBackupPath = ChooseAvailableBackupPath(targetDllPath);
					File.Move(targetDllPath, movedBackupPath);
				}

				File.Move(stagedDllPath, targetDllPath);
				TryRemoveZoneIdentifier(targetDllPath);

				if (finalMetadataBytes != null && File.Exists(stagedMetaPath)) {
					File.Copy(stagedMetaPath, targetMetaPath, true);
					TryDeleteQuietly(stagedMetaPath);
				}

				string verLabel = string.IsNullOrWhiteSpace(targetVersion) ? "최신 버전" : $"v{targetVersion}";
				return new UpdateApplyResult(
					UpdateApplyOutcome.AppliedInPlacePendingRestart,
					$"{verLabel} 설치 완료 — 게임을 재시작하면 적용됩니다."
				);
			} catch (Exception swapEx) when (swapEx is IOException || swapEx is UnauthorizedAccessException) {
				if (movedBackupPath != null && !File.Exists(targetDllPath) && File.Exists(movedBackupPath)) {
					try {
						File.Move(movedBackupPath, targetDllPath);
					} catch (Exception rollbackEx) {
						Trace.WriteLine($"[SephiriaOptimizer] Backup rollback skipped: {rollbackEx.Message}");
					}
				}
				TryDeleteQuietly(stagedDllPath);
				TryDeleteQuietly(stagedMetaPath);

				if (allowExternalHelperFallback
					&& TrySchedulePostExitReplacement(fullModDir, extractedDllBytes!, finalMetadataBytes)) {
					string verLabel = string.IsNullOrWhiteSpace(targetVersion) ? "최신 버전" : $"v{targetVersion}";
					return new UpdateApplyResult(
						UpdateApplyOutcome.ScheduledOnExit,
						$"{verLabel} 다운로드 완료 — 게임 종료 즉시 자동 교체됩니다."
					);
				}

				return new UpdateApplyResult(
					UpdateApplyOutcome.Failed,
					$"DLL 파일 교체 실패: {swapEx.Message}"
				);
			}
		}

		public IEnumerator CheckForUpdatesCoroutine(
			string currentModVersion,
			Action? onStateChanged = null
		) {
			if (CurrentState == UpdateCheckState.Checking || CurrentState == UpdateCheckState.Downloading) {
				yield break;
			}

			CurrentState = UpdateCheckState.Checking;
			StatusMessage = "GitHub 최신 릴리즈 확인 중...";
			onStateChanged?.Invoke();

			using UnityWebRequest request = UnityWebRequest.Get(ModVersionConstants.GitHubLatestReleaseApiUrl);
			request.timeout = WebRequestTimeoutSeconds;
			string safeVer = string.IsNullOrWhiteSpace(currentModVersion)
				? ModVersionConstants.CurrentModVersion
				: currentModVersion;
			request.SetRequestHeader("User-Agent", "SephiriaOptimizer/" + safeVer);
			request.SetRequestHeader("Accept", "application/vnd.github+json");

			yield return request.SendWebRequest();

			if (request.result != UnityWebRequest.Result.Success) {
				if (request.responseCode == 404) {
					CurrentState = UpdateCheckState.UpToDate;
					StatusMessage = $"현재 최신 버전입니다 (v{safeVer}).";
				} else {
					CurrentState = UpdateCheckState.Failed;
					StatusMessage = $"업데이트 확인 실패 (HTTP {request.responseCode}).";
				}
				onStateChanged?.Invoke();
				yield break;
			}

			string responseJson = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
			if (!GitHubReleaseParser.TryParseLatestRelease(responseJson, out GitHubReleaseInfo? release)
				|| release == null
				|| release.IsDraft) {
				CurrentState = UpdateCheckState.UpToDate;
				StatusMessage = $"현재 최신 버전입니다 (v{safeVer}).";
				onStateChanged?.Invoke();
				yield break;
			}

			LatestRelease = release;
			if (!release.IsPrerelease
				&& VersionCompatibilityChecker.IsNewerVersion(release.NormalizedVersion, safeVer)) {
				CurrentState = UpdateCheckState.UpdateAvailable;
				StatusMessage = $"새 버전 v{release.NormalizedVersion} 출시됨!";
			} else {
				CurrentState = UpdateCheckState.UpToDate;
				StatusMessage = $"현재 최신 버전입니다 (v{safeVer}).";
			}
			onStateChanged?.Invoke();
		}

		public IEnumerator DownloadAndApplyUpdateCoroutine(
			string modDirectory,
			Action? onStateChanged = null
		) {
			if (CurrentState == UpdateCheckState.Downloading) {
				yield break;
			}

			GitHubReleaseInfo? release = LatestRelease;
			if (release == null || !release.HasDownloadableAsset) {
				CurrentState = UpdateCheckState.Failed;
				StatusMessage = "릴리즈에 다운로드 가능한 ZIP/DLL 에셋이 없습니다.";
				onStateChanged?.Invoke();
				yield break;
			}

			if (!GitHubReleaseParser.IsTrustedGitHubUrl(release.PreferredAssetUrl)) {
				CurrentState = UpdateCheckState.Failed;
				StatusMessage = "신뢰할 수 없는 다운로드 주소가 차단되었습니다.";
				onStateChanged?.Invoke();
				yield break;
			}

			CurrentState = UpdateCheckState.Downloading;
			DownloadProgress = 0f;
			StatusMessage = $"v{release.NormalizedVersion} 다운로드 중...";
			onStateChanged?.Invoke();

			using UnityWebRequest request = UnityWebRequest.Get(release.PreferredAssetUrl);
			request.timeout = 45;
			request.SetRequestHeader(
				"User-Agent",
				"SephiriaOptimizer/" + ModVersionConstants.CurrentModVersion
			);

			UnityWebRequestAsyncOperation asyncOp = request.SendWebRequest();
			while (!asyncOp.isDone) {
				DownloadProgress = request.downloadProgress;
				yield return null;
			}

			if (request.result != UnityWebRequest.Result.Success) {
				CurrentState = UpdateCheckState.Failed;
				StatusMessage = $"다운로드 실패 (HTTP {request.responseCode}): {request.error}";
				onStateChanged?.Invoke();
				yield break;
			}

			byte[]? downloadedBytes = request.downloadHandler != null ? request.downloadHandler.data : null;
			UpdateApplyResult applyResult = ApplyDownloadedPayload(
				downloadedBytes,
				release.PreferredAssetName,
				modDirectory,
				release.NormalizedVersion,
				true
			);

			if (applyResult.IsSuccess) {
				CurrentState = UpdateCheckState.AppliedPendingRestart;
				DownloadProgress = 1f;
				StatusMessage = applyResult.Message;
			} else {
				CurrentState = UpdateCheckState.Failed;
				StatusMessage = applyResult.Message;
			}
			onStateChanged?.Invoke();
		}

		private static bool TryExtractPayloadEntries(
			byte[] payloadBytes,
			string? assetFileName,
			out byte[]? dllBytes,
			out byte[]? metadataBytes,
			out string errorMessage
		) {
			dllBytes = null;
			metadataBytes = null;
			errorMessage = string.Empty;

			bool looksLikeZip = (assetFileName != null
					&& assetFileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
				|| (payloadBytes.Length >= 4
					&& payloadBytes[0] == 0x50
					&& payloadBytes[1] == 0x4B
					&& payloadBytes[2] == 0x03
					&& payloadBytes[3] == 0x04);

			if (!looksLikeZip) {
				dllBytes = payloadBytes;
				return true;
			}

			try {
				using MemoryStream zipStream = new MemoryStream(payloadBytes, false);
				using ZipArchive archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
				foreach (ZipArchiveEntry entry in archive.Entries) {
					string entryFileName = Path.GetFileName(entry.FullName);
					if (string.IsNullOrEmpty(entryFileName)) {
						continue;
					}

					if (string.Equals(
						entryFileName,
						ModVersionConstants.ModDllFileName,
						StringComparison.OrdinalIgnoreCase
					)) {
						if (entry.Length > MaxAllowedDllSizeBytes) {
							errorMessage = "압축 파일 내 DLL 크기가 허용 한도를 초과합니다.";
							return false;
						}
						dllBytes = ReadZipEntryBytes(entry, MaxAllowedDllSizeBytes);
					} else if (string.Equals(
						entryFileName,
						ModVersionConstants.MetadataFileName,
						StringComparison.OrdinalIgnoreCase
					)) {
						if (entry.Length <= MaxAllowedMetadataSizeBytes) {
							metadataBytes = ReadZipEntryBytes(entry, MaxAllowedMetadataSizeBytes);
						}
					}
				}
			} catch (InvalidDataException) {
				errorMessage = "다운로드된 ZIP 압축 파일이 손상되었습니다.";
				return false;
			} catch (IOException ioEx) {
				errorMessage = $"압축 해제 오류: {ioEx.Message}";
				return false;
			}

			if (dllBytes == null) {
				errorMessage = "릴리즈 ZIP 내부에 SephiriaOptimizer.dll 파일이 포함되어 있지 않습니다.";
				return false;
			}
			return true;
		}

		private static byte[] ReadZipEntryBytes(ZipArchiveEntry entry, int maxAllowedBytes) {
			using Stream entryStream = entry.Open();
			using MemoryStream output = new MemoryStream();
			byte[] buffer = new byte[8192];
			int totalRead = 0;

			while (true) {
				int readCount = entryStream.Read(buffer, 0, buffer.Length);
				if (readCount <= 0) {
					break;
				}
				totalRead += readCount;
				if (totalRead > maxAllowedBytes) {
					throw new InvalidDataException("ZIP entry exceeded maximum allowed size.");
				}
				output.Write(buffer, 0, readCount);
			}
			return output.ToArray();
		}

		private static byte[]? BuildUpdatedMetadataBytes(
			string modDirectory,
			byte[]? extractedMetadataBytes,
			string? targetVersion
		) {
			if (extractedMetadataBytes != null && extractedMetadataBytes.Length > 0) {
				if (string.IsNullOrWhiteSpace(targetVersion)) {
					return extractedMetadataBytes;
				}
				string extractedJson = Encoding.UTF8.GetString(extractedMetadataBytes);
				string patchedJson = VersionCompatibilityChecker.UpsertJsonStringField(
					extractedJson,
					"modVersion",
					targetVersion
				);
				return new UTF8Encoding(false).GetBytes(patchedJson);
			}

			string existingMetaPath = Path.Combine(modDirectory, ModVersionConstants.MetadataFileName);
			if (!File.Exists(existingMetaPath) || string.IsNullOrWhiteSpace(targetVersion)) {
				return null;
			}

			try {
				string existingJson = File.ReadAllText(existingMetaPath, Encoding.UTF8);
				string updatedJson = VersionCompatibilityChecker.UpsertJsonStringField(
					existingJson,
					"modVersion",
					targetVersion
				);
				return new UTF8Encoding(false).GetBytes(updatedJson);
			} catch (IOException) {
				return null;
			} catch (UnauthorizedAccessException) {
				return null;
			}
		}

		private static string ChooseAvailableBackupPath(string targetDllPath) {
			string primaryBackup = targetDllPath + ".old";
			if (!File.Exists(primaryBackup)) {
				return primaryBackup;
			}
			try {
				File.SetAttributes(primaryBackup, FileAttributes.Normal);
				File.Delete(primaryBackup);
				return primaryBackup;
			} catch (Exception deleteEx) {
				Trace.WriteLine($"[SephiriaOptimizer] Primary .old still mapped: {deleteEx.Message}");
			}

			string timestampSuffix = DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture);
			return targetDllPath + ".old." + timestampSuffix;
		}

		private static void WriteAllBytesFlushed(string filePath, byte[] content) {
			using FileStream stream = new FileStream(
				filePath,
				FileMode.Create,
				FileAccess.Write,
				FileShare.None
			);
			stream.Write(content, 0, content.Length);
			stream.Flush(true);
		}

		private static void TryRemoveZoneIdentifier(string filePath) {
			if (Path.DirectorySeparatorChar != '\\') {
				return;
			}
			try {
				File.Delete(filePath + ":Zone.Identifier");
			} catch (Exception adsEx) {
				Trace.WriteLine($"[SephiriaOptimizer] ADS cleanup skipped: {adsEx.Message}");
			}
		}

		private static void TryDeleteQuietly(string filePath) {
			if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) {
				return;
			}
			try {
				File.SetAttributes(filePath, FileAttributes.Normal);
				File.Delete(filePath);
			} catch (Exception cleanupEx) {
				Trace.WriteLine($"[SephiriaOptimizer] Temp file cleanup skipped: {cleanupEx.Message}");
			}
		}

		private static bool TrySchedulePostExitReplacement(
			string modDirectory,
			byte[] dllBytes,
			byte[]? metadataBytes
		) {
			try {
				string stageDir = Path.Combine(
					Path.GetTempPath(),
					"SephiriaOptimizer_Update_" + Guid.NewGuid().ToString("N")
				);
				Directory.CreateDirectory(stageDir);

				string stagedDll = Path.Combine(stageDir, ModVersionConstants.ModDllFileName);
				WriteAllBytesFlushed(stagedDll, dllBytes);
				if (metadataBytes != null) {
					string stagedMeta = Path.Combine(stageDir, ModVersionConstants.MetadataFileName);
					WriteAllBytesFlushed(stagedMeta, metadataBytes);
				}

				int currentPid = Process.GetCurrentProcess().Id;
				ProcessStartInfo startInfo;
				if (Path.DirectorySeparatorChar == '\\') {
					string psCommand =
						"try { Wait-Process -Id ([int]$env:SEPH_PID) -Timeout 600 "
						+ "-ErrorAction SilentlyContinue } catch {}; "
						+ "Start-Sleep -Milliseconds 400; "
						+ "Copy-Item -LiteralPath (Join-Path $env:SEPH_STAGE 'SephiriaOptimizer.dll') "
						+ "-Destination (Join-Path $env:SEPH_TARGET 'SephiriaOptimizer.dll') -Force; "
						+ "$m = Join-Path $env:SEPH_STAGE 'metadata.json'; "
						+ "if (Test-Path -LiteralPath $m) { "
						+ "Copy-Item -LiteralPath $m "
						+ "-Destination (Join-Path $env:SEPH_TARGET 'metadata.json') -Force }; "
						+ "Remove-Item -LiteralPath $env:SEPH_STAGE -Recurse -Force -ErrorAction SilentlyContinue";
					startInfo = new ProcessStartInfo {
						FileName = "powershell.exe",
						Arguments = "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -Command \""
							+ psCommand + "\"",
						CreateNoWindow = true,
						UseShellExecute = false
					};
				} else {
					string shCommand =
						"while kill -0 \"$SEPH_PID\" 2>/dev/null; do sleep 1; done; "
						+ "cp -f \"$SEPH_STAGE/SephiriaOptimizer.dll\" \"$SEPH_TARGET/SephiriaOptimizer.dll\"; "
						+ "if [ -f \"$SEPH_STAGE/metadata.json\" ]; then "
						+ "cp -f \"$SEPH_STAGE/metadata.json\" \"$SEPH_TARGET/metadata.json\"; fi; "
						+ "rm -rf \"$SEPH_STAGE\"";
					startInfo = new ProcessStartInfo {
						FileName = "/bin/sh",
						Arguments = "-c '" + shCommand + "'",
						CreateNoWindow = true,
						UseShellExecute = false
					};
				}

				startInfo.EnvironmentVariables["SEPH_PID"] = currentPid.ToString(CultureInfo.InvariantCulture);
				startInfo.EnvironmentVariables["SEPH_STAGE"] = stageDir;
				startInfo.EnvironmentVariables["SEPH_TARGET"] = modDirectory;

				Process? launched = Process.Start(startInfo);
				return launched != null;
			} catch (Exception) {
				return false;
			}
		}
	}
}
