using System;

namespace BuildPoints
{
    public enum BuildPointsUpgradeKind
    {
        AccrualRate, // adds BP/day to this save's base accrual
        Capacity     // adds BP to this save's max storage
    }

    public enum BuildPointsCurrency
    {
        Funds,
        Science
    }

    /// <summary>
    /// The rules for buying permanent Build Points upgrades with funds or
    /// science (the "BuildPoint" tab of the Space Center toolbar window).
    ///
    /// A purchase is priced per unit of upgrade — e.g. fundsPerRateIncrease is
    /// the funds cost of +1 BP/day — so buying 2.5 costs 2.5x the unit price.
    /// What's stored in the save is the BP amount bought (see
    /// BuildPointsScenario.RateUpgradePerDay / CapacityUpgrade), not the
    /// price paid, so changing the prices later only affects future purchases.
    ///
    /// Which currencies work depends on the game mode (see IsCurrencyAvailable):
    /// funds or science in Career, science only in Science Sandbox, and
    /// nothing in Sandbox, where upgrades are disabled. A unit price of 0 in the settings disables
    /// that currency for that upgrade (otherwise it would be free and
    /// unlimited).
    ///
    /// NOTE: verify against 1.12.5: Funding.Instance.Funds (double),
    /// Funding.AddFunds(double, TransactionReasons),
    /// ResearchAndDevelopment.Instance.Science (float) and
    /// ResearchAndDevelopment.AddScience(float, TransactionReasons). Stock uses
    /// AddFunds/AddScience with negative values for its own spending.
    /// </summary>
    public static class BuildPointsUpgrades
    {
        /// <summary>
        /// Which currencies can be used in the current game mode:
        ///   Career          — funds or science
        ///   Science Sandbox — science only (there are no funds)
        ///   Sandbox         — neither, so upgrades are disabled
        /// </summary>
        public static bool IsCurrencyAvailable(BuildPointsCurrency currency)
        {
            var game = HighLogic.CurrentGame;
            if (game == null) return false;

            if (currency == BuildPointsCurrency.Funds)
                return game.Mode == Game.Modes.CAREER && Funding.Instance != null;

            return (game.Mode == Game.Modes.CAREER || game.Mode == Game.Modes.SCIENCE_SANDBOX)
                   && ResearchAndDevelopment.Instance != null;
        }

        public static double GetBalance(BuildPointsCurrency currency)
        {
            if (!IsCurrencyAvailable(currency)) return 0;

            return currency == BuildPointsCurrency.Funds
                ? Funding.Instance.Funds
                : ResearchAndDevelopment.Instance.Science;
        }

        /// <summary>Price of one unit of the upgrade (1 BP/day or 1 BP of max) in the given currency.</summary>
        public static double GetUnitCost(BuildPointsSettingsValues s, BuildPointsUpgradeKind kind, BuildPointsCurrency currency)
        {
            switch (kind)
            {
                case BuildPointsUpgradeKind.AccrualRate:
                    return currency == BuildPointsCurrency.Funds ? s.fundsPerRateIncrease : s.sciencePerRateIncrease;
                default:
                    return currency == BuildPointsCurrency.Funds ? s.fundsPerCapacityIncrease : s.sciencePerCapacityIncrease;
            }
        }

        /// <summary>Currency exists in this game mode AND this upgrade has a price in it.</summary>
        public static bool IsPurchasable(BuildPointsSettingsValues s, BuildPointsUpgradeKind kind, BuildPointsCurrency currency)
        {
            return IsCurrencyAvailable(currency) && GetUnitCost(s, kind, currency) > 0;
        }

        /// <summary>
        /// Total price for the given amount, rounded up (whole funds, tenths of
        /// science) so what the UI shows is exactly what's charged.
        /// </summary>
        public static double GetTotalCost(BuildPointsSettingsValues s, BuildPointsUpgradeKind kind,
            BuildPointsCurrency currency, double amount)
        {
            double raw = GetUnitCost(s, kind, currency) * amount;
            return currency == BuildPointsCurrency.Funds
                ? Math.Ceiling(raw - 1e-9)
                : Math.Ceiling(raw * 10.0 - 1e-9) / 10.0;
        }

        /// <summary>
        /// Charges the currency and adds the upgrade. Returns false (nothing
        /// charged, nothing added) if it can't be done; message is always set
        /// and is suitable for a screen message.
        /// </summary>
        public static bool TryPurchase(BuildPointsScenario scenario, BuildPointsUpgradeKind kind,
            BuildPointsCurrency currency, double amount, out string message)
        {
            string currencyName = currency == BuildPointsCurrency.Funds ? "funds" : "science";

            if (scenario == null)
            {
                message = "Build Points aren't active in this game.";
                return false;
            }
            if (double.IsNaN(amount) || double.IsInfinity(amount) || amount <= 0)
            {
                message = "Enter an amount greater than 0.";
                return false;
            }
            if (!IsCurrencyAvailable(currency))
            {
                message = currency == BuildPointsCurrency.Funds
                    ? "Upgrades can only be bought with funds in Career mode."
                    : "Upgrades can only be bought with science in Career or Science Sandbox mode.";
                return false;
            }

            var settings = scenario.Settings;
            if (GetUnitCost(settings, kind, currency) <= 0)
            {
                message = "Upgrades can't be bought with " + currencyName + " (its price is 0 in the settings).";
                return false;
            }

            double cost = GetTotalCost(settings, kind, currency, amount);
            if (GetBalance(currency) + 1e-6 < cost)
            {
                message = currency == BuildPointsCurrency.Funds
                    ? $"Not enough funds: need {cost:N0}."
                    : $"Not enough science: need {cost:0.#}.";
                return false;
            }

            if (currency == BuildPointsCurrency.Funds)
                Funding.Instance.AddFunds(-cost, TransactionReasons.None);
            else
                ResearchAndDevelopment.Instance.AddScience(-(float)cost, TransactionReasons.None);

            scenario.AddUpgrade(kind, amount);

            string what = kind == BuildPointsUpgradeKind.AccrualRate
                ? $"+{amount:0.##} BP/day accrual"
                : $"+{amount:0.##} max BP";
            string paid = currency == BuildPointsCurrency.Funds ? $"{cost:N0} funds" : $"{cost:0.#} science";
            message = $"Build Points upgraded: {what} for {paid}.";
            return true;
        }
    }
}
