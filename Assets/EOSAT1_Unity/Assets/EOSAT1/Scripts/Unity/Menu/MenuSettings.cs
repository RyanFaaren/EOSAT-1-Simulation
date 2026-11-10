// EOSAT-1 VR - the choices made on the main menu's Settings page. They are remembered
// between runs (PlayerPrefs) and handed to the simulation only when it was started from
// the menu, so opening the demo scene directly still uses what is set in the Inspector.
using UnityEngine;

namespace Eosat1
{
    public static class MenuSettings
    {
        public const string MenuSceneName = "EOSAT1_Menu";
        public const string SimulationSceneName = "EOSAT1_Demo";

        /// <summary>True once Start was pressed on the menu in this run.</summary>
        public static bool LaunchedFromMenu;

        static readonly float[] SpeedChoices = { 1f, 10f, 60f };
        static readonly float[] SnapChoices = { 30f, 45f, 90f };

        const string KeyFeed = "eosat1.menu.feed";
        const string KeySpeed = "eosat1.menu.speed";
        const string KeyInfo = "eosat1.menu.infoPanel";
        const string KeyHints = "eosat1.menu.hints";
        const string KeySnap = "eosat1.menu.snapTurn";

        // ------------------------------------------------------------------ values
        public static FeedKind Feed
        {
            get { return (FeedKind)Mathf.Clamp(PlayerPrefs.GetInt(KeyFeed, 0), 0, 2); }
            set { PlayerPrefs.SetInt(KeyFeed, (int)value); }
        }

        public static int SpeedChoice
        {
            get { return Mathf.Clamp(PlayerPrefs.GetInt(KeySpeed, 0), 0, SpeedChoices.Length - 1); }
            set { PlayerPrefs.SetInt(KeySpeed, Wrap(value, SpeedChoices.Length)); }
        }

        public static float StartSpeed { get { return SpeedChoices[SpeedChoice]; } }

        public static bool ShowInfoPanel
        {
            get { return PlayerPrefs.GetInt(KeyInfo, 1) != 0; }
            set { PlayerPrefs.SetInt(KeyInfo, value ? 1 : 0); }
        }

        public static bool ShowHints
        {
            get { return PlayerPrefs.GetInt(KeyHints, 1) != 0; }
            set { PlayerPrefs.SetInt(KeyHints, value ? 1 : 0); }
        }

        public static int SnapChoice
        {
            get { return Mathf.Clamp(PlayerPrefs.GetInt(KeySnap, 0), 0, SnapChoices.Length - 1); }
            set { PlayerPrefs.SetInt(KeySnap, Wrap(value, SnapChoices.Length)); }
        }

        public static float SnapTurnDegrees { get { return SnapChoices[SnapChoice]; } }

        // ------------------------------------------------------------------ menu helpers
        public static void NextFeed() { Feed = (FeedKind)Wrap((int)Feed + 1, 3); }
        public static void NextSpeed() { SpeedChoice = SpeedChoice + 1; }
        public static void NextSnap() { SnapChoice = SnapChoice + 1; }

        public static string FeedLabel
        {
            get
            {
                switch (Feed)
                {
                    case FeedKind.Replay: return "REPLAY";
                    case FeedKind.Tcp: return "LIVE LINK";
                    default: return "SANDBOX";
                }
            }
        }

        public static string FeedDescription
        {
            get
            {
                switch (Feed)
                {
                    case FeedKind.Replay: return "Plays the recorded one-orbit pass";
                    case FeedKind.Tcp: return "Course simulator over TCP, port 8100";
                    default: return "Built-in simulator, takes commands";
                }
            }
        }

        public static string SpeedLabel { get { return StartSpeed.ToString("0") + "x"; } }
        public static string SnapLabel { get { return SnapTurnDegrees.ToString("0") + " DEG"; } }

        public static void Save()
        {
            PlayerPrefs.Save();
        }

        // ------------------------------------------------------------------ simulation side
        /// <summary>
        /// Called by EosatApp.Awake, before the link file and command-line overrides
        /// (so those still win).
        /// </summary>
        public static void ApplyTo(EosatApp app)
        {
            if (!LaunchedFromMenu || app == null) return;
            app.feed = Feed;
            if (app.speedSteps != null && app.speedSteps.Length > 0)
            {
                float want = StartSpeed;
                int best = 0;
                for (int i = 0; i < app.speedSteps.Length; i++)
                    if (Mathf.Abs(app.speedSteps[i] - want) < Mathf.Abs(app.speedSteps[best] - want)) best = i;
                app.speedIndex = best;
            }
            if (app.infoPanel != null)
            {
                app.infoPanel.showOnScreen = ShowInfoPanel;
                app.infoPanel.showHelp = ShowHints;
            }
        }

        static int Wrap(int i, int count)
        {
            return ((i % count) + count) % count;
        }
    }
}
