using System;
using HarmonyLib;
using UnityEngine;

namespace BuildPoints
{
    /// <summary>
    /// Installs the Harmony patches once at game start, and loads
    /// BuildPointsConfig.Defaults from GlobalSettings.cfg so it's ready
    /// before any save needs to seed its own settings from it.
    ///
    /// Patching is wrapped in try/catch and logs every patched method, so a
    /// failed patch shows up clearly in KSP.log instead of silently leaving
    /// the launch gate uninstalled.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class BuildPointsBootstrap : MonoBehaviour
    {
        public void Awake()
        {
            BuildPointsConfig.EnsureLoaded();
            Debug.Log("[BuildPoints] Bootstrap running, applying Harmony patches");

            try
            {
                var harmony = new Harmony("com.buildpoints.mod");
                harmony.PatchAll();
                foreach (var m in harmony.GetPatchedMethods())
                    Debug.Log("[BuildPoints] Patched: " + m.DeclaringType + "." + m.Name);
            }
            catch (Exception e)
            {
                Debug.LogError("[BuildPoints] Harmony patching failed: " + e);
            }

            DontDestroyOnLoad(this);
        }
    }

    /// <summary>
    /// Launch GATE. Prefix on EditorLogic.launchVessel(string) — the real
    /// implementation (the parameterless launchVessel() just forwards to it,
    /// so it must not be patched too). Runs when the player clicks Launch,
    /// before any crew / pre-flight dialogs.
    ///
    /// This only CHECKS affordability. Returning false cancels the launch
    /// and shows a popup; returning true lets the launch continue. The
    /// actual charge happens later, in LaunchChargePatch, once the launch
    /// is confirmed — so backing out of a pre-flight warning costs nothing.
    /// </summary>
    [HarmonyPatch(typeof(EditorLogic), "launchVessel", new Type[] { typeof(string) })]
    public static class LaunchGatePatch
    {
        public static bool Prefix()
        {

            if (!BuildPointsScenario.IsActiveForCurrentGame()) return true;

            var scenario = BuildPointsScenario.Instance;
            if (scenario == null) return true; // fail open if not loaded

            if (!BuildPointsCalculator.TryGetCurrentShipCost(out double bpCost, out _, out _))
                return true; // couldn't compute a cost; don't block launch on a calculation failure

            double have = scenario.CurrentPoints;
            double cap = scenario.GetCapacity();

            if (bpCost <= have)
                return true; // affordable; charged later, on confirmed launch

            if (bpCost > cap)
            {
                // Craft costs more than the player could ever bank. Warping
                // won't help — the craft itself needs to change, or the cap
                // needs to be raised in the Build Points Settings window.
                PopupDialog.SpawnPopupDialog(
                    new MultiOptionDialog(
                        "buildPointsImpossible",
                        $"Vessel cost: {bpCost:0.0} BP\nMaximum capacity: {cap:0.0} BP\n\n" +
                        "This craft costs more Build Points than you can ever bank. Reduce its cost, " +
                        "mass, or part count, or raise the Build Points cap in the Settings window " +
                        "(toolbar button, Space Center).",
                        "Craft Exceeds Build Points Capacity",
                        HighLogic.UISkin,
                        new DialogGUIButton("OK", () => { })),
                    false, HighLogic.UISkin);
                return false;
            }

            // Affordable in principle, just not banked yet — offer to skip
            // ahead in time rather than making the player sit and wait.
            double secondsNeeded = scenario.GetSecondsUntilAffordable(bpCost);
            bool canWarp = !double.IsInfinity(secondsNeeded);

            string body = $"Vessel cost: {bpCost:0.0} BP\nAvailable: {have:0.0} / {cap:0.0} BP\n\n" +
                (canWarp
                    ? $"At the current accrual rate, you'll have enough in about {FormatDuration(secondsNeeded)}."
                    : "Your current accrual rate is 0, so waiting won't help — check the Build Points " +
                      "Settings window or upgrade your VAB/SPH.");

            var buttons = canWarp
                ? new[]
                  {
                      new DialogGUIButton("Warp Until Affordable", () =>
                      {
                          bool instant = scenario.Settings.useInstantTimeSkip;

                            if (instant)
                            {
	                            bool ok = scenario.SkipAheadSeconds(secondsNeeded + 1.0); // small buffer against float rounding
                                ScreenMessages.PostScreenMessage(
		                            ok ? "Build Points accrued — press Launch again."
		                               : "Couldn't advance the game clock (see KSP.log). Try turning off instant time-skip in the settings.",
		                            5f, ScreenMessageStyle.UPPER_CENTER);
                            }
						  else
                          {
                              double targetUT = Planetarium.GetUniversalTime() + secondsNeeded + 1.0;
                                if (!WarpAndReturnController.TryRequestWarpAndReturn(targetUT))
                                {
	                                ScreenMessages.PostScreenMessage(
		                                "Couldn't start warp (see KSP.log for details). Your craft was not changed.",
		                                5f, ScreenMessageStyle.UPPER_CENTER);
                                }
						  }
                      }),
                      new DialogGUIButton("Cancel", () => { })
                  }
                : new[] { new DialogGUIButton("OK", () => { }) };

            PopupDialog.SpawnPopupDialog(
                new MultiOptionDialog("buildPointsInsufficient", body, "Not Enough Build Points",
                    HighLogic.UISkin, buttons),
                false, HighLogic.UISkin);

            return false; // cancel the launch
        }

