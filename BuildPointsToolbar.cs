using System;
using System.Collections;
using KSP.UI.Screens;
using UnityEngine;

namespace BuildPoints
{
    /// <summary>
    /// Adds the mod's button to the stock App Launcher (the row of icons
    /// top-right of the screen) and owns the window it opens: a two-tab
    /// window at the Space Center (Settings / BuildPoint), or a Cost
    /// Breakdown window for the current craft (VAB/SPH). One class handles
    /// both since they're mutually exclusive by scene and share the toggle
    /// button plumbing.
    ///
    /// Settings tab: edits this save's own copy of the settings
    /// (BuildPointsScenario.Instance.Settings). Apply updates that copy and
    /// immediately saves the game so the values land in the save's
    /// persistent file. GlobalSettings.cfg is never touched — it only
    /// supplies the defaults new saves start from, and "Reset to Global
    /// Defaults" re-applies it to this save. startingPoints isn't shown
    /// here: it only matters when a save is first created, so it's set in
    /// GlobalSettings.cfg. The accrual and storage values here are BASE
    /// values; the BuildPoint tab's purchases are added on top.
    ///
    /// BuildPoint tab: a table of base / purchased / VAB-SPH bonus / total
    /// for accrual and max storage, plus controls to buy more of each with
    /// funds or science (rules live in BuildPointsUpgrades; purchases are
    /// stored by BuildPointsScenario). Buying takes effect immediately and
    /// needs no Apply.
    ///
    /// The VAB/SPH Cost window remembers its position and whether it was
    /// open, separately for the VAB and the SPH. That state lives in
    /// BuildPointsScenario (VabCostWindow / SphCostWindow) so it is saved
    /// with the game, and is restored when the button is added on entering
    /// the editor (see RestoreEditorWindowState).
    ///
    /// The Space Center window remembers its position only (see
    /// RestoreSpaceCenterWindowPosition); it always starts closed. The
    /// position is stored in BuildPointsScenario (SpaceCenterWindowX/Y).
    ///
    /// Button creation: rather than reacting to the launcher's "ready"
    /// event (which appeared to fire more than once in the editor and
    /// produced duplicate buttons), each instance waits for the launcher
    /// to be ready and adds its button exactly once. A static reference to
    /// the last button added means that even if more than one instance of
    /// this addon exists, only one button survives. The same rule decides
    /// which instance draws the window: only the one that owns the surviving
    /// button (see OwnsSharedButton).
    ///
    /// Uses stock ApplicationLauncher rather than a third-party toolbar
    /// mod (Blizzy's Toolbar, etc.) so nothing extra needs installing.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.EveryScene, false)]
    public class BuildPointsToolbar : MonoBehaviour
    {
        private ApplicationLauncherButton button;
        private static ApplicationLauncherButton sharedButton; // one button across all instances

        // True while we remove a button ourselves, so any callback that fires
        // during removal can't be mistaken for the player closing the window.
        private static bool suppressStateWrites;

        private bool buttonAdded;
        private bool showWindow;
        private Rect windowRect = new Rect(300, 100, 360, 0);
        private bool relevantScene;

        // --- Space Center window layout ---
        private const float SpaceCenterWindowWidth = 440f;
        private const float FieldLabelWidth = 290f;
        private const float TableLabelWidth = 110f;
        private const float TableColumnWidth = 72f;

        // --- Tabs (Space Center only) ---
        private static readonly string[] TabNames = { "Settings", "BuildPoint" };
        private int selectedTab;

        // --- Settings tab state ---
        // Everything is staged here and only copied into this save's
        // settings when Apply is pressed.
        private string[] textFields;
        private bool pendingShowDisplay;
        private bool pendingInstantSkip;
        private bool pendingIncludeFuel;

