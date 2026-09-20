using UnityEngine;

namespace BuildPoints
{
	/// <summary>
	/// Small draggable window showing the current Build Points balance.
	/// Space Center only. Visibility is still controlled by this save's
	/// Settings.showBuildPointsDisplay (the toggle in the Build Points
	/// Settings window, BuildPointsToolbar).
	///
	/// Drawn as a real IMGUI window using the default skin, which is the
	/// same look as the Build Points Cost window in the VAB/SPH
	/// (BuildPointsToolbar.DrawCostWindow): light grey panel with a border
	/// and a title bar.
	///
	/// The window position is kept in BuildPointsScenario (DisplayX/Y), so
	/// it's saved with the game and survives both scene changes and restarts.
	/// </summary>
	[KSPAddon(KSPAddon.Startup.EveryScene, false)]
	public class BuildPointsUI : MonoBehaviour
	{
		private const float WindowWidth = 170f;

		private Rect windowRect = new Rect(0, 0, WindowWidth, 0);
		private bool positionLoaded;
		private GUIStyle valueStyle;
		private bool uiHidden; // stock F2 "hide UI"

		public void Awake()
		{
			if (HighLogic.LoadedScene != GameScenes.SPACECENTER)
			{
				enabled = false; // stops OnGUI from ever running in other scenes
				return;
			}

			GameEvents.onHideUI.Add(OnHideUI);
			GameEvents.onShowUI.Add(OnShowUI);
		}

		public void OnDestroy()
		{
			GameEvents.onHideUI.Remove(OnHideUI);
			GameEvents.onShowUI.Remove(OnShowUI);
		}

		private void OnHideUI() => uiHidden = true;
		private void OnShowUI() => uiHidden = false;

		public void OnGUI()
		{
			if (uiHidden) return;
			if (HighLogic.LoadedScene != GameScenes.SPACECENTER) return;
			if (!BuildPointsScenario.IsActiveForCurrentGame()) return;

			var scenario = BuildPointsScenario.Instance;
			if (scenario == null || !scenario.Settings.showBuildPointsDisplay) return;

			if (!positionLoaded)
			{
				windowRect.x = scenario.DisplayX;
				windowRect.y = scenario.DisplayY;
				positionLoaded = true;
			}

			if (valueStyle == null)
			{
				valueStyle = new GUIStyle(GUI.skin.label)
				{
					fontSize = 14,
					alignment = TextAnchor.MiddleCenter
				};
			}

			windowRect = GUILayout.Window(GetInstanceID(), windowRect, DrawWindow,
				"Build Points", GUILayout.Width(WindowWidth));

			// Keep the window on screen so it can't be dragged out of reach
			// (also covers a save made at a larger resolution).
			windowRect.x = Mathf.Clamp(windowRect.x, 0f, Screen.width - windowRect.width);
			windowRect.y = Mathf.Clamp(windowRect.y, 0f, Screen.height - windowRect.height);

			scenario.SetDisplayPosition(windowRect.x, windowRect.y);
		}

		private void DrawWindow(int id)
		{
			var scenario = BuildPointsScenario.Instance;
			if (scenario != null)
			{
				GUILayout.Label(
					$"{scenario.CurrentPoints:0.0} / {scenario.GetCapacity():0}",
					valueStyle);
			}

			GUI.DragWindow(); // whole window is the drag handle
		}
	}
}