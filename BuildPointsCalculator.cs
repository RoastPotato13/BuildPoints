namespace BuildPoints
{
	/// <summary>
	/// Breakdown of a vessel's Build Points cost by component, so UI (the
	/// VAB/SPH toolbar window) can show each contributor separately instead
	/// of just the total. fundsCost/massTonnes are the vessel's raw funds
	/// cost and mass (not yet converted to BP) — useful for display. Whether
	/// they include fuel/resources depends on Settings.includeFuelInCost.
	/// </summary>
	public struct BuildPointsCostBreakdown
	{
		public double constant;       // BP, from settings.constantCost directly
		public double funds;          // BP, from fundsCost * fundsCostWeight
		public double partCountCost;  // BP, from partCount * costPerPart
		public double mass;           // BP, from massTonnes * massCostWeight
		public double fundsCost;      // raw vessel funds cost (dry, plus resources if included)
		public double massTonnes;     // raw vessel mass (dry, plus resources if included)
		public int partCount;
		public double total;          // sum of the four BP components, floored at minimumCraftCost
	}

	/// <summary>
	/// Computes the Build Points cost of a vessel — either the one
	/// currently in the editor (charged on launch) or a just-recovered
	/// one (refunded on recovery). Both paths share the same cost
	/// formula (BuildBreakdown) so they can never drift out of sync with
	/// each other. Both also read this save's settings via
	/// BuildPointsScenario.GetActiveSettings() rather than the global
	/// defaults, so the cost formula respects whatever the player set for
	/// this game (including whether fuel/resources are counted, see
	/// Settings.includeFuelInCost).
	///
	/// Where the raw funds cost and mass come from:
	///   Launch   — the live ship in the editor (ShipConstruct.GetShipCosts /
	///              GetShipMass), the same totals the stock editor readouts
	///              use. That means tweaked tank levels, part variants and
	///              module cost/mass modifiers are all reflected, and the
	///              cost window updates as the player changes resources.
	///   Recovery — each ProtoPartSnapshot, using the same per-part formula
	///              stock applies for the launch charge (base cost + module
	///              costs - resource value; prefab mass + module mass) with the
	///              values the snapshot stored. Resources are added at the
	///              amounts the vessel actually came back with, only when
	///              includeFuelInCost is on. This keeps recovery consistent
	///              with launch even when a part's live numbers differ from
	///              its template (FAR wing mass, a parachute's module cost,
	///              resources a mod adds at runtime, and so on).
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
		///
		/// Uses the stock two-argument ShipConstruct.GetShipCosts and
		/// GetShipMass (each passes a null crew manifest). "fuel" in both means
		/// every part resource, not just liquid fuel and oxidizer. Launch clamps
		/// are counted like any other part.
		/// </summary>
		public static bool TryGetShipCostBreakdown(out BuildPointsCostBreakdown breakdown)
		{
			breakdown = default;

			if (!HighLogic.LoadedSceneIsEditor || EditorLogic.fetch == null || EditorLogic.fetch.ship == null)
				return false;

			var ship = EditorLogic.fetch.ship;
			int partCount = ship.parts.Count;
			if (partCount == 0) return false;

			var settings = BuildPointsScenario.GetActiveSettings();

			// Live cost totals, split into dry and resource ("fuel") portions.
			ship.GetShipCosts(out float dryCost, out float fuelCost);

			// Live mass, the same totals the stock editor uses. The two-argument
			// overloads pass a null crew manifest, so kerbal mass and crew
			// inventory are not included (only what's built into the craft).
			ship.GetShipMass(out float dryMass, out float fuelMass);

			double fundsCost = dryCost + (settings.includeFuelInCost ? fuelCost : 0f);
			double massTonnes = dryMass + (settings.includeFuelInCost ? fuelMass : 0f);

			// In TryGetShipCostBreakdown (launch / editor):
			breakdown = BuildBreakdown(settings, fundsCost, partCount, massTonnes,
				chargeLaunchOverhead: true);
			return true;
		}

		/// <summary>
		/// Same per-part / per-funds / per-mass formula as TryGetCurrentShipCost,
		/// but WITHOUT the constant per-launch cost or the minimum-cost floor,
		/// applied to a recovered vessel's ProtoVessel rather than a live editor
		/// ShipConstruct — used to size the Build Points refund on recovery.
		///
		/// Per part, this mirrors what ShipConstruct.GetShipCosts / GetShipMass
		/// produce for the launch charge, from the values stored in the
		/// ProtoPartSnapshot:
		///   dry cost = partInfo.cost + moduleCosts
		///              - (maxAmount x unitCost) of each resource
		///   dry mass = prefab mass + moduleMass (not pps.mass, see the loop)
		/// If Settings.includeFuelInCost is on, each resource is added back at
		/// its current amount, so a vessel that comes back with empty tanks is
		/// refunded for empty tanks, matching what launch charged for what was
		/// actually loaded.
		///
		/// NOTE: verify against 1.12.5:
		///   ProtoPartSnapshot.partInfo / moduleMass / moduleCosts / resources
		///   (List&lt;ProtoPartResourceSnapshot&gt; with resourceName, amount and
		///   maxAmount), and PartResourceLibrary.Instance.GetDefinition(string)
		///   with PartResourceDefinition.unitCost / density.
		/// A part whose mod was removed since launch will have partInfo null;
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

			var settings = BuildPointsScenario.GetActiveSettings();

			double totalCost = 0, totalMass = 0;
			int counted = 0;

			foreach (ProtoPartSnapshot pps in protoVessel.protoPartSnapshots)
			{
				if (pps?.partInfo == null) continue;
				counted++;

				// Mirror the launch-side per-part formula (see the method summary).
				double partDryCost = pps.partInfo.cost + pps.moduleCosts;
				// Prefab mass plus module mass modifiers, which is what the editor
				// counts. pps.mass is deliberately not used: in flight it came out
				// 0.09 t higher than launch counted for the same craft, so it isn't
				// a like-for-like match with the editor's numbers.
				double partDryMass = pps.partInfo.partPrefab.mass + pps.moduleMass;

				if (pps.resources != null)
				{
					foreach (ProtoPartResourceSnapshot res in pps.resources)
					{
						if (res == null) continue;
						PartResourceDefinition def = PartResourceLibrary.Instance.GetDefinition(res.resourceName);
						if (def == null) continue;

						// The value of a full load is not part of the dry cost.
						partDryCost -= res.maxAmount * def.unitCost;

						// Resources at the levels the vessel actually has now.
						if (settings.includeFuelInCost)
						{
							totalCost += res.amount * def.unitCost;
							totalMass += res.amount * def.density;
						}
					}
				}

				totalCost += partDryCost;
				totalMass += partDryMass;
			}

			if (counted == 0) return false;

			fundsCost = totalCost;

			// In TryGetRecoveredVesselCost (recovery):
			var breakdown = BuildBreakdown(settings, fundsCost, partCount, totalMass,
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
	}
}