        private static readonly string[] FieldLabels =
        {
            "Base accrual (BP / day)",
            "Facility bonus per upgrade (%)",
            "Base storage cap (BP)",
            "Constant cost per launch (BP)",
            "Cost per unit of vessel funds cost",
            "Cost per part (BP)",
            "Cost per tonne (BP)",
            "Minimum craft cost (BP)",
            "Recovery refund (%)",
            // Upgrade prices (BuildPoint tab) start here — keep in sync with UpgradeFieldStart.
            "Funds per +1 BP/day of accrual",
            "Science per +1 BP/day of accrual",
            "Funds per +1 BP of max storage",
            "Science per +1 BP of max storage"
        };
        private const int UpgradeFieldStart = 9;

        // --- BuildPoint tab state ---
        private string rateAmountText = "1";
        private string capacityAmountText = "10";

        private GUIStyle boldLabel;
        private GUIStyle boldNumber;
        private GUIStyle numberLabel;

        public void Awake()
        {
            BuildPointsConfig.EnsureLoaded();

            GameScenes scene = HighLogic.LoadedScene;
            relevantScene = scene == GameScenes.SPACECENTER || scene == GameScenes.EDITOR;
            Debug.Log($"[BuildPoints] Toolbar Awake, scene={scene}, id={GetInstanceID()}");
            if (!relevantScene) return;

            StartCoroutine(AddButtonWhenReady());
        }

        public void OnDestroy()
        {
            RemoveButton();
        }

        private IEnumerator AddButtonWhenReady()
        {
            while (!ApplicationLauncher.Ready || ApplicationLauncher.Instance == null
                   || BuildPointsScenario.Instance == null)
                yield return null;

            if (buttonAdded) yield break;
            buttonAdded = true;
            AddButton();
        }

        private void AddButton()
        {
            if (!relevantScene || ApplicationLauncher.Instance == null) return;

            Debug.Log($"[BuildPoints] Toolbar AddButton, id={GetInstanceID()}");

            // Only ever one button: clear any left over from another instance
            // before adding ours.
            if (sharedButton != null)
            {
                RemoveButtonQuietly(sharedButton);
                sharedButton = null;
            }
            button = null;
            showWindow = false;

            // NOTE: Texture created by Claude...Update if you want something prettier
            Texture2D icon = LoadIcon();

            ApplicationLauncher.AppScenes scenes = HighLogic.LoadedScene == GameScenes.SPACECENTER
                ? ApplicationLauncher.AppScenes.SPACECENTER
                : ApplicationLauncher.AppScenes.VAB | ApplicationLauncher.AppScenes.SPH;

            button = ApplicationLauncher.Instance.AddModApplication(
                OnToggleOn, OnToggleOff, null, null, null, null, scenes, icon);
            sharedButton = button;

            RestoreEditorWindowState();
            RestoreSpaceCenterWindowPosition();
        }

        /// <summary>
        /// Puts the VAB/SPH cost window back where the player last left it in
        /// this editor, and reopens it if it was open. SetTrue(false) shows the
        /// button as pressed without firing OnToggleOn.
        /// </summary>
        private void RestoreEditorWindowState()
        {
            if (!HighLogic.LoadedSceneIsEditor || button == null) return;

            var state = GetEditorWindowState();
            if (state == null) return;

            windowRect.x = state.x;
            windowRect.y = state.y;

            if (state.open)
            {
                showWindow = true;
                button.SetTrue(false);
            }
        }

        /// <summary>
        /// Puts the Space Center window back where the player last left it,
        /// on the tab they last had selected. Only position and tab are
        /// restored; the window itself always starts closed (showWindow stays
        /// false and the button isn't pressed).
        /// </summary>
        private void RestoreSpaceCenterWindowPosition()
        {
            if (HighLogic.LoadedScene != GameScenes.SPACECENTER) return;

            var scenario = BuildPointsScenario.Instance;
            if (scenario == null) return;

            windowRect.x = scenario.SpaceCenterWindowX;
            windowRect.y = scenario.SpaceCenterWindowY;
            selectedTab = Mathf.Clamp(scenario.SpaceCenterTab, 0, TabNames.Length - 1);
        }

        // NOTE: verify EditorDriver.editorFacility against 1.12.5 — it should be
        // the static EditorFacility (VAB or SPH) of the editor currently loaded.
        private static BuildPointsWindowState GetEditorWindowState()
        {
            var scenario = BuildPointsScenario.Instance;
            return scenario?.GetCostWindowState(EditorDriver.editorFacility);
        }

