using System;
using UnityEngine;

namespace BuildPoints
{
    /// <summary>
    /// Persists the player's current Build Points balance and accrues new
    /// points over time. Runs in every relevant scene so the balance keeps
    /// ticking whether the player is at the Space Center, in the editor, or
    /// in flight.
    ///
    /// Also owns this save's own copy of the mod's settings (Settings) —
    /// seeded from BuildPointsConfig.Defaults (GlobalSettings.cfg) the first
    /// time this save is created, then stored in the save's persistent file
    /// via OnSave/OnLoad, the same way CurrentPoints is. The Space Center
    /// toolbar's Apply button edits this copy, never GlobalSettings.cfg.
    ///
    /// Settings.baseAccrualPerDay and Settings.capacity are BASE values. The
    /// upgrades the player buys with funds/science (BuildPointsUpgrades) are
    /// kept here, saved alongside the balance, and added on top:
    ///   accrual/day = (baseAccrualPerDay + RateUpgradePerDay)
    ///                 * (1 + VAB/SPH facility bonus)
    ///   max BP      = capacity + CapacityUpgrade
    /// They are stored as BP amounts rather than prices paid, so editing the
    /// upgrade prices later never changes what's already been bought.
    ///
    /// A brand-new save's balance starts at the global startingPoints.
    /// </summary>
    [KSPScenario(ScenarioCreationOptions.AddToAllGames,
        GameScenes.SPACECENTER, GameScenes.EDITOR, GameScenes.FLIGHT, GameScenes.TRACKSTATION)]
    public class BuildPointsScenario : ScenarioModule
    {
        public static BuildPointsScenario Instance { get; private set; }

        /// <summary>Current banked Build Points.</summary>
        public double CurrentPoints { get; private set; }

        /// <summary>BP/day of accrual bought with funds/science, added to the base rate.</summary>
        public double RateUpgradePerDay { get; private set; }

        /// <summary>BP of max storage bought with funds/science, added to the base cap.</summary>
        public double CapacityUpgrade { get; private set; }

        /// <summary>
        /// Saved top-left position of the on-screen Build Points window, in screen
        /// pixels. Stored in this save's persistent file alongside the balance.
        /// </summary>
        public float DisplayX { get; private set; } = 500f;
        public float DisplayY { get; private set; } = 8f;

        public void SetDisplayPosition(float x, float y)
        {
            DisplayX = x;
            DisplayY = y;
        }

        /// <summary>
        /// Saved top-left position of the Space Center toolbar window
        /// (Settings / BuildPoint tabs), in screen pixels. Position only:
        /// unlike the VAB/SPH cost windows, whether it was open is
        /// deliberately not stored, so it always starts closed.
        /// </summary>
        public float SpaceCenterWindowX { get; private set; } = 300f;
        public float SpaceCenterWindowY { get; private set; } = 100f;

        public void SetSpaceCenterWindowPosition(float x, float y)
        {
            SpaceCenterWindowX = x;
            SpaceCenterWindowY = y;
        }

        /// <summary>
        /// Which tab of the Space Center window was selected last
        /// (0 = Settings, 1 = BuildPoint). Stored per save; a new save has
        /// no stored value, so it starts on 0 (Settings).
        /// </summary>
        public int SpaceCenterTab { get; private set; }

        public void SetSpaceCenterTab(int tab)
        {
            SpaceCenterTab = tab;
        }

        /// <summary>
        /// Position and open/closed state of the Build Points Cost window,
        /// tracked separately for the VAB and the SPH so each can sit in its
        /// own place and start open or closed on its own. Stored in this
        /// save's persistent file.
        /// </summary>
        public BuildPointsWindowState VabCostWindow { get; } = new BuildPointsWindowState(300f, 100f);
        public BuildPointsWindowState SphCostWindow { get; } = new BuildPointsWindowState(300f, 100f);

        public BuildPointsWindowState GetCostWindowState(EditorFacility facility) =>
            facility == EditorFacility.SPH ? SphCostWindow : VabCostWindow;

        /// <summary>This save's own settings — see class remarks.</summary>
        public BuildPointsSettingsValues Settings { get; private set; }

        // Universal time (in-game seconds) at which we last accrued points.
        // Using UT rather than real time means accrual is warp-safe and
        // doesn't grant free points while the game is paused/closed.
        private double lastAccrualUT = -1;

        public override void OnAwake()
        {
            Instance = this;
            BuildPointsConfig.EnsureLoaded();
            if (Settings == null) Settings = BuildPointsConfig.Defaults.Clone();
        }

