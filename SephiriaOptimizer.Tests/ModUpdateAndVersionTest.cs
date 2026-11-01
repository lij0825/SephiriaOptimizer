using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using SephiriaOptimizer.Update;
using Xunit;

namespace SephiriaOptimizer.Tests {
	public sealed class ModUpdateAndVersionTest {
		[Fact(DisplayName = "버전 문자열에 v 접두사나 프리릴리즈 태그가 포함되어 있어도 시맨틱 버전 우선순위를 정확히 비교한다")]
		public void CompareVersions_HandlesPrefixNumericSegmentsAndPrereleaseOrder() {
			int sameWithPrefix = VersionCompatibilityChecker.CompareVersions("v1.0.33", "1.0.33");
			int patchHigher = VersionCompatibilityChecker.CompareVersions("1.0.34", "1.0.33");
			int minorHigher = VersionCompatibilityChecker.CompareVersions("v2.2.0", "2.1.9");
			int stableOverRc = VersionCompatibilityChecker.CompareVersions("2.2.0", "2.2.0-rc.1");
			bool isNewerRelease = VersionCompatibilityChecker.IsNewerVersion("v2.2.0", "2.1.0");
			bool isSameReleaseNewer = VersionCompatibilityChecker.IsNewerVersion("v2.1.0", "2.1.0");

			Assert.Equal(0, sameWithPrefix);
			Assert.True(patchHigher > 0);
			Assert.True(minorHigher > 0);
			Assert.True(stableOverRc > 0);
			Assert.True(isNewerRelease);
			Assert.False(isSameReleaseNewer);
		}

		[Fact(DisplayName = "게임 런타임 버전과 기준 버전 대조 시 일치·신규 패치·구버전 상태 및 API 불일치 안전 잠금을 정확히 판정한다")]
		public void EvaluateCompatibility_DetectsVersionMatchMismatchAndLocksOnApiChange() {
			GameCompatibilityReport exactMatch = VersionCompatibilityChecker.EvaluateCompatibility(
				"1.0.33",
				"1.0.33",
				"2.1.0",
				false
			);
			GameCompatibilityReport newerGame = VersionCompatibilityChecker.EvaluateCompatibility(
				"1.0.35",
				"1.0.33",
				"2.1.0",
				false
			);
			GameCompatibilityReport olderGame = VersionCompatibilityChecker.EvaluateCompatibility(
				"1.0.31",
				"1.0.33",
				"2.1.0",
				false
			);
			GameCompatibilityReport brokenApi = VersionCompatibilityChecker.EvaluateCompatibility(
				"1.0.33",
				"1.0.33",
				"2.1.0",
				true,
				typeof(ModUpdateAndVersionTest).Assembly
			);

			Assert.Equal(GameCompatibilityStatus.Compatible, exactMatch.Status);
			Assert.False(exactMatch.ShouldWarnUser);
			Assert.False(exactMatch.ShouldLockAutoSwap);

			Assert.Equal(GameCompatibilityStatus.GameVersionNewer, newerGame.Status);
			Assert.True(newerGame.ShouldWarnUser);
			Assert.False(newerGame.ShouldLockAutoSwap);
			Assert.Contains("1.0.35", newerGame.BannerMessage, StringComparison.Ordinal);

			Assert.Equal(GameCompatibilityStatus.GameVersionOlder, olderGame.Status);
			Assert.True(olderGame.ShouldWarnUser);
			Assert.False(olderGame.ShouldLockAutoSwap);

			Assert.Equal(GameCompatibilityStatus.ApiIncompatible, brokenApi.Status);
			Assert.True(brokenApi.ShouldWarnUser);
			Assert.True(brokenApi.ShouldLockAutoSwap);

			Assembly? realGameAsm = TryLoadLocalGameAssemblyForVerification();
			if (realGameAsm != null) {
				bool apiHealthy = VersionCompatibilityChecker.VerifyCoreGameApi(
					out string missingApi,
					realGameAsm
				);
				Assert.True(apiHealthy, $"Missing real game member: {missingApi}");
			}
		}