        /// <summary>
        /// Formats a duration using the same homeworld "day" the accrual
        /// formula itself uses (see BuildPointsScenario.GetHomeworldDayLengthSeconds),
        /// so the estimate stays internally consistent regardless of the
        /// player's Kerbin-time/Earth-time display setting, and correct
        /// under rescaled systems where the homeworld's day length differs
        /// from stock.
        /// </summary>
        private static string FormatDuration(double seconds)
        {
            double secondsPerDay = BuildPointsScenario.GetHomeworldDayLengthSeconds();
            int days = (int)(seconds / secondsPerDay);
            seconds -= days * secondsPerDay;
            int hours = (int)(seconds / 3600);
            seconds -= hours * 3600;
            int minutes = (int)(seconds / 60);

            if (days > 0) return $"{days}d {hours}h";
            if (hours > 0) return $"{hours}h {minutes}m";
            return $"{Mathf.Max(minutes, 1)}m";
        }
    }

    /// <summary>
    /// Launch CHARGE. Prefix on EditorLogic.proceedWithVesselLaunch, which the
    /// stock pre-flight check calls only once the launch is confirmed (all
    /// tests passed, or the player clicked through a warning). The cancel
    /// path (abortLaunch) never reaches it, so cancelling costs nothing.
    ///
    /// Also records the balance from just before the charge in
    /// LaunchRevertTracker, so a later "Revert to VAB/SPH" can put it back
    /// exactly (stock's own revert snapshot already contains the charge).
    ///
    /// A void Prefix always lets the original method run. That's deliberate:
    /// the editor is input-locked at this point, and blocking here could
    /// leave the lock stuck. If the charge somehow fails it is logged and the
    /// launch proceeds.
    /// </summary>
    [HarmonyPatch(typeof(EditorLogic), "proceedWithVesselLaunch")]
    public static class LaunchChargePatch
    {
        public static void Prefix()
        {

            // Any previously tracked launch is superseded by this one.
            LaunchRevertTracker.Clear();

            if (!BuildPointsScenario.IsActiveForCurrentGame()) return;

            var scenario = BuildPointsScenario.Instance;
            if (scenario == null) return;

            if (!BuildPointsCalculator.TryGetCurrentShipCost(out double bpCost, out _, out _))
            {
                Debug.LogWarning("[BuildPoints] Couldn't compute ship cost at launch; not charging");
                return;
            }

            double balanceBeforeCharge = scenario.CurrentPoints;

            if (scenario.TrySpend(bpCost))
            {
                LaunchRevertTracker.RecordLaunch(balanceBeforeCharge);
                Debug.Log($"[BuildPoints] Charged {bpCost:0.0} BP for launch");
            }
            else
                Debug.LogWarning($"[BuildPoints] Couldn't charge {bpCost:0.0} BP at launch (balance {scenario.CurrentPoints:0.0})");
        }
    }

    /// <summary>
    /// Remembers the Build Points balance from just before the last editor
    /// launch was charged, so a revert to the VAB/SPH can put the balance
    /// back exactly. Static on purpose: it has to survive the scene loads a
    /// revert goes through, and it lives about as long as stock's own revert
    /// state. Restoring the absolute balance (rather than adding the cost
    /// back) means it can't over-refund.
    /// </summary>
    public static class LaunchRevertTracker
    {
        private static double? balanceBeforeLaunch;
        private static bool revertToEditorPending;

        public static void RecordLaunch(double balanceBeforeCharge)
        {
            balanceBeforeLaunch = balanceBeforeCharge;
            revertToEditorPending = false;
        }

        public static void Clear()
        {
            balanceBeforeLaunch = null;
            revertToEditorPending = false;
        }

        public static void MarkRevertToEditor()
        {
            revertToEditorPending = balanceBeforeLaunch.HasValue;
        }

        /// <summary>
        /// True once, on the first scenario load after a revert to the editor,
        /// with the balance to restore. Consumed so it can't apply twice.
        /// </summary>
        public static bool TryConsumeRevertRestore(out double points)
        {
            points = 0;
            if (!revertToEditorPending || !balanceBeforeLaunch.HasValue) return false;

            points = balanceBeforeLaunch.Value;
            Clear();
            return true;
        }
    }

    /// <summary>
    /// Revert to VAB/SPH. Flags the revert so the next scenario load (the
    /// editor's) restores the pre-launch balance. Revert to Launch is
    /// deliberately not patched: the craft is still on the pad and stays paid
    /// for, which is also how stock funds behave.
    ///
    /// Prepare() skips this patch (with a log line) if the target method
    /// isn't found, so a wrong name here can't take the other patches down.
    ///
    /// </summary>
    [HarmonyPatch(typeof(FlightDriver), "RevertToPrelaunch", new Type[] { typeof(EditorFacility) })]
    public static class RevertToEditorPatch
    {
        public static bool Prepare()
        {
            bool found = AccessTools.Method(typeof(FlightDriver), "RevertToPrelaunch",
                new Type[] { typeof(EditorFacility) }) != null;
            if (!found)
                Debug.LogWarning("[BuildPoints] FlightDriver.RevertToPrelaunch(EditorFacility) not found; " +
                                 "Build Points won't be refunded on revert to VAB/SPH");
            return found;
        }

        public static void Prefix()
        {
            if (!BuildPointsScenario.IsActiveForCurrentGame()) return;
            if (!FlightDriver.CanRevertToPrelaunch) return;
            LaunchRevertTracker.MarkRevertToEditor();
        }
    }
}