        public void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Single source of truth for "does Build Points apply to the
        /// current game?" Used by the launch gate, the launch charge, the
        /// on-screen readout, accrual, and recovery refunds, so they can
        /// never disagree about which game modes are covered.
        ///
        /// To extend to sandbox later, add a case for Game.Modes.SANDBOX
        /// here (ideally driven by an "enableInSandbox" setting).
        /// </summary>
        public static bool IsActiveForCurrentGame()
        {
            var game = HighLogic.CurrentGame;
            if (game == null) return false;

			switch (game.Mode)
			{
				case Game.Modes.CAREER:
				case Game.Modes.SCIENCE_SANDBOX:
				case Game.Modes.SANDBOX:
					return true;
				default:
					return false;
			}
		}

        /// <summary>
        /// Returns the settings to use right now: this save's own copy if
        /// a save is loaded, otherwise the global defaults. Same as
        /// BuildPointsConfig.Settings; kept so callers can use either.
        /// </summary>
        public static BuildPointsSettingsValues GetActiveSettings() => BuildPointsConfig.Settings;

        /// <summary>
        /// Overwrites this save's settings with the defaults from
        /// GlobalSettings.cfg, re-reading the file first in case it's been
        /// hand-edited since the game started. Wired to the "Reset to Global
        /// Defaults" button in the Settings window. Does not touch the
        /// Build Points balance or the upgrades the player has bought.
        /// </summary>
        public void ResetSettingsToGlobalDefaults()
        {
            BuildPointsConfig.Load();
            Settings.CopyFrom(BuildPointsConfig.Defaults);
        }

        /// <summary>
        /// Adds a bought upgrade. Called by BuildPointsUpgrades.TryPurchase
        /// after the funds/science have been charged.
        /// </summary>
        public void AddUpgrade(BuildPointsUpgradeKind kind, double amount)
        {
            if (amount <= 0) return;

            if (kind == BuildPointsUpgradeKind.AccrualRate) RateUpgradePerDay += amount;
            else CapacityUpgrade += amount;
        }

        public void FixedUpdate()
        {
            if (HighLogic.LoadedScene == GameScenes.MAINMENU) return;
            if (!IsActiveForCurrentGame()) return;

            double now = Planetarium.GetUniversalTime();
            if (lastAccrualUT < 0)
            {
                lastAccrualUT = now;
                return;
            }

            double elapsedSeconds = now - lastAccrualUT;
            if (elapsedSeconds < 0)
            {
                // UT went backwards (quickload, revert, etc.): resync instead of stalling.
                lastAccrualUT = now;
                return;
            }
            if (elapsedSeconds == 0) return;

            double rate = GetCurrentAccrualRatePerSecond();
            Accrue(rate * elapsedSeconds);
            lastAccrualUT = now;
        }

        /// <summary>
        /// VAB and SPH upgrade levels, each 0..1 normalized by stock KSP.
        /// </summary>
        public static void GetFacilityLevels(out float vabLevel, out float sphLevel)
        {
            vabLevel = ScenarioUpgradeableFacilities.GetFacilityLevel(SpaceCenterFacility.VehicleAssemblyBuilding);
            sphLevel = ScenarioUpgradeableFacilities.GetFacilityLevel(SpaceCenterFacility.SpaceplaneHangar);
        }

        /// <summary>
        /// The accrual bonus from the VAB/SPH as a fraction (0.5 = +50%).
        /// We use whichever facility is more upgraded so players aren't
        /// penalized for specializing in one build track.
        /// </summary>
        public double GetFacilityBonusFraction()
        {
            GetFacilityLevels(out float vabLevel, out float sphLevel);
            float facilityLevel = Mathf.Max(vabLevel, sphLevel); // 0f..1f
            return facilityLevel * (Settings.facilityLevelBonusPercent / 100.0);
        }

        /// <summary>Base accrual plus purchased upgrades, before the facility bonus (BP/day).</summary>
        public double GetAccrualPerDayBeforeFacility() => Settings.baseAccrualPerDay + RateUpgradePerDay;

        /// <summary>Total accrual in BP per homeworld day, everything included.</summary>
        public double GetAccrualPerDay() => GetAccrualPerDayBeforeFacility() * (1.0 + GetFacilityBonusFraction());

        /// <summary>
        /// Accrual rate in BP/second: (base + purchased) scaled by the
        /// VAB/SPH bonus (see GetFacilityBonusFraction), per homeworld day.
        /// </summary>
        public double GetCurrentAccrualRatePerSecond()
        {
            return GetAccrualPerDay() / GetHomeworldDayLengthSeconds();
        }

