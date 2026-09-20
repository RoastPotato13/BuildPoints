using System.Collections.Generic;

namespace BuildPoints
{
	/// <summary>
	/// Breakdown of a vessel's Build Points cost by component, so UI (the
	/// VAB/SPH toolbar window) can show each contributor separately instead
	/// of just the total. fundsCost/massTonnes are the vessel's raw funds
	/// cost and mass (not yet converted to BP) — useful for display.
	/// </summary>
	public struct BuildPointsCostBreakdown
	{
		public double constant;       // BP, from settings.constantCost directly
		public double funds;          // BP, from fundsCost * fundsCostWeight
		public double partCountCost;  // BP, from partCount * costPerPart
		public double mass;           // BP, from massTonnes * massCostWeight
		public double fundsCost;      // raw vessel funds cost (dry + resources)
		public double massTonnes;     // raw vessel mass (dry + resources)
		public int partCount;
		public double total;          // sum of the four BP components, floored at minimumCraftCost
	}

	/// <summary>
	/// Computes the Build Points cost of a vessel — either the one
	/// currently in the editor (charged on launch) or a just-recovered
	/// one (refunded on recovery). Both paths share the same cost
	/// formula and the same part-summing helper so they can never drift
	/// out of sync with each other. Both also read this save's settings
	/// via BuildPointsScenario.GetActiveSettings() rather than the global
	/// defaults, so the cost formula respects whatever the player set for
	/// this game.
	/// </summary>
	public static class BuildPointsCalculator
	{
		public static bool TryGetCurrentShipCost(out double bpCost, out double fundsCost, out int partCount)
		{
			bpCost = 0;
			fundsCost = 0;
			partCount = 0;

			if (!TryGetShipCostBreakdown(out BuildPointsCostBreakdown breakdown)) return false;

			bpCost = breakdown.total;
			fundsCost = breakdown.fundsCost;
			partCount = breakdown.partCount;
			return true;
		}

		/// <summary>
		/// Same as TryGetCurrentShipCost, but returns every component of
		/// the cost formula separately (used by the VAB/SPH "Build Points
		/// Cost" toolbar window).
		/// </summary>
		public static bool TryGetShipCostBreakdown(out BuildPointsCostBreakdown breakdown)
		{
			breakdown = default;

			if (!HighLogic.LoadedSceneIsEditor || EditorLogic.fetch == null || EditorLogic.fetch.ship == null)
				return false;

			var ship = EditorLogic.fetch.ship;
			int partCount = ship.parts.Count;
			if (partCount == 0) return false;

			var availableParts = new List<AvailablePart>(partCount);
			foreach (Part part in ship.parts)
			{
				if (part.partInfo == null) continue; // defensive: shouldn't happen for a placed part
				availableParts.Add(part.partInfo);
			}

			SumPartCostsAndMass(availableParts, out double fundsCost, out double massTonnes);

			// In TryGetShipCostBreakdown (launch / editor):
			breakdown = BuildBreakdown(BuildPointsScenario.GetActiveSettings(), fundsCost, partCount, massTonnes,
			chargeLaunchOverhead: true);
			return true;
		}