        /// <summary>
        /// Finds BuildPointsIcon.png among the textures KSP loaded from GameData.
        /// Matches on the file name rather than a full URL, so it still works if
        /// the mod folder is renamed or the PNG is moved into a subfolder.
        /// Falls back to a plain white square (and logs a warning) if it's missing.
        /// </summary>
        private static Texture2D LoadIcon()
        {
            const string iconName = "BuildPointsIcon";

            if (GameDatabase.Instance != null)
            {
                foreach (var info in GameDatabase.Instance.databaseTexture)
                {
                    if (info.name == iconName || info.name.EndsWith("/" + iconName))
                        return info.texture;
                }
            }

            Debug.LogWarning("[BuildPoints] " + iconName + ".png not found in GameData; using placeholder icon.");
            return Texture2D.whiteTexture;
        }

        private void RemoveButton()
        {
            if (button == null) return;

            // Only the button that's still live needs removing. A duplicate
            // instance's button was already removed when a later instance
            // added its own.
            if (sharedButton == button)
            {
                if (ApplicationLauncher.Instance != null) RemoveButtonQuietly(button);
                sharedButton = null;
            }
            button = null;
        }

        private static void RemoveButtonQuietly(ApplicationLauncherButton b)
        {
            suppressStateWrites = true;
            try { ApplicationLauncher.Instance.RemoveModApplication(b); }
            finally { suppressStateWrites = false; }
        }

        private void OnToggleOn()
        {
            SetWindowOpen(true);
            if (HighLogic.LoadedScene == GameScenes.SPACECENTER) RefreshFieldsFromSettings();
        }

        private void OnToggleOff() => SetWindowOpen(false);

        private void CloseWindow()
        {
            SetWindowOpen(false);
            button?.SetFalse(false); // makeCall = false, so OnToggleOff doesn't fire; SetWindowOpen already handled it
        }

        /// <summary>
        /// The one place the window's open/closed state changes. In the editor
        /// it's also recorded in the scenario (per VAB/SPH) so it's restored
        /// next visit.
        /// </summary>
        private void SetWindowOpen(bool open)
        {
            showWindow = open;

            if (suppressStateWrites || !HighLogic.LoadedSceneIsEditor) return;

            var state = GetEditorWindowState();
            if (state != null) state.open = open;
        }

        private void RefreshFieldsFromSettings()
        {
            var s = BuildPointsScenario.GetActiveSettings();
            textFields = new[]
            {
                s.baseAccrualPerDay.ToString("0.###"),
                s.facilityLevelBonusPercent.ToString("0.###"),
                s.capacity.ToString("0.###"),
                s.constantCost.ToString("0.###"),
                s.fundsCostWeight.ToString("0.###"),
                s.costPerPart.ToString("0.###"),
                s.massCostWeight.ToString("0.###"),
                s.minimumCraftCost.ToString("0.###"),
                s.recoveryRefundPercent.ToString("0.###"),
                s.fundsPerRateIncrease.ToString("0.###"),
                s.sciencePerRateIncrease.ToString("0.###"),
                s.fundsPerCapacityIncrease.ToString("0.###"),
                s.sciencePerCapacityIncrease.ToString("0.###")
            };
            pendingShowDisplay = s.showBuildPointsDisplay;
            pendingInstantSkip = s.useInstantTimeSkip;
            pendingIncludeFuel = s.includeFuelInCost;
        }

        /// <summary>
        /// True only for the instance whose button is the one currently in the
        /// launcher. The editor creates several instances of this addon (the
        /// reason there used to be several buttons); each one restores the
        /// saved open state, so without this check every instance would draw
        /// its own copy of the window. Only the owner draws it and writes its
        /// position back.
        /// </summary>
        private bool OwnsSharedButton => button != null && button == sharedButton;