        /// <summary>
        /// Length of a homeworld solar day, in seconds — used as the "day"
        /// unit for the accrual rate instead of a hardcoded 6h Kerbin day,
        /// so this stays correct under Kopernicus rescales/rescaled systems
        /// (JNSQ, RSS, etc.) where the homeworld's rotation period differs
        /// from stock.
        ///
        /// NOTE: verify solarDayLength against your KSP version — it's the
        /// stock CelestialBody property that drives the day-length used in
        /// the UI's date formatter, and accounts for orbital motion (solar
        /// vs. sidereal day), which is what a "day" means to the player.
        /// Falls back to sidereal rotationPeriod, then to a flat 6h, for
        /// edge cases (e.g. a tidally-locked homeworld, where solar day is
        /// undefined/infinite).
        /// </summary>
        public static double GetHomeworldDayLengthSeconds()
        {
            const double fallback = 6 * 60 * 60; // stock Kerbin day, last resort

            var home = FlightGlobals.GetHomeBody();
            if (home == null) return fallback;

            double day = home.solarDayLength;
            if (double.IsNaN(day) || double.IsInfinity(day) || day <= 0)
                day = home.rotationPeriod;
            if (double.IsNaN(day) || double.IsInfinity(day) || day <= 0)
                day = fallback;

            return day;
        }

        public void Accrue(double amount)
        {
            if (amount <= 0) return;
            CurrentPoints = Math.Min(CurrentPoints + amount, GetCapacity());
        }

        /// <summary>Attempts to spend points. Returns false (no state change) if insufficient.</summary>
        public bool TrySpend(double amount)
        {
            if (amount <= 0) return true;
            if (CurrentPoints + 1e-6 < amount) return false;
            CurrentPoints -= amount;
            return true;
        }

        /// <summary>Max storage: this save's base capacity plus purchased upgrades.</summary>
        public double GetCapacity() => Settings.capacity + CapacityUpgrade;

        /// <summary>
        /// Seconds of (in-game) time needed before CurrentPoints would reach
        /// neededPoints at the current accrual rate. Returns 0 if already
        /// affordable, or PositiveInfinity if the rate is 0 and it's not.
        /// </summary>
        public double GetSecondsUntilAffordable(double neededPoints)
        {
            double deficit = neededPoints - CurrentPoints;
            if (deficit <= 0) return 0;

            double rate = GetCurrentAccrualRatePerSecond();
            if (rate <= 0) return double.PositiveInfinity;

            return deficit / rate;
        }

