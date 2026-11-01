using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace SephiriaOptimizer.Update {
	public static class ModVersionConstants {
		public const string ModName = "SephiriaOptimizer";
		public const string CurrentModVersion = "2.1.0";
		public const string DefaultTargetGameVersion = "1.0.33";
		public const string SteamAppId = "2436940";
		public const string SteamRunGameUri = "steam://rungameid/2436940";
		public const string ModDllFileName = "SephiriaOptimizer.dll";
		public const string MetadataFileName = "metadata.json";
		public const string GitHubLatestReleaseApiUrl =
			"https://api.github.com/repos/lij0825/SephiriaOptimizer/releases/latest";
		public const string GitHubReleasesPageUrl =
			"https://github.com/lij0825/SephiriaOptimizer/releases";
	}

	public enum GameCompatibilityStatus {
		Compatible = 0,
		GameVersionNewer = 1,
		GameVersionOlder = 2,
		VersionUnknown = 3,
		ApiIncompatible = 4
	}

	public readonly struct SemanticVersionInfo : IComparable<SemanticVersionInfo>, IEquatable<SemanticVersionInfo> {
		public int Major { get; }
		public int Minor { get; }
		public int Patch { get; }
		public int Revision { get; }
		public string PreReleaseTag { get; }

		public SemanticVersionInfo(
			int major,
			int minor,
			int patch,
			int revision = 0,
			string? preReleaseTag = null
		) {
			Major = major;
			Minor = minor;
			Patch = patch;
			Revision = revision;
			PreReleaseTag = preReleaseTag ?? string.Empty;
		}

		public static bool TryParse(string? rawText, out SemanticVersionInfo parsedVersion) {
			parsedVersion = default;
			if (string.IsNullOrWhiteSpace(rawText)) {
				return false;
			}

			string trimmed = rawText.Trim();
			if (trimmed.Length > 0 && (trimmed[0] == 'v' || trimmed[0] == 'V')) {
				trimmed = trimmed.Substring(1).Trim();
			}

			int plusIdx = trimmed.IndexOf('+');
			if (plusIdx >= 0) {
				trimmed = trimmed.Substring(0, plusIdx);
			}

			string preRelease = string.Empty;
			int dashIdx = trimmed.IndexOf('-');
			if (dashIdx >= 0) {
				preRelease = trimmed.Substring(dashIdx + 1).Trim();
				trimmed = trimmed.Substring(0, dashIdx).Trim();
			}

			if (trimmed.Length == 0) {
				return false;
			}

			string[] segments = trimmed.Split('.');
			if (segments.Length < 2 || segments.Length > 4) {
				return false;
			}

			int[] numbers = new int[4];
			for (int i = 0; i < segments.Length; i++) {
				if (!int.TryParse(
					segments[i],
					NumberStyles.None,
					CultureInfo.InvariantCulture,
					out int componentValue
				)) {
					return false;
				}
				numbers[i] = componentValue;
			}

			parsedVersion = new SemanticVersionInfo(
				numbers[0],
				numbers[1],
				numbers[2],
				numbers[3],
				preRelease
			);
			return true;
		}

		public int CompareTo(SemanticVersionInfo other) {
			int majorDiff = Major.CompareTo(other.Major);
			if (majorDiff != 0) {
				return majorDiff;
			}
			int minorDiff = Minor.CompareTo(other.Minor);
			if (minorDiff != 0) {
				return minorDiff;
			}
			int patchDiff = Patch.CompareTo(other.Patch);
			if (patchDiff != 0) {
				return patchDiff;
			}
			int revisionDiff = Revision.CompareTo(other.Revision);
			if (revisionDiff != 0) {
				return revisionDiff;
			}

			bool selfStable = string.IsNullOrEmpty(PreReleaseTag);
			bool otherStable = string.IsNullOrEmpty(other.PreReleaseTag);
			if (selfStable && otherStable) {
				return 0;
			}
			if (selfStable) {
				return 1;
			}
			if (otherStable) {
				return -1;
			}
			return StringComparer.OrdinalIgnoreCase.Compare(PreReleaseTag, other.PreReleaseTag);
		}

		public bool Equals(SemanticVersionInfo other) {
			return CompareTo(other) == 0;
		}

		public override bool Equals(object? obj) {
			return obj is SemanticVersionInfo other && Equals(other);
		}

		public override int GetHashCode() {
			int hash = 17;
			hash = (hash * 31) + Major;
			hash = (hash * 31) + Minor;
			hash = (hash * 31) + Patch;
			hash = (hash * 31) + Revision;
			hash = (hash * 31) + StringComparer.OrdinalIgnoreCase.GetHashCode(PreReleaseTag ?? string.Empty);
			return hash;
		}

		public string ToDisplayString() {
			StringBuilder builder = new StringBuilder(16);
			builder.Append(Major.ToString(CultureInfo.InvariantCulture));
			builder.Append('.');
			builder.Append(Minor.ToString(CultureInfo.InvariantCulture));
			builder.Append('.');
			builder.Append(Patch.ToString(CultureInfo.InvariantCulture));
			if (Revision > 0) {
				builder.Append('.');
				builder.Append(Revision.ToString(CultureInfo.InvariantCulture));
			}
			if (!string.IsNullOrEmpty(PreReleaseTag)) {
				builder.Append('-');
				builder.Append(PreReleaseTag);
			}
			return builder.ToString();
		}
	}

	public sealed class GameCompatibilityReport {
		public GameCompatibilityStatus Status { get; }
		public string ModVersion { get; }
		public string TargetGameVersion { get; }
		public string ActualGameVersion { get; }
		public bool IsReflectionApiValid { get; }
		public string MissingApiDetail { get; }
		public string BannerMessage { get; }

		public bool ShouldWarnUser => Status != GameCompatibilityStatus.Compatible;
		public bool ShouldLockAutoSwap => Status == GameCompatibilityStatus.ApiIncompatible;

		public GameCompatibilityReport(
			GameCompatibilityStatus status,
			string modVersion,
			string targetGameVersion,
			string actualGameVersion,
			bool isReflectionApiValid,
			string missingApiDetail,
			string bannerMessage
		) {
			Status = status;
			ModVersion = modVersion;
			TargetGameVersion = targetGameVersion;
			ActualGameVersion = actualGameVersion;
			IsReflectionApiValid = isReflectionApiValid;
			MissingApiDetail = missingApiDetail;
			BannerMessage = bannerMessage;
		}
	}

	public static class VersionCompatibilityChecker {
		public static string NormalizeVersionText(string? rawVersion) {
			if (SemanticVersionInfo.TryParse(rawVersion, out SemanticVersionInfo parsed)) {
				return parsed.ToDisplayString();
			}
			if (string.IsNullOrWhiteSpace(rawVersion)) {
				return string.Empty;
			}
			string trimmed = rawVersion.Trim();
			if (trimmed.Length > 0 && (trimmed[0] == 'v' || trimmed[0] == 'V')) {
				return trimmed.Substring(1).Trim();
			}
			return trimmed;
		}

		public static int CompareVersions(string? leftVersion, string? rightVersion) {
			bool hasLeft = SemanticVersionInfo.TryParse(leftVersion, out SemanticVersionInfo parsedLeft);
			bool hasRight = SemanticVersionInfo.TryParse(rightVersion, out SemanticVersionInfo parsedRight);
			if (hasLeft && hasRight) {
				return parsedLeft.CompareTo(parsedRight);
			}
			string normalizedLeft = NormalizeVersionText(leftVersion);
			string normalizedRight = NormalizeVersionText(rightVersion);
			return StringComparer.OrdinalIgnoreCase.Compare(normalizedLeft, normalizedRight);
		}

		public static bool IsNewerVersion(string? candidateVersion, string? currentVersion) {
			if (!SemanticVersionInfo.TryParse(candidateVersion, out SemanticVersionInfo parsedCandidate)) {
				return false;
			}
			if (!SemanticVersionInfo.TryParse(currentVersion, out SemanticVersionInfo parsedCurrent)) {
				return false;
			}
			return parsedCandidate.CompareTo(parsedCurrent) > 0;
		}

		public static string ResolveModDirectory(string? explicitDirectory = null) {
			if (!string.IsNullOrWhiteSpace(explicitDirectory)) {
				return Path.GetFullPath(explicitDirectory);
			}
			string assemblyLocation = typeof(VersionCompatibilityChecker).Assembly.Location;
			if (!string.IsNullOrWhiteSpace(assemblyLocation)) {
				string? assemblyDir = Path.GetDirectoryName(assemblyLocation);
				if (!string.IsNullOrWhiteSpace(assemblyDir)) {
					return assemblyDir;
				}
			}
			return AppDomain.CurrentDomain.BaseDirectory;
		}

		public static bool TryReadInstalledMetadata(
			string? modDirectory,
			out string modVersion,
			out string targetGameVersion
		) {
			modVersion = ModVersionConstants.CurrentModVersion;
			targetGameVersion = ModVersionConstants.DefaultTargetGameVersion;

			string resolvedDir = ResolveModDirectory(modDirectory);
			string metadataPath = Path.Combine(resolvedDir, ModVersionConstants.MetadataFileName);
			if (!File.Exists(metadataPath)) {
				return false;
			}

			try {
				string jsonContent = File.ReadAllText(metadataPath, Encoding.UTF8);
				if (TryExtractJsonStringField(jsonContent, "modVersion", out string extractedModVer)
					&& !string.IsNullOrWhiteSpace(extractedModVer)) {
					modVersion = NormalizeVersionText(extractedModVer);
				}
				if (TryExtractJsonStringField(jsonContent, "targetGameVersion", out string extractedGameVer)
					&& !string.IsNullOrWhiteSpace(extractedGameVer)) {
					targetGameVersion = NormalizeVersionText(extractedGameVer);
				}
				return true;
			} catch (IOException) {
				return false;
			} catch (UnauthorizedAccessException) {
				return false;
			}
		}

		public static bool TryExtractJsonStringField(
			string jsonText,
			string fieldName,
			out string fieldValue
		) {
			fieldValue = string.Empty;
			if (string.IsNullOrEmpty(jsonText) || string.IsNullOrEmpty(fieldName)) {
				return false;
			}

			string quotedKey = "\"" + fieldName + "\"";
			int searchOffset = 0;
			while (searchOffset < jsonText.Length) {
				int keyIdx = jsonText.IndexOf(quotedKey, searchOffset, StringComparison.Ordinal);
				if (keyIdx < 0) {
					return false;
				}

				int cursor = keyIdx + quotedKey.Length;
				while (cursor < jsonText.Length && char.IsWhiteSpace(jsonText[cursor])) {
					cursor++;
				}
				if (cursor >= jsonText.Length || jsonText[cursor] != ':') {
					searchOffset = cursor;
					continue;
				}
				cursor++;
				while (cursor < jsonText.Length && char.IsWhiteSpace(jsonText[cursor])) {
					cursor++;
				}
				if (cursor >= jsonText.Length || jsonText[cursor] != '"') {
					searchOffset = cursor;
					continue;
				}

				cursor++;
				StringBuilder valueBuilder = new StringBuilder(32);
				while (cursor < jsonText.Length) {
					char currentChar = jsonText[cursor];
					if (currentChar == '\\') {
						cursor++;
						if (cursor >= jsonText.Length) {
							return false;
						}
						char escapedChar = jsonText[cursor];
						if (escapedChar == '"' || escapedChar == '\\' || escapedChar == '/') {
							valueBuilder.Append(escapedChar);
						} else if (escapedChar == 'n') {
							valueBuilder.Append('\n');
						} else if (escapedChar == 'r') {
							valueBuilder.Append('\r');
						} else if (escapedChar == 't') {
							valueBuilder.Append('\t');
						} else {
							valueBuilder.Append(escapedChar);
						}
						cursor++;
						continue;
					}
					if (currentChar == '"') {
						fieldValue = valueBuilder.ToString();
						return true;
					}
					valueBuilder.Append(currentChar);
					cursor++;
				}
				return false;
			}
			return false;
		}

		public static string UpsertJsonStringField(string jsonText, string fieldName, string newValue) {
			string escapedValue = (newValue ?? string.Empty)
				.Replace("\\", "\\\\")
				.Replace("\"", "\\\"");
			string quotedKey = "\"" + fieldName + "\"";

			if (string.IsNullOrWhiteSpace(jsonText)) {
				return "{\n\t" + quotedKey + ": \"" + escapedValue + "\"\n}\n";
			}

			int keyIdx = jsonText.IndexOf(quotedKey, StringComparison.Ordinal);
			if (keyIdx >= 0) {
				int colonIdx = jsonText.IndexOf(':', keyIdx + quotedKey.Length);
				if (colonIdx >= 0) {
					int openQuote = jsonText.IndexOf('"', colonIdx + 1);
					if (openQuote >= 0) {
						int closeQuote = openQuote + 1;
						while (closeQuote < jsonText.Length) {
							if (jsonText[closeQuote] == '\\') {
								closeQuote += 2;
								continue;
							}
							if (jsonText[closeQuote] == '"') {
								return jsonText.Substring(0, openQuote + 1)
									+ escapedValue
									+ jsonText.Substring(closeQuote);
							}
							closeQuote++;
						}
					}
				}
			}

			int lastBrace = jsonText.LastIndexOf('}');
			if (lastBrace > 0) {
				string prefix = jsonText.Substring(0, lastBrace).TrimEnd();
				string separator = prefix.EndsWith("{", StringComparison.Ordinal) ? "\n\t" : ",\n\t";
				return prefix + separator + quotedKey + ": \"" + escapedValue + "\"\n}\n";
			}
			return jsonText;
		}

		public static bool VerifyCoreGameApi(
			out string missingMemberDescription,
			Assembly? targetGameAssembly = null
		) {
			BindingFlags publicInstance = BindingFlags.Public | BindingFlags.Instance;

			try {
				Type? itemPosType = FindGameType("ItemPosition", targetGameAssembly);
				if (itemPosType == null) {
					missingMemberDescription = "ItemPosition";
					return false;
				}

				Type? gridType = FindGameType("GridInventory", targetGameAssembly);
				if (gridType == null) {
					missingMemberDescription = "GridInventory";
					return false;
				}
				if (gridType.GetField("CurrentInventoryStorage", publicInstance) == null) {
					missingMemberDescription = "GridInventory.CurrentInventoryStorage";
					return false;
				}
				if (gridType.GetField("Width", publicInstance) == null) {
					missingMemberDescription = "GridInventory.Width";
					return false;
				}
				if (gridType.GetField("mysticPositions", publicInstance) == null) {
					missingMemberDescription = "GridInventory.mysticPositions";
					return false;
				}
				if (gridType.GetMethod(
					"FindItem",
					publicInstance,
					null,
					new[] { typeof(sbyte), typeof(sbyte) },
					null
				) == null) {
					missingMemberDescription = "GridInventory.FindItem(sbyte, sbyte)";
					return false;
				}
				if (gridType.GetMethod(
					"Swap",
					publicInstance,
					null,
					new[] { typeof(sbyte), typeof(sbyte), typeof(sbyte), typeof(sbyte) },
					null
				) == null) {
					missingMemberDescription = "GridInventory.Swap(sbyte, sbyte, sbyte, sbyte)";
					return false;
				}
				if (gridType.GetMethod(
					"DoClickAction",
					publicInstance,
					null,
					new[] { itemPosType },
					null
				) == null) {
					missingMemberDescription = "GridInventory.DoClickAction(ItemPosition)";
					return false;
				}

				Type? panelType = FindGameType("UI_CharacterStatusPanel", targetGameAssembly);
				if (panelType == null) {
					missingMemberDescription = "UI_CharacterStatusPanel";
					return false;
				}
				if (panelType.GetMethod(
					"GetItemIcon",
					publicInstance,
					null,
					new[] { itemPosType },
					null
				) == null) {
					missingMemberDescription = "UI_CharacterStatusPanel.GetItemIcon(ItemPosition)";
					return false;
				}
				if (panelType.GetProperty("InventoryMode", publicInstance) == null) {
					missingMemberDescription = "UI_CharacterStatusPanel.InventoryMode";
					return false;
				}
				if (panelType.GetField("potionZone", publicInstance) == null) {
					missingMemberDescription = "UI_CharacterStatusPanel.potionZone";
					return false;
				}
				if (panelType.GetField("inventoryZone", publicInstance) == null) {
					missingMemberDescription = "UI_CharacterStatusPanel.inventoryZone";
					return false;
				}

				Type? tabletType = FindGameType("StoneTablet", targetGameAssembly);
				if (tabletType == null || tabletType.GetField("rotation", publicInstance) == null) {
					missingMemberDescription = "StoneTablet.rotation";
					return false;
				}

				if (FindGameType("Charm_Basic", targetGameAssembly) == null) {
					missingMemberDescription = "Charm_Basic";
					return false;
				}
			} catch (Exception reflectionEx) {
				missingMemberDescription = reflectionEx.GetType().Name;
				return false;
			}

			missingMemberDescription = string.Empty;
			return true;
		}

		private static Type? FindGameType(string fullTypeName, Assembly? explicitAssembly) {
			if (explicitAssembly != null) {
				return explicitAssembly.GetType(fullTypeName, false, false);
			}

			Type? directMatch = Type.GetType(fullTypeName + ", Assembly-CSharp", false, false);
			if (directMatch != null) {
				return directMatch;
			}

			Assembly[] loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
			for (int i = 0; i < loadedAssemblies.Length; i++) {
				Type? candidate = loadedAssemblies[i].GetType(fullTypeName, false, false);
				if (candidate != null) {
					return candidate;
				}
			}
			return null;
		}

		public static GameCompatibilityReport EvaluateCompatibility(
			string? actualGameVersion,
			string? targetGameVersion,
			string? modVersion = null,
			bool verifyReflectionApi = true,
			Assembly? targetGameAssembly = null
		) {
			string resolvedModVer = string.IsNullOrWhiteSpace(modVersion)
				? ModVersionConstants.CurrentModVersion
				: NormalizeVersionText(modVersion);
			string resolvedTargetVer = string.IsNullOrWhiteSpace(targetGameVersion)
				? ModVersionConstants.DefaultTargetGameVersion
				: NormalizeVersionText(targetGameVersion);
			string resolvedActualVer = NormalizeVersionText(actualGameVersion);

			string missingMember = string.Empty;
			bool apiValid = !verifyReflectionApi || VerifyCoreGameApi(out missingMember, targetGameAssembly);
			if (!apiValid) {
				string apiMsg = $"게임 내부 API 변경 감지 ({missingMember}) — 안전을 위해 자동 스왑이 잠겼습니다.";
				return new GameCompatibilityReport(
					GameCompatibilityStatus.ApiIncompatible,
					resolvedModVer,
					resolvedTargetVer,
					resolvedActualVer,
					false,
					missingMember,
					apiMsg
				);
			}

			if (string.IsNullOrWhiteSpace(resolvedActualVer)) {
				return new GameCompatibilityReport(
					GameCompatibilityStatus.VersionUnknown,
					resolvedModVer,
					resolvedTargetVer,
					"알 수 없음",
					true,
					string.Empty,
					$"게임 버전을 확인할 수 없습니다 (모드 기준 v{resolvedTargetVer})."
				);
			}

			int versionComparison = CompareVersions(resolvedActualVer, resolvedTargetVer);
			if (versionComparison == 0) {
				return new GameCompatibilityReport(
					GameCompatibilityStatus.Compatible,
					resolvedModVer,
					resolvedTargetVer,
					resolvedActualVer,
					true,
					string.Empty,
					string.Empty
				);
			}

			if (versionComparison > 0) {
				string newerMsg =
					$"게임 버전 다름: 현재 v{resolvedActualVer} (모드 기준 v{resolvedTargetVer})";
				return new GameCompatibilityReport(
					GameCompatibilityStatus.GameVersionNewer,
					resolvedModVer,
					resolvedTargetVer,
					resolvedActualVer,
					true,
					string.Empty,
					newerMsg
				);
			}

			string olderMsg =
				$"구버전 게임 감지: 현재 v{resolvedActualVer} (권장 v{resolvedTargetVer} 이상)";
			return new GameCompatibilityReport(
				GameCompatibilityStatus.GameVersionOlder,
				resolvedModVer,
				resolvedTargetVer,
				resolvedActualVer,
				true,
				string.Empty,
				olderMsg
			);
		}

		public static GameCompatibilityReport EvaluateRuntimeCompatibility(string? modDirectory = null) {
			TryReadInstalledMetadata(modDirectory, out string modVersion, out string targetGameVersion);
			string runtimeGameVersion = Application.version;
			return EvaluateCompatibility(runtimeGameVersion, targetGameVersion, modVersion, true);
		}
	}
}
