using System;
using System.IO;
using UnityEngine;

namespace BuildPoints
{
    /// <summary>
    /// Orchestrates "warp until affordable" using stock TimeWarp instead of
    /// a hard clock jump. The editor scene has no warp loop of its own, so
    /// this: (1) saves the in-progress craft to a named file, (2) exits to
    /// the Space Center, (3) runs a real TimeWarp.WarpTo() right there
    /// (Kerbal Construction Time does the same from GameScenes.SPACECENTER),
    /// and (4) stops there once the target time is reached, with a message
    /// telling the player where to find the saved craft.
    ///
    /// Build Points accrual needs no special handling: it already runs off
    /// elapsed Universal Time in BuildPointsScenario.FixedUpdate, so it
    /// accrues naturally while the warp is running.
    ///
    /// Every early-out in TryRequestWarpAndReturn logs its specific reason
    /// to KSP.log (search for "[BuildPoints] Warp-and-return"), since the
    /// in-game popup can only say that it failed, not why.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.EveryScene, true)]
    public class WarpAndReturnController : MonoBehaviour
    {
        private const string CraftName = "BuildPoints - continue here";

        // Give the Space Center scene a few frames to finish setting up
        // TimeWarp before asking it to warp.
        private const int ReadyFramesBeforeWarp = 10;

        private static WarpAndReturnController instance;

        private bool warpPending;
        private bool awaitingWarpStart;
        private int readyFrames;
        private double targetUT;
        private EditorFacility pendingFacility;

        public void Awake()
        {
            if (instance != null) { Destroy(gameObject); return; }
            instance = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>
        /// Call from the editor popup. Saves the current craft under a
        /// clearly-named file and switches to the Space Center to begin a
        /// real warp toward targetUT. Returns false (does nothing, and logs
        /// why) if it can't safely do that — in particular it never leaves
        /// the editor unless the craft was saved successfully.
        /// </summary>
        public static bool TryRequestWarpAndReturn(double targetUT)
        {
            // If the addon didn't get instantiated for some reason, create
            // the controller now rather than failing. Awake() registers it
            // as the instance and marks it DontDestroyOnLoad.
            if (instance == null)
            {
                Debug.LogWarning("[BuildPoints] Warp-and-return: controller wasn't running; creating it on demand.");
                new GameObject("BuildPoints.WarpAndReturn").AddComponent<WarpAndReturnController>();
            }
            if (instance == null)
            {
                Debug.LogError("[BuildPoints] Warp-and-return: couldn't create the controller.");
                return false;
            }

            if (!HighLogic.LoadedSceneIsEditor)
            {
                Debug.LogError("[BuildPoints] Warp-and-return: not in the editor (scene = " + HighLogic.LoadedScene + ").");
                return false;
            }
            if (EditorLogic.fetch == null || EditorLogic.fetch.ship == null)
            {
                Debug.LogError("[BuildPoints] Warp-and-return: no ship in the editor.");
                return false;
            }

            var ship = EditorLogic.fetch.ship;
            EditorFacility facility = ship.shipFacility;

            try
            {
                string dir = Path.Combine(KSPUtil.ApplicationRootPath, "saves",
                    HighLogic.SaveFolder, "Ships", facility == EditorFacility.SPH ? "SPH" : "VAB");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, CraftName + ".craft");

                // Build the craft's ConfigNode ourselves and write it to an
                // exact path. (ShipConstruction.SaveShip(ship, x) treats x as
                // a craft *name* it builds its own path from, so handing it a
                // full path is what made this fail before.)
                ConfigNode craftNode = ship.SaveShip();

                // The craft browser lists a craft by the name stored inside
                // the file, not by its file name, so make them match.
                craftNode.SetValue("ship", CraftName, true);
                craftNode.Save(path);

                if (!File.Exists(path))
                {
                    Debug.LogError("[BuildPoints] Warp-and-return: craft file wasn't written to " + path);
                    return false;
                }

                Debug.Log("[BuildPoints] Warp-and-return: saved craft to " + path);
            }
            catch (Exception e)
            {
                Debug.LogError("[BuildPoints] Warp-and-return: failed to save the craft: " + e);
                return false;
            }

            instance.pendingFacility = facility;
            instance.targetUT = targetUT;
            instance.readyFrames = 0;
            instance.warpPending = true;
            instance.awaitingWarpStart = true;

            HighLogic.LoadScene(GameScenes.SPACECENTER);
            return true;
        }

        public void Update()
        {
            if (!warpPending) return;
            if (HighLogic.LoadedScene != GameScenes.SPACECENTER) return; // still mid scene-load

            if (awaitingWarpStart)
            {
                if (TimeWarp.fetch == null) return; // scene not fully ready yet this frame
                if (++readyFrames < ReadyFramesBeforeWarp) return;

                Debug.Log("[BuildPoints] Warp-and-return: starting warp to UT " + targetUT);
                try
                {
                    TimeWarp.fetch.WarpTo(targetUT);
                }
                catch (Exception e)
                {
                    Debug.LogError("[BuildPoints] Warp-and-return: TimeWarp.WarpTo threw: " + e);
                    warpPending = false;
                    ScreenMessages.PostScreenMessage(
                        "Couldn't start the warp (see KSP.log). Your craft was saved as \"" + CraftName + "\".",
                        8f, ScreenMessageStyle.UPPER_CENTER);
                    return;
                }
                awaitingWarpStart = false;
                return;
            }

            if (Planetarium.GetUniversalTime() >= targetUT - 0.5)
            {
                TimeWarp.SetRate(0, true); // drop back to 1x, stay right here
                warpPending = false;

                string facilityName = pendingFacility == EditorFacility.SPH ? "SPH" : "VAB";
                ScreenMessages.PostScreenMessage(
                    $"Build Points banked. Head into the {facilityName} and load " +
                    $"\"{CraftName}\" to pick up where you left off.",
                    8f, ScreenMessageStyle.UPPER_CENTER);
            }
        }
    }
}
