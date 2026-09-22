namespace BuildPoints
{
    /// <summary>
    /// The full set of mod tunables. Used two ways: as the defaults loaded
    /// from GlobalSettings.cfg (BuildPointsConfig.Defaults), and as the live,
    /// per-save values a given game actually plays with
    /// (BuildPointsScenario.Settings, stored in that save's persistent file).
    /// A new save starts as a copy of the defaults; after that the two are
    /// independent until the player hits "Reset to Global Defaults".
    ///
    /// baseAccrualPerDay and capacity are BASE values: what the player buys on
    /// the toolbar's BuildPoint tab is stored separately in the scenario and
    /// added on top (see BuildPointsScenario.GetAccrualPerDay / GetCapacity).
    /// </summary>
    public class BuildPointsSettingsValues
    {
        // --- Starting balance ---
        // Only meaningful for a brand-new save, and only ever read from the
        // defaults (GlobalSettings.cfg). It is deliberately not written into
        // a save's own settings (see Save) and not shown in the toolbar.
        public float startingPoints = 100f;

        // --- Accrual (base values; purchased upgrades are added on top) ---
        public float baseAccrualPerDay = 5f;
        public float facilityLevelBonusPercent = 50f;

        // --- Capacity (base value; purchased upgrades are added on top) ---
        public float capacity = 200f;

        // --- Upgrade prices (BuildPoint tab) ---
        // Price of +1 BP/day of accrual, and of +1 BP of max storage, in funds
        // or science. A price of 0 disables buying with that currency.
        public float fundsPerRateIncrease = 5000f;
        public float sciencePerRateIncrease = 25f;
        public float fundsPerCapacityIncrease = 250f;
        public float sciencePerCapacityIncrease = 2f;

        // --- Cost formula ---
        // cost = constantCost + (vesselFundsCost * fundsCostWeight)
        //        + (partCount * costPerPart) + (vesselMass * massCostWeight)
        public float constantCost = 0f;
        public float fundsCostWeight = 0.01f;
        public float costPerPart = 0.5f;
        public float massCostWeight = 0.2f;
        public float minimumCraftCost = 1f;

        // true  = the vessel funds cost and mass in the formula above include
        //         fuel/resources.
        // false = dry cost and dry mass only (fuel/resources are ignored).
        // Applies to both the launch charge and the recovery refund.
        public bool includeFuelInCost = false;

        // --- Recovery ---
        public float recoveryRefundPercent = 50f;

        // --- "Warp until affordable" behavior ---
        public bool useInstantTimeSkip = false;

        // --- Display ---
        public bool showBuildPointsDisplay = true;

        public BuildPointsSettingsValues Clone()
        {
            var copy = new BuildPointsSettingsValues();
            copy.CopyFrom(this);
            return copy;
        }

        public void CopyFrom(BuildPointsSettingsValues other)
        {
            startingPoints = other.startingPoints;
            baseAccrualPerDay = other.baseAccrualPerDay;
            facilityLevelBonusPercent = other.facilityLevelBonusPercent;
            capacity = other.capacity;
            fundsPerRateIncrease = other.fundsPerRateIncrease;
            sciencePerRateIncrease = other.sciencePerRateIncrease;
            fundsPerCapacityIncrease = other.fundsPerCapacityIncrease;
            sciencePerCapacityIncrease = other.sciencePerCapacityIncrease;
            constantCost = other.constantCost;
            fundsCostWeight = other.fundsCostWeight;
            costPerPart = other.costPerPart;
            massCostWeight = other.massCostWeight;
            minimumCraftCost = other.minimumCraftCost;
            includeFuelInCost = other.includeFuelInCost;
            recoveryRefundPercent = other.recoveryRefundPercent;
            useInstantTimeSkip = other.useInstantTimeSkip;
            showBuildPointsDisplay = other.showBuildPointsDisplay;
        }

        public void Load(ConfigNode node)
        {
            ReadFloat(node, "startingPoints", ref startingPoints);
            ReadFloat(node, "baseAccrualPerDay", ref baseAccrualPerDay);
            ReadFloat(node, "facilityLevelBonusPercent", ref facilityLevelBonusPercent);
            ReadFloat(node, "capacity", ref capacity);
            ReadFloat(node, "fundsPerRateIncrease", ref fundsPerRateIncrease);
            ReadFloat(node, "sciencePerRateIncrease", ref sciencePerRateIncrease);
            ReadFloat(node, "fundsPerCapacityIncrease", ref fundsPerCapacityIncrease);
            ReadFloat(node, "sciencePerCapacityIncrease", ref sciencePerCapacityIncrease);
            ReadFloat(node, "constantCost", ref constantCost);
            ReadFloat(node, "fundsCostWeight", ref fundsCostWeight);
            ReadFloat(node, "costPerPart", ref costPerPart);
            ReadFloat(node, "massCostWeight", ref massCostWeight);
            ReadFloat(node, "minimumCraftCost", ref minimumCraftCost);
            ReadBool(node, "includeFuelInCost", ref includeFuelInCost);
            ReadFloat(node, "recoveryRefundPercent", ref recoveryRefundPercent);
            ReadBool(node, "useInstantTimeSkip", ref useInstantTimeSkip);
            ReadBool(node, "showBuildPointsDisplay", ref showBuildPointsDisplay);
        }

        // Writes a save's own settings. startingPoints is intentionally left
        // out: it only matters at save creation and always comes from
        // GlobalSettings.cfg, so persisting it would just leave a stale copy
        // in the save file.
        public void Save(ConfigNode node)
        {
            node.AddValue("baseAccrualPerDay", baseAccrualPerDay);
            node.AddValue("facilityLevelBonusPercent", facilityLevelBonusPercent);
            node.AddValue("capacity", capacity);
            node.AddValue("fundsPerRateIncrease", fundsPerRateIncrease);
            node.AddValue("sciencePerRateIncrease", sciencePerRateIncrease);
            node.AddValue("fundsPerCapacityIncrease", fundsPerCapacityIncrease);
            node.AddValue("sciencePerCapacityIncrease", sciencePerCapacityIncrease);
            node.AddValue("constantCost", constantCost);
            node.AddValue("fundsCostWeight", fundsCostWeight);
            node.AddValue("costPerPart", costPerPart);
            node.AddValue("massCostWeight", massCostWeight);
            node.AddValue("minimumCraftCost", minimumCraftCost);
            node.AddValue("includeFuelInCost", includeFuelInCost);
            node.AddValue("recoveryRefundPercent", recoveryRefundPercent);
            node.AddValue("useInstantTimeSkip", useInstantTimeSkip);
            node.AddValue("showBuildPointsDisplay", showBuildPointsDisplay);
        }

        private static void ReadFloat(ConfigNode node, string key, ref float value)
        {
            float parsed = value;
            if (node.TryGetValue(key, ref parsed)) value = parsed;
        }

        private static void ReadBool(ConfigNode node, string key, ref bool value)
        {
            bool parsed = value;
            if (node.TryGetValue(key, ref parsed)) value = parsed;
        }
    }
}