		[Fact(DisplayName = "GitHub 최신 릴리즈 JSON 파싱 시 중첩 객체 URL에 오염되지 않고 최상위 태그와 공식 ZIP 에셋을 우선 선택한다")]
		public void TryParseLatestRelease_ExtractsTopLevelTagAndPrefersModZipAsset() {
			string dllUrl = "https://github.com/lij0825/SephiriaOptimizer"
				+ "/releases/download/v2.2.0/SephiriaOptimizer.dll";
			string zipUrl = "https://objects.githubusercontent.com"
				+ "/github-production-release-asset/SephiriaOptimizer_v2.2.0.zip";
			string sampleGitHubJson = "{\n"
				+ "\t\"url\": \"https://api.github.com/repos/lij0825/SephiriaOptimizer/releases/12345\",\n"
				+ "\t\"author\": { \"login\": \"lij0825\", \"html_url\": \"https://github.com/lij0825\" },\n"
				+ "\t\"tag_name\": \"v2.2.0\",\n"
				+ "\t\"name\": \"SephiriaOptimizer v2.2.0\",\n"
				+ "\t\"draft\": false,\n"
				+ "\t\"prerelease\": false,\n"
				+ "\t\"html_url\": \"https://github.com/lij0825/SephiriaOptimizer/releases/tag/v2.2.0\",\n"
				+ "\t\"body\": \"## 변경 사항\\n- \\\"tag_name\\\": \\\"v9.9.9\\\" 무시\\n- { 중괄호 }\",\n"
				+ "\t\"assets\": [\n"
				+ "\t\t{ \"name\": \"SephiriaOptimizer.dll\", \"size\": 98304, "
				+ "\"browser_download_url\": \"" + dllUrl + "\" },\n"
				+ "\t\t{ \"name\": \"SephiriaOptimizer_v2.2.0.zip\", \"size\": 112640, "
				+ "\"browser_download_url\": \"" + zipUrl + "\" }\n"
				+ "\t]\n"
				+ "}";

			bool parsed = GitHubReleaseParser.TryParseLatestRelease(
				sampleGitHubJson,
				out GitHubReleaseInfo? release
			);

			Assert.True(parsed);
			Assert.NotNull(release);
			Assert.Equal("v2.2.0", release!.TagName);
			Assert.Equal("2.2.0", release.NormalizedVersion);
			Assert.False(release.IsDraft);
			Assert.False(release.IsPrerelease);
			Assert.Equal(
				"https://github.com/lij0825/SephiriaOptimizer/releases/tag/v2.2.0",
				release.HtmlUrl
			);
			Assert.True(release.HasDownloadableAsset);
			Assert.Equal("SephiriaOptimizer_v2.2.0.zip", release.PreferredAssetName);
			Assert.Contains("githubusercontent.com", release.PreferredAssetUrl, StringComparison.Ordinal);
		}

		[Fact(DisplayName = "비암호화 HTTP 프로토콜이나 허용되지 않은 외부 도메인 URL은 보안 검증에서 즉시 차단된다")]
		public void IsTrustedGitHubUrl_AllowsOnlyOfficialHttpsGitHubDomains() {
			bool validRepoUrl = GitHubReleaseParser.IsTrustedGitHubUrl(
				"https://github.com/lij0825/SephiriaOptimizer/releases/download/v2.2.0/SephiriaOptimizer_v2.2.0.zip"
			);
			bool validObjectsUrl = GitHubReleaseParser.IsTrustedGitHubUrl(
				"https://objects.githubusercontent.com/github-production-release-asset/asset.zip"
			);
			bool validReleaseAssetsUrl = GitHubReleaseParser.IsTrustedGitHubUrl(
				"https://release-assets.githubusercontent.com/asset.zip"
			);
			bool plainHttpRejected = GitHubReleaseParser.IsTrustedGitHubUrl(
				"http://github.com/lij0825/SephiriaOptimizer/releases/download/v2.2.0/asset.zip"
			);
			bool spoofedDomainRejected = GitHubReleaseParser.IsTrustedGitHubUrl(
				"https://evil-github.com/lij0825/asset.zip"
			);
			bool fileSchemeRejected = GitHubReleaseParser.IsTrustedGitHubUrl(
				"file:///C:/Temp/SephiriaOptimizer.zip"
			);

			Assert.True(validRepoUrl);
			Assert.True(validObjectsUrl);
			Assert.True(validReleaseAssetsUrl);
			Assert.False(plainHttpRejected);
			Assert.False(spoofedDomainRejected);
			Assert.False(fileSchemeRejected);
		}

