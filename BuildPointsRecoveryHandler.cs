using UnityEngine;

namespace BuildPoints
{
	/// <summary>
	/// Refunds Build Points when a vessel is recovered. Cost is computed
	/// with the same formula used to charge the vessel at launch
	/// (BuildPointsCalculator.TryGetRecoveredVesselCost)
	/// minus the constant cost per launch, then scaled by
	/// this save's "Recovery refund (%)" setting. onVesselRecovered fires
	/// for both the full recovery dialog and "quick recover"
	/// (right-clicking a landed/splashed vessel near the Space Center), so
	/// both are covered without extra hooks.
	///
	/// </summary>
	[KSPAddon(KSPAddon.Startup.EveryScene, true)]
	public class BuildPointsRecoveryHandler : MonoBehaviour
	{
		private static bool subscribed;

		public void Awake()
		{
			if (subscribed) { Destroy(gameObject); return; }
			subscribed = true;
			DontDestroyOnLoad(gameObject);
			GameEvents.onVesselRecovered.Add(OnVesselRecovered);
		}

		public void OnDestroy()
		{
			GameEvents.onVesselRecovered.Remove(OnVesselRecovered);
		}

		private void OnVesselRecovered(ProtoVessel protoVessel, bool quick)
		{
			if (!BuildPointsScenario.IsActiveForCurrentGame()) return;

			var scenario = BuildPointsScenario.Instance;
			if (scenario == null) return;

			var settings = scenario.Settings;
			if (settings.recoveryRefundPercent <= 0f) return;

			if (!BuildPointsCalculator.TryGetRecoveredVesselCost(protoVessel, out double bpCost, out _, out _))
				return;

			double refund = bpCost * (settings.recoveryRefundPercent / 100.0);
			if (refund <= 0) return;

			scenario.Accrue(refund);

			ScreenMessages.PostScreenMessage(
				$"Build Points refunded: +{refund:0.0} BP",
				4f, ScreenMessageStyle.UPPER_CENTER);
		}
	}
}