        /// <summary>
        /// Jumps the game clock forward and immediately grants the Build
        /// Points that would have accrued over that span. This is the
        /// "instant" alternative to WarpAndReturnController's real-warp
        /// approach — see Settings.useInstantTimeSkip.
        ///
        /// The editor scene has no running Planetarium, so
        /// Planetarium.SetUniversalTime does nothing there. In that case the
        /// game's stored universal time (flightState.universalTime) is
        /// written instead, and the new time carries into the other scenes.
        /// On-rails vessels follow the new clock on their own. If the clock
        /// still doesn't advance, nothing is granted and the clock and
        /// stored time are restored.
        ///
        /// It is a hard jump rather than an incremental warp, so mods that
        /// do their own per-frame background simulation (e.g. some life
        /// support mods) may not react to it the way they would to a normal
        /// warp.
        /// </summary>
        public bool SkipAheadSeconds(double seconds)
        {
            if (seconds <= 0) return false;

            double before = Planetarium.GetUniversalTime();
            double target = before + seconds;
            bool wroteFallback = false;

            Planetarium.SetUniversalTime(target);
            double after = Planetarium.GetUniversalTime();

            // The editor has no running Planetarium, so the call above does nothing there.
            // Fall back to writing the game's stored universal time.
            if (after - before < seconds * 0.5 && HighLogic.CurrentGame?.flightState != null)
            {
                HighLogic.CurrentGame.flightState.universalTime = target;
                wroteFallback = true;
                after = Planetarium.GetUniversalTime();
                Debug.Log($"[BuildPoints] Instant skip: Planetarium ignored the jump; wrote flightState.universalTime. " +
                          $"Planetarium UT now {after:0.0}, flightState UT {HighLogic.CurrentGame.flightState.universalTime:0.0}");
            }

            double actual = after - before;
            Debug.Log($"[BuildPoints] Instant skip: asked for {seconds:0.0}s, UT {before:0.0} -> {after:0.0} (moved {actual:0.0}s)");

            if (actual < seconds * 0.9)
            {
                // Undo the fallback write so a failed skip doesn't leave the stored time changed.
                if (wroteFallback)
                    HighLogic.CurrentGame.flightState.universalTime = before;
                Planetarium.SetUniversalTime(before);

                Debug.LogWarning("[BuildPoints] Instant skip: game clock didn't advance; not granting points.");
                return false;
            }

            // Grant only what the clock really advanced, and resync from the real UT.
            Accrue(GetCurrentAccrualRatePerSecond() * actual);
            lastAccrualUT = after;

            return true;
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            BuildPointsConfig.EnsureLoaded();

            // Seed from the current global defaults, then overwrite with
            // this save's own stored values if it has any yet (new saves
            // won't — they just keep the defaults as their starting point).
            Settings = BuildPointsConfig.Defaults.Clone();
            ConfigNode settingsNode = node.GetNode("Settings");
            if (settingsNode != null) Settings.Load(settingsNode);

            // Purchased upgrades. Loaded before the balance below, because a
            // new save's starting balance is capped by the (upgraded) capacity.
            // Properties can't be passed by ref, so go through locals.
            double rateUpgrade = 0, capacityUpgrade = 0;
            node.TryGetValue("rateUpgradePerDay", ref rateUpgrade);
            node.TryGetValue("capacityUpgrade", ref capacityUpgrade);
            RateUpgradePerDay = Math.Max(0, rateUpgrade);
            CapacityUpgrade = Math.Max(0, capacityUpgrade);

            if (node.HasValue("currentPoints"))
            {
                double points = 0;
                node.TryGetValue("currentPoints", ref points);
                CurrentPoints = points;
            }
            else
            {
                // No stored balance yet: a brand-new save (or one that had
                // this mod added mid-game). Seed it from startingPoints in
                // GlobalSettings.cfg, capped at this save's storage cap.
                // Existing saves never hit this branch, so changing
                // startingPoints later doesn't touch a save in progress.
                CurrentPoints = Math.Min(BuildPointsConfig.Defaults.startingPoints, GetCapacity());
            }

            float displayX = DisplayX, displayY = DisplayY;
            node.TryGetValue("displayX", ref displayX);
            node.TryGetValue("displayY", ref displayY);
            DisplayX = displayX;
            DisplayY = displayY;

            // Space Center toolbar window position (position only; it always starts closed).
            float scWindowX = SpaceCenterWindowX, scWindowY = SpaceCenterWindowY;
            node.TryGetValue("spaceCenterWindowX", ref scWindowX);
            node.TryGetValue("spaceCenterWindowY", ref scWindowY);
            SpaceCenterWindowX = scWindowX;
            SpaceCenterWindowY = scWindowY;

            // Last-selected Space Center tab. Missing in new saves, so it stays 0 (Settings).
            int scTab = 0;
            node.TryGetValue("spaceCenterTab", ref scTab);
            SpaceCenterTab = Math.Max(0, scTab);

            // Per-editor cost window state (position + open/closed).
            ConfigNode vabNode = node.GetNode("VabCostWindow");
            if (vabNode != null) VabCostWindow.Load(vabNode);
            ConfigNode sphNode = node.GetNode("SphCostWindow");
            if (sphNode != null) SphCostWindow.Load(sphNode);

            double lastUT = -1;
            node.TryGetValue("lastAccrualUT", ref lastUT);
            lastAccrualUT = lastUT;

            // After a revert to the VAB/SPH the game reloads a snapshot that already
            // has the launch charged. Put the balance back to what it was before it.
            if (LaunchRevertTracker.TryConsumeRevertRestore(out double restoredPoints))
            {
                CurrentPoints = Math.Min(restoredPoints, GetCapacity());
                Debug.Log($"[BuildPoints] Revert to editor: balance restored to {CurrentPoints:0.0} BP");
            }
        }

        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node);
            node.AddValue("currentPoints", CurrentPoints);
            node.AddValue("lastAccrualUT", lastAccrualUT);

            node.AddValue("rateUpgradePerDay", RateUpgradePerDay);
            node.AddValue("capacityUpgrade", CapacityUpgrade);

            node.AddValue("displayX", DisplayX);
            node.AddValue("displayY", DisplayY);

            node.AddValue("spaceCenterWindowX", SpaceCenterWindowX);
            node.AddValue("spaceCenterWindowY", SpaceCenterWindowY);
            node.AddValue("spaceCenterTab", SpaceCenterTab);

            VabCostWindow.Save(node.AddNode("VabCostWindow"));
            SphCostWindow.Save(node.AddNode("SphCostWindow"));

            ConfigNode settingsNode = node.AddNode("Settings");
            Settings?.Save(settingsNode);
        }
    }
}
