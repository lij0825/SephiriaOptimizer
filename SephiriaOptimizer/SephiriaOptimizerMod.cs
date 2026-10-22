using SephiriaOptimizer.UI;
using UnityEngine;

namespace SephiriaOptimizer {
	public sealed class SephiriaOptimizerMod : HorayModBase {
		private static GameObject? hostGameObject;

		protected override void OnModLoaded() {
			base.OnModLoaded();
			if (hostGameObject != null) {
				return;
			}
			hostGameObject = new GameObject("SephiriaOptimizer_Host");
			Object.DontDestroyOnLoad(hostGameObject);
			hostGameObject.AddComponent<SephiriaOptimizerController>();
			Debug.Log("[SephiriaOptimizer] Hybrid Auto-Swap & Overlay Guide Mod initialized.");
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