		/// <summary>
		/// Same per-part / per-funds / per-mass formula as TryGetCurrentShipCost,
		/// but WITHOUT the constant per-launch cost or the minimum-cost floor,
		/// applied to a recovered vessel's ProtoVessel rather than a live editor
		/// ShipConstruct — used to size the Build Points refund on
		/// recovery. Like the editor path, this sums cost/mass off each
		/// part's AvailablePart template (partInfo.partConfig) rather
		/// than that part's actual persisted resource levels — so it's
		/// consistent with the launch charge, but inherits the same
		/// "ignores partial fuel" simplification already flagged for
		/// GetPartCostsAndMass in the README.
		///
		/// NOTE: verify ProtoPartSnapshot.partInfo against your KSP
		/// version — it should be the same AvailablePart reference the
		/// live Part exposes, populated when the vessel is loaded/recovered.
		/// A part whose mod was removed since launch will have this null;
		/// such parts are skipped rather than failing the whole refund.
		/// </summary>
		public static bool TryGetRecoveredVesselCost(ProtoVessel protoVessel, out double bpCost, out double fundsCost, out int partCount)
		{
			bpCost = 0;
			fundsCost = 0;
			partCount = 0;

			if (protoVessel?.protoPartSnapshots == null) return false;
			partCount = protoVessel.protoPartSnapshots.Count;
			if (partCount == 0) return false;

			var availableParts = new List<AvailablePart>(partCount);
			foreach (ProtoPartSnapshot pps in protoVessel.protoPartSnapshots)
			{
				if (pps?.partInfo == null) continue;
				availableParts.Add(pps.partInfo);
			}
			if (availableParts.Count == 0) return false;

			SumPartCostsAndMass(availableParts, out fundsCost, out double massTonnes);

			// In TryGetRecoveredVesselCost (recovery):
			var breakdown = BuildBreakdown(BuildPointsScenario.GetActiveSettings(), fundsCost, partCount, massTonnes,
				chargeLaunchOverhead: false);
			bpCost = breakdown.total;
			return true;
		}

		/// <summary>
		/// cost = constantCost + (fundsCost * fundsCostWeight)
		///        + (partCount * costPerPart) + (massTonnes * massCostWeight),
		/// floored at minimumCraftCost.
		///
		/// chargeLaunchOverhead = true (launch): the full formula above.
		/// chargeLaunchOverhead = false (recovery): constantCost and the
		/// minimumCraftCost floor are both skipped. They're per-launch charges,
		/// and a craft that breaks into several recovered pieces would otherwise
		/// refund them once per piece. Only the per-part, per-funds and per-mass
		/// terms scale with what's actually being recovered.
		/// The refund handler then multiplies the result by recoveryRefundPercent.
		/// </summary>
		private static BuildPointsCostBreakdown BuildBreakdown(BuildPointsSettingsValues settings,
			double fundsCost, int partCount, double massTonnes, bool chargeLaunchOverhead)
		{
			var b = new BuildPointsCostBreakdown
			{
				constant = chargeLaunchOverhead ? settings.constantCost : 0,
				funds = fundsCost * settings.fundsCostWeight,
				partCountCost = partCount * settings.costPerPart,
				mass = massTonnes * settings.massCostWeight,
				fundsCost = fundsCost,
				massTonnes = massTonnes,
				partCount = partCount
			};

			double raw = b.constant + b.funds + b.partCountCost + b.mass;

			if (chargeLaunchOverhead)
				b.total = raw < settings.minimumCraftCost ? settings.minimumCraftCost : raw;
			else
				b.total = raw; // no floor on recovery

			return b;
		}

		// Stock helper ShipConstruction.GetPartCostsAndMass is per-part, not
		// per-ship (it takes a ConfigNode + AvailablePart, not a whole
		// ShipConstruct/ProtoVessel) — so sum it across every part to get
		// ship-wide totals. Shared by the editor and recovery paths, since
		// both ultimately hand it one AvailablePart per part.
		private static void SumPartCostsAndMass(List<AvailablePart> availableParts, out double fundsCost, out double massTonnes)
		{
			float totalDryCost = 0f, totalFuelCost = 0f, totalDryMass = 0f, totalFuelMass = 0f;
			foreach (AvailablePart ap in availableParts)
			{
				ShipConstruction.GetPartCostsAndMass(ap.partConfig, ap,
					out float dryCost, out float fuelCost, out float dryMass, out float fuelMass);
				totalDryCost += dryCost;
				totalFuelCost += fuelCost;
				totalDryMass += dryMass;
				totalFuelMass += fuelMass;
			}
			fundsCost = totalDryCost + totalFuelCost;
			massTonnes = totalDryMass + totalFuelMass;
		}
	}
}