        public void OnGUI()
        {
            if (!showWindow || !OwnsSharedButton) return;

            if (HighLogic.LoadedScene == GameScenes.SPACECENTER)
            {
                windowRect = GUILayout.Window(GetInstanceID(), windowRect, DrawSpaceCenterWindow,
                    "Build Points", GUILayout.Width(SpaceCenterWindowWidth));

                // Keep it on screen (also covers a save made at a larger resolution).
                windowRect.x = Mathf.Clamp(windowRect.x, 0f, Screen.width - windowRect.width);
                windowRect.y = Mathf.Clamp(windowRect.y, 0f, Screen.height - windowRect.height);

                // Remember where it is, so it comes back here next time.
                BuildPointsScenario.Instance?.SetSpaceCenterWindowPosition(windowRect.x, windowRect.y);
            }
            else if (HighLogic.LoadedSceneIsEditor)
            {
                windowRect = GUILayout.Window(GetInstanceID(), windowRect, DrawCostWindow, "Build Points Cost");

                // Keep it on screen (also covers a save made at a larger resolution).
                windowRect.x = Mathf.Clamp(windowRect.x, 0f, Screen.width - windowRect.width);
                windowRect.y = Mathf.Clamp(windowRect.y, 0f, Screen.height - windowRect.height);

                var state = GetEditorWindowState();
                if (state != null)
                {
                    state.x = windowRect.x;
                    state.y = windowRect.y;
                }
            }
        }

        // ------------------------------------------------------------
        // Space Center window: tab bar + Settings / BuildPoint tabs
        // ------------------------------------------------------------

        private void DrawSpaceCenterWindow(int id)
        {
            GUILayout.BeginVertical();

            int newTab = GUILayout.Toolbar(selectedTab, TabNames);
            if (newTab != selectedTab)
            {
                selectedTab = newTab;
                BuildPointsScenario.Instance?.SetSpaceCenterTab(newTab); // remembered for next time
            }
            GUILayout.Space(6);

            if (selectedTab == 0) DrawSettingsTab();
            else DrawBuildPointTab();

            GUILayout.Space(8);
            if (GUILayout.Button("Close"))
            {
                CloseWindow();
            }

            GUILayout.EndVertical();
            GUI.DragWindow();
        }

        // ------------------------------------------------------------
        // Settings tab — edits THIS SAVE's settings (the base values)
        // ------------------------------------------------------------