		[Fact(DisplayName = "유효한 릴리즈 ZIP 적용 시 기존 DLL을 old로 백업하고 새 DLL과 metadata.json을 원자적으로 교체한 뒤 다음 시작 시 정리한다")]
		public void ApplyDownloadedPayload_PerformsAtomicRenameSwapAndCleansBackupOnStartup() {
			string tempModDir = Path.Combine(
				Path.GetTempPath(),
				"SephiriaOptimizer_Test_" + Guid.NewGuid().ToString("N")
			);
			Directory.CreateDirectory(tempModDir);

			try {
				byte[] oldDllBytes = CreateSyntheticValidPeDll(0x11, 8192);
				byte[] newDllBytes = CreateSyntheticValidPeDll(0x22, 10240);
				string dllPath = Path.Combine(tempModDir, "SephiriaOptimizer.dll");
				string metaPath = Path.Combine(tempModDir, "metadata.json");

				string oldMetaJson = "{\n\t\"modName\": \"SephiriaOptimizer\",\n"
					+ "\t\"modVersion\": \"2.1.0\",\n\t\"targetGameVersion\": \"1.0.33\"\n}\n";
				string newMetaJson = "{\n\t\"modName\": \"SephiriaOptimizer\",\n"
					+ "\t\"modVersion\": \"2.2.0\",\n\t\"targetGameVersion\": \"1.0.34\"\n}\n";
				File.WriteAllBytes(dllPath, oldDllBytes);
				File.WriteAllText(metaPath, oldMetaJson, new UTF8Encoding(false));

				byte[] zipPayload = CreateReleaseZipPayload(newDllBytes, newMetaJson);

				UpdateApplyResult result = GitHubReleaseUpdater.ApplyDownloadedPayload(
					zipPayload,
					"SephiriaOptimizer_v2.2.0.zip",
					tempModDir,
					"2.2.0",
					false
				);

				string backupDllPath = dllPath + ".old";
				bool metaReadOk = VersionCompatibilityChecker.TryReadInstalledMetadata(
					tempModDir,
					out string updatedModVer,
					out string updatedTargetGameVer
				);

				Assert.Equal(UpdateApplyOutcome.AppliedInPlacePendingRestart, result.Outcome);
				Assert.True(File.Exists(dllPath));
				Assert.True(File.Exists(backupDllPath));
				Assert.Equal(newDllBytes, File.ReadAllBytes(dllPath));
				Assert.Equal(oldDllBytes, File.ReadAllBytes(backupDllPath));
				Assert.True(metaReadOk);
				Assert.Equal("2.2.0", updatedModVer);
				Assert.Equal("1.0.34", updatedTargetGameVer);

				int cleanedCount = GitHubReleaseUpdater.CleanupOldBackupFiles(tempModDir);
				Assert.Equal(1, cleanedCount);
				Assert.False(File.Exists(backupDllPath));
			} finally {
				if (Directory.Exists(tempModDir)) {
					Directory.Delete(tempModDir, true);
				}
			}
		}

