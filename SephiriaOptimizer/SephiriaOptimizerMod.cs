using SephiriaOptimizer.UI;
using SephiriaOptimizer.Update;
using UnityEngine;

namespace SephiriaOptimizer {
	public sealed class SephiriaOptimizerMod : HorayModBase {
		private static GameObject? hostGameObject;

		protected override void OnModLoaded() {
			base.OnModLoaded();
			if (hostGameObject != null) {
				return;
			}

			string modDirectory = VersionCompatibilityChecker.ResolveModDirectory();
			int removedBackups = GitHubReleaseUpdater.CleanupOldBackupFiles(modDirectory);
			if (removedBackups > 0) {
				Debug.Log($"[SephiriaOptimizer] Cleaned up {removedBackups} previous update backup file(s).");
			}

			hostGameObject = new GameObject("SephiriaOptimizer_Host");
			Object.DontDestroyOnLoad(hostGameObject);
			SephiriaOptimizerController controller = hostGameObject.AddComponent<SephiriaOptimizerController>();
			controller.InitializeModContext(metadata.modVersion, modDirectory);
			Debug.Log($"[SephiriaOptimizer] v{ModVersionConstants.CurrentModVersion} initialized.");
		}

		protected override void OnModUnloaded() {
			if (hostGameObject != null) {
				Object.Destroy(hostGameObject);
				hostGameObject = null;
			}
			base.OnModUnloaded();
		}
	}
}