        private void DrawSettingsTab()
        {
            if (textFields == null) RefreshFieldsFromSettings();

            GUILayout.Label("Applies to this save only. Accrual and storage here are BASE values; " +
                            "upgrades bought on the BuildPoint tab are added on top. Edit " +
                            "GlobalSettings.cfg (in the mod's folder) to change what new saves start " +
                            "with, including starting Build Points.");
            GUILayout.Space(6);

            pendingShowDisplay = GUILayout.Toggle(pendingShowDisplay, "Show Build Points display");

            GUILayout.Space(8);

            for (int i = 0; i < FieldLabels.Length; i++)
            {
                if (i == UpgradeFieldStart)
                {
                    GUILayout.Space(6);
                    GUILayout.Label("BuildPoint tab upgrade prices (0 = can't buy with that currency)");
                }

                GUILayout.BeginHorizontal();
                GUILayout.Label(FieldLabels[i], GUILayout.Width(FieldLabelWidth));
                textFields[i] = GUILayout.TextField(textFields[i]);
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(4);
            pendingIncludeFuel = GUILayout.Toggle(pendingIncludeFuel,
                "Include fuel/resources in cost and mass (off = dry only)");
            pendingInstantSkip = GUILayout.Toggle(pendingInstantSkip,
                "Use instant time-skip (off = real TimeWarp)");

            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Apply"))
            {
                ApplyAndSave();
            }
            if (GUILayout.Button("Revert"))
            {
                RefreshFieldsFromSettings();
            }
            GUILayout.EndHorizontal();

            if (GUILayout.Button("Reset to Global Defaults"))
            {
                BuildPointsScenario.Instance?.ResetSettingsToGlobalDefaults();
                RefreshFieldsFromSettings();
                SaveToPersistentFile();
            }
        }

        private void ApplyAndSave()
        {
            var s = BuildPointsScenario.GetActiveSettings();
            s.baseAccrualPerDay = ParseOrKeep(textFields[0], s.baseAccrualPerDay);
            s.facilityLevelBonusPercent = ParseOrKeep(textFields[1], s.facilityLevelBonusPercent);
            s.capacity = ParseOrKeep(textFields[2], s.capacity);
            s.constantCost = ParseOrKeep(textFields[3], s.constantCost);
            s.fundsCostWeight = ParseOrKeep(textFields[4], s.fundsCostWeight);
            s.costPerPart = ParseOrKeep(textFields[5], s.costPerPart);
            s.massCostWeight = ParseOrKeep(textFields[6], s.massCostWeight);
            s.minimumCraftCost = ParseOrKeep(textFields[7], s.minimumCraftCost);
            s.recoveryRefundPercent = ParseOrKeep(textFields[8], s.recoveryRefundPercent);
            s.fundsPerRateIncrease = ParseOrKeep(textFields[9], s.fundsPerRateIncrease);
            s.sciencePerRateIncrease = ParseOrKeep(textFields[10], s.sciencePerRateIncrease);
            s.fundsPerCapacityIncrease = ParseOrKeep(textFields[11], s.fundsPerCapacityIncrease);
            s.sciencePerCapacityIncrease = ParseOrKeep(textFields[12], s.sciencePerCapacityIncrease);
            s.showBuildPointsDisplay = pendingShowDisplay;
            s.useInstantTimeSkip = pendingInstantSkip;
            s.includeFuelInCost = pendingIncludeFuel;

            SaveToPersistentFile();

            // Re-sync the boxes from the (possibly-rejected) parsed values,
            // so a bad entry snaps back to the last good number instead of
            // silently keeping invalid text on screen.
            RefreshFieldsFromSettings();
        }

        /// <summary>
        /// Saves the game right now so this save's settings (stored by
        /// BuildPointsScenario.OnSave) land in its persistent file instead of
        /// waiting for the next autosave/quicksave. If the save fails, the
        /// new values still apply this session and get written on the next
        /// normal save.
        ///
        /// NOTE: verify GamePersistence.SaveGame's signature and
        /// Game.Updated() against your KSP version. Updated() is there so the
        /// scenario's OnSave has definitely run before the file is written;
        /// after your first Apply, open persistent.sfs and confirm the
        /// BuildPointsScenario "Settings" node shows the new values.
        /// </summary>
        private static void SaveToPersistentFile()
        {
            bool saved = false;
            try
            {
                HighLogic.CurrentGame.Updated();
                GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE);
                saved = true;
            }
            catch (Exception e)
            {
                Debug.LogError("[BuildPoints] Failed to save persistent file after settings change: " + e);
            }

            ScreenMessages.PostScreenMessage(
                saved
                    ? "Build Points settings saved to this save."
                    : "Settings applied, but the save file couldn't be written (see KSP.log). They'll be saved with the next game save.",
                4f, ScreenMessageStyle.UPPER_CENTER);
        }

        /// <summary>
        /// Text fields have no min/max like the old sliders did — this only
        /// rejects unparsable or negative input. Add per-field clamps here
        /// (e.g. percentages to 0-200) if players manage to type something
        /// that breaks the cost formula in practice.
        /// </summary>
        private static float ParseOrKeep(string text, float fallback)
        {
            return float.TryParse(text, out float parsed) && parsed >= 0f ? parsed : fallback;
        }

        // ------------------------------------------------------------
        // BuildPoint tab — table of totals + buying upgrades
        // ------------------------------------------------------------