		[Fact(DisplayName = "PE 헤더가 없는 손상된 바이너리나 HTML 응답은 기존 모드 DLL을 변경하지 않고 즉시 거부한다")]
		public void ApplyDownloadedPayload_RejectsInvalidPeBinaryWithoutTouchingInstalledDll() {
			string tempModDir = Path.Combine(
				Path.GetTempPath(),
				"SephiriaOptimizer_InvalidTest_" + Guid.NewGuid().ToString("N")
			);
			Directory.CreateDirectory(tempModDir);

			try {
				byte[] existingDllBytes = CreateSyntheticValidPeDll(0x55, 8192);
				string dllPath = Path.Combine(tempModDir, "SephiriaOptimizer.dll");
				File.WriteAllBytes(dllPath, existingDllBytes);

				byte[] fakeHtmlError = Encoding.UTF8.GetBytes("<html><body>502 Bad Gateway</body></html>");
				UpdateApplyResult htmlResult = GitHubReleaseUpdater.ApplyDownloadedPayload(
					fakeHtmlError,
					"SephiriaOptimizer.dll",
					tempModDir,
					"2.2.0",
					false
				);

				byte[] zipWithCorruptDll = CreateReleaseZipPayload(new byte[6000], null);
				UpdateApplyResult corruptZipResult = GitHubReleaseUpdater.ApplyDownloadedPayload(
					zipWithCorruptDll,
					"SephiriaOptimizer_v2.2.0.zip",
					tempModDir,
					"2.2.0",
					false
				);

				Assert.Equal(UpdateApplyOutcome.Failed, htmlResult.Outcome);
				Assert.Equal(UpdateApplyOutcome.Failed, corruptZipResult.Outcome);
				Assert.Equal(existingDllBytes, File.ReadAllBytes(dllPath));
				Assert.False(File.Exists(dllPath + ".old"));
			} finally {
				if (Directory.Exists(tempModDir)) {
					Directory.Delete(tempModDir, true);
				}
			}
		}

		private static byte[] CreateSyntheticValidPeDll(byte fillPattern, int totalLength) {
			byte[] buffer = new byte[totalLength];
			for (int i = 0; i < buffer.Length; i++) {
				buffer[i] = fillPattern;
			}
			buffer[0] = 0x4D;
			buffer[1] = 0x5A;

			int peOffset = 0x80;
			byte[] offsetBytes = BitConverter.GetBytes(peOffset);
			Buffer.BlockCopy(offsetBytes, 0, buffer, 0x3C, 4);

			buffer[peOffset] = 0x50;
			buffer[peOffset + 1] = 0x45;
			buffer[peOffset + 2] = 0x00;
			buffer[peOffset + 3] = 0x00;
			return buffer;
		}

		private static byte[] CreateReleaseZipPayload(byte[] dllContent, string? metadataJson) {
			using MemoryStream memory = new MemoryStream();
			using (ZipArchive archive = new ZipArchive(memory, ZipArchiveMode.Create, true)) {
				ZipArchiveEntry dllEntry = archive.CreateEntry("SephiriaOptimizer.dll");
				using (Stream dllStream = dllEntry.Open()) {
					dllStream.Write(dllContent, 0, dllContent.Length);
				}
				if (!string.IsNullOrEmpty(metadataJson)) {
					ZipArchiveEntry metaEntry = archive.CreateEntry("metadata.json");
					byte[] metaBytes = new UTF8Encoding(false).GetBytes(metadataJson);
					using Stream metaStream = metaEntry.Open();
					metaStream.Write(metaBytes, 0, metaBytes.Length);
				}
			}
			return memory.ToArray();
		}

		private static Assembly? TryLoadLocalGameAssemblyForVerification() {
			string? currentDir = AppDomain.CurrentDomain.BaseDirectory;
			while (!string.IsNullOrEmpty(currentDir)) {
				string cacheFile = Path.Combine(currentDir, ".sephiria-path");
				if (File.Exists(cacheFile)) {
					string gameRoot = File.ReadAllText(cacheFile, Encoding.UTF8).Trim();
					string managedDir = Path.Combine(gameRoot, "Sephiria_Data", "Managed");
					string asmCSharpPath = Path.Combine(managedDir, "Assembly-CSharp.dll");
					if (File.Exists(asmCSharpPath)) {
						AppDomain.CurrentDomain.AssemblyResolve += (_, resolveArgs) => {
							string simpleName = new AssemblyName(resolveArgs.Name).Name ?? string.Empty;
							string candidateDll = Path.Combine(managedDir, simpleName + ".dll");
							return File.Exists(candidateDll) ? Assembly.LoadFrom(candidateDll) : null;
						};
						return Assembly.LoadFrom(asmCSharpPath);
					}
					break;
				}
				currentDir = Directory.GetParent(currentDir)?.FullName;
			}
			return null;
		}
	}
}
