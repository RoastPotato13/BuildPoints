using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace BuildPoints
{
    /// <summary>
    /// Loads the mod's default settings from GlobalSettings.cfg, which sits in
    /// the mod's own folder, one level above Plugins (see ConfigPath).
    /// That file ships with the mod and is read-only as far as the mod is
    /// concerned: it is never generated or written to. In-game changes
    /// (the Space Center toolbar's Apply button) go into the current save's
    /// own copy of the settings, stored in that save's persistent file by
    /// BuildPointsScenario.
    ///
    /// Two things live here:
    ///   Defaults — the values from GlobalSettings.cfg. New saves are seeded
    ///              from these, and startingPoints is read from here.
    ///   Settings — the values actually in effect right now: the loaded
    ///              save's own copy if a save is loaded, otherwise Defaults.
    ///              This is what the rest of the mod should read.
    ///
    /// If the file is missing or unreadable, Defaults stays at the built-in
    /// values in BuildPointsSettingsValues and a warning goes to KSP.log.
    /// </summary>
    public static class BuildPointsConfig
    {
        private const string NodeName = "BuildPointsSettings";

        private static bool loaded;

        /// <summary>The values from GlobalSettings.cfg. Never null.</summary>
        public static BuildPointsSettingsValues Defaults { get; private set; } = new BuildPointsSettingsValues();

        /// <summary>Settings in effect right now — see class remarks.</summary>
        public static BuildPointsSettingsValues Settings
        {
            get
            {
                EnsureLoaded();
                return BuildPointsScenario.Instance?.Settings ?? Defaults;
            }
        }

        private static string configPath;

        /// <summary>
        /// GlobalSettings.cfg sits one folder above the folder this DLL is in
        /// (mod folder / Plugins / BuildPoints.dll), so it's found wherever
        /// the mod folder is, whatever it's named. Falls back to
        /// GameData/BuildPoints/ if the DLL's own location can't be determined.
        /// </summary>
        private static string ConfigPath => configPath ??= ResolveConfigPath();

        private static string ResolveConfigPath()
        {
            try
            {
                string dllPath = Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(dllPath))
                {
                    string pluginsDir = Path.GetDirectoryName(dllPath);
                    string modDir = pluginsDir != null ? Path.GetDirectoryName(pluginsDir) : null;
                    if (modDir != null) return Path.Combine(modDir, "GlobalSettings.cfg");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[BuildPoints] Couldn't locate the plugin folder, falling back to GameData/BuildPoints: " + e);
            }

            return Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "BuildPoints", "GlobalSettings.cfg");
        }

        /// <summary>Loads once; safe to call from anywhere that touches Defaults.</summary>
        public static void EnsureLoaded()
        {
            if (loaded) return;
            Load();
        }

        /// <summary>
        /// (Re-)reads GlobalSettings.cfg from disk into Defaults. Called at
        /// bootstrap, and by "Reset to Global Defaults" so that action picks
        /// up any hand edits made since the game started.
        /// </summary>
        public static void Load()
        {
            loaded = true;
            var fresh = new BuildPointsSettingsValues();

            try
            {
                if (!File.Exists(ConfigPath))
                {
                    Debug.LogWarning("[BuildPoints] GlobalSettings.cfg not found at " + ConfigPath +
                                     " — using built-in defaults.");
                }
                else
                {
                    ConfigNode file = ConfigNode.Load(ConfigPath);
                    ConfigNode node = file?.GetNode(NodeName) ?? file;
                    if (node != null) fresh.Load(node);
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[BuildPoints] Failed to load GlobalSettings.cfg, using built-in defaults: " + e);
            }

            Defaults = fresh;
        }
    }
}