        // Styles have to be built from inside OnGUI (GUI.skin isn't valid
        // before that), so they're created on first use.
        private void EnsureStyles()
        {
            if (boldLabel != null) return;

            boldLabel = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            boldNumber = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleRight
            };
            numberLabel = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleRight };
        }

        private void TableRow(bool header, string label, string c1, string c2, string c3, string c4)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, header ? boldLabel : GUI.skin.label, GUILayout.Width(TableLabelWidth));
            GUIStyle cell = header ? boldNumber : numberLabel;
            GUILayout.Label(c1, cell, GUILayout.Width(TableColumnWidth));
            GUILayout.Label(c2, cell, GUILayout.Width(TableColumnWidth));
            GUILayout.Label(c3, cell, GUILayout.Width(TableColumnWidth));
            GUILayout.Label(c4, cell, GUILayout.Width(TableColumnWidth));
            GUILayout.EndHorizontal();
        }

        private void DrawBuildPointTab()
        {
            var scenario = BuildPointsScenario.Instance;
            if (scenario == null)
            {
                GUILayout.Label("Build Points aren't loaded for this save.");
                return;
            }

            EnsureStyles();
            var s = scenario.Settings;

            GUILayout.Label($"Current Build Points: {scenario.CurrentPoints:0.0} / {scenario.GetCapacity():0.#}");
            GUILayout.Space(6);

            // --- Table ---
            double facilityBonusPercent = scenario.GetFacilityBonusFraction() * 100.0;

            TableRow(true, "", "Base", "Purchased", "VAB/SPH", "Total");
            TableRow(false, "Accrual (BP/day)",
                s.baseAccrualPerDay.ToString("0.##"),
                "+" + scenario.RateUpgradePerDay.ToString("0.##"),
                "+" + facilityBonusPercent.ToString("0.#") + "%",
                scenario.GetAccrualPerDay().ToString("0.##"));
            TableRow(false, "Max BP",
                s.capacity.ToString("0.##"),
                "+" + scenario.CapacityUpgrade.ToString("0.##"),
                "+" + facilityBonusPercent.ToString("0.#") + "%",
                scenario.GetCapacity().ToString("0.##"));

            BuildPointsScenario.GetFacilityUpgradeCounts(out int vabUpgrades, out int sphUpgrades);
            GUILayout.Space(2);
            GUILayout.Label(
                $"VAB bonus: +{vabUpgrades * s.facilityLevelBonusPercent:0.#}%   " +
                $"SPH bonus: +{sphUpgrades * s.facilityLevelBonusPercent:0.#}%   (they add together)");
            GUILayout.Label("Totals = (Base + Purchased) x (1 + VAB bonus + SPH bonus), for both accrual and max BP.");

            GUILayout.Space(8);

            // --- Currencies ---
            bool fundsAvailable = BuildPointsUpgrades.IsCurrencyAvailable(BuildPointsCurrency.Funds);
            bool scienceAvailable = BuildPointsUpgrades.IsCurrencyAvailable(BuildPointsCurrency.Science);

            if (!fundsAvailable && !scienceAvailable)
            {
                GUILayout.Label("Upgrades are disabled in Sandbox mode (they're bought with funds or science).");
                return;
            }

            string balances = fundsAvailable
                ? $"Funds: {BuildPointsUpgrades.GetBalance(BuildPointsCurrency.Funds):N0}"
                : "";
            if (fundsAvailable && scienceAvailable) balances += "   ";
            if (scienceAvailable)
                balances += $"Science: {BuildPointsUpgrades.GetBalance(BuildPointsCurrency.Science):0.#}";
            GUILayout.Label(balances);
            GUILayout.Space(6);

            // --- Purchases ---
            DrawUpgradeSection(scenario, BuildPointsUpgradeKind.AccrualRate,
                "Increase accrual rate", "BP/day", ref rateAmountText);
            GUILayout.Space(8);
            DrawUpgradeSection(scenario, BuildPointsUpgradeKind.Capacity,
                "Increase max Build Points", "BP", ref capacityAmountText);
        }

        private void DrawUpgradeSection(BuildPointsScenario scenario, BuildPointsUpgradeKind kind,
            string title, string unit, ref string amountText)
        {
            GUILayout.Label(title, boldLabel);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Increase by", GUILayout.Width(80f));
            amountText = GUILayout.TextField(amountText, GUILayout.Width(70f));
            GUILayout.Label(unit);
            GUILayout.EndHorizontal();

            bool amountOk = double.TryParse(amountText, out double amount)
                            && amount > 0 && !double.IsInfinity(amount);

            // Only offer currencies that exist in this game mode (e.g. no
            // funds button in Science Sandbox). A currency that exists but has
            // a price of 0 still shows, greyed out, via DrawBuyButton.
            GUILayout.BeginHorizontal();
            if (BuildPointsUpgrades.IsCurrencyAvailable(BuildPointsCurrency.Funds))
                DrawBuyButton(scenario, kind, BuildPointsCurrency.Funds, amountOk, amount);
            if (BuildPointsUpgrades.IsCurrencyAvailable(BuildPointsCurrency.Science))
                DrawBuyButton(scenario, kind, BuildPointsCurrency.Science, amountOk, amount);
            GUILayout.EndHorizontal();
        }

        /// <summary>
        /// One "Buy" button for one currency. The label shows the exact price
        /// for the typed amount, and the button is greyed out when that
        /// currency can't be used or can't be afforded.
        /// </summary>
        private void DrawBuyButton(BuildPointsScenario scenario, BuildPointsUpgradeKind kind,
            BuildPointsCurrency currency, bool amountOk, double amount)
        {
            var s = scenario.Settings;
            string name = currency == BuildPointsCurrency.Funds ? "Funds" : "Science";

            bool purchasable = BuildPointsUpgrades.IsPurchasable(s, kind, currency);
            bool canAfford = false;
            string label;

            if (!purchasable)
            {
                label = name + ": unavailable";
            }
            else if (!amountOk)
            {
                label = "Buy with " + name;
            }
            else
            {
                double cost = BuildPointsUpgrades.GetTotalCost(s, kind, currency, amount);
                canAfford = BuildPointsUpgrades.GetBalance(currency) + 1e-6 >= cost;
                label = currency == BuildPointsCurrency.Funds
                    ? $"Buy: {cost:N0} funds"
                    : $"Buy: {cost:0.#} science";
            }

            bool previouslyEnabled = GUI.enabled;
            GUI.enabled = previouslyEnabled && purchasable && amountOk && canAfford;
            if (GUILayout.Button(label))
            {
                BuildPointsUpgrades.TryPurchase(scenario, kind, currency, amount, out string message);
                ScreenMessages.PostScreenMessage(message, 4f, ScreenMessageStyle.UPPER_CENTER);
            }
            GUI.enabled = previouslyEnabled;
        }

        // ------------------------------------------------------------
        // Cost breakdown window (VAB/SPH)
        // ------------------------------------------------------------

        private void DrawCostWindow(int id)
        {
            GUILayout.BeginVertical();

            var scenario = BuildPointsScenario.Instance;
            double current = scenario != null ? scenario.CurrentPoints : 0;
            double cap = scenario != null ? scenario.GetCapacity() : 0;
            GUILayout.Label($"Current Build Points: {current:0.0} / {cap:0}");
            GUILayout.Space(8);

            if (BuildPointsCalculator.TryGetShipCostBreakdown(out var b))
            {
                GUILayout.Label($"Constant:    {b.constant,8:0.0} BP");
                GUILayout.Label($"Funds cost:  {b.funds,8:0.0} BP   ({b.fundsCost:0} funds)");
                GUILayout.Label($"Part count:  {b.partCountCost,8:0.0} BP   ({b.partCount} parts)");
                GUILayout.Label($"Mass:        {b.mass,8:0.0} BP   ({b.massTonnes:0.00} t)");
                if (!BuildPointsScenario.GetActiveSettings().includeFuelInCost)
                    GUILayout.Label("(fuel excluded: dry cost and mass only)");
                GUILayout.Space(4);
                GUILayout.Label($"Total cost:  {b.total,8:0.0} BP");

                GUILayout.Space(4);
                GUILayout.Label(current + 1e-6 < b.total
                    ? $"Short by {b.total - current:0.0} BP."
                    : "Affordable.");
            }
            else
            {
                GUILayout.Label("No vessel in the editor.");
            }

            GUILayout.Space(8);
            if (GUILayout.Button("Close"))
            {
                CloseWindow();
            }

            GUILayout.EndVertical();
            GUI.DragWindow();
        }
    }
}
