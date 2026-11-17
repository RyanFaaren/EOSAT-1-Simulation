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
        const string KeyHost = "eosat1.menu.simHost";
        const string KeyModel = "eosat1.menu.satModel";

        /// <summary>The simulator's TCP port (smo-vr/1).</summary>
        public const int SimPort = 8100;
        public const string ThisComputer = "127.0.0.1";
        /// <summary>The course's simulator in the cloud (ICD smo-vr/1 issue 2): WebSocket, no sign-in, needs the internet.</summary>
        public const string CloudAddress = "wss://ops.c2o.live/vr";

        /// <summary>
        /// The address of the laptop running the simulator or vr_stub.py, used by Live link.
        /// Typed on the menu's Settings screen and remembered between runs.
        /// </summary>
        public static string SimHost
        {
            get
            {
                string h = PlayerPrefs.GetString(KeyHost, ThisComputer);
                return IsValidHost(h) ? h : ThisComputer;
            }
            set { if (IsValidHost(value)) PlayerPrefs.SetString(KeyHost, value); }
        }

        /// <summary>An IPv4 address, or a ws:// / wss:// address such as the cloud one.</summary>
        public static bool IsValidHost(string s)
        {
            if (IsValidAddress(s)) return true;
            if (!Eosat1.Core.TcpFeedClient.IsWebSocketAddress(s) || s.Length > 200 || s.IndexOf(' ') >= 0) return false;
            System.Uri u;
            return System.Uri.TryCreate(s, System.UriKind.Absolute, out u) && !string.IsNullOrEmpty(u.Host);
        }

        /// <summary>Short name for the settings row: the IP, or "C2O CLOUD".</summary>
        public static string HostLabel(string h)
        {
            if (h == CloudAddress) return "C2O CLOUD";
            if (Eosat1.Core.TcpFeedClient.IsWebSocketAddress(h))
            {
                System.Uri u;
                return System.Uri.TryCreate(h, System.UriKind.Absolute, out u) ? u.Host : h;
            }
            return h;
        }

        /// <summary>A whole IPv4 address: four numbers 0-255 with dots, e.g. 192.168.1.42.</summary>
        public static bool IsValidAddress(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            string[] parts = s.Split('.');
            if (parts.Length != 4) return false;
            foreach (string p in parts)
            {
                if (p.Length < 1 || p.Length > 3) return false;
                int n = 0;
                foreach (char c in p)
                {
                    if (c < '0' || c > '9') return false;
                    n = n * 10 + (c - '0');
                }
                if (n > 255) return false;
            }
            return true;
        }

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

        /// <summary>
        /// Which satellite to draw. True (the default): the course's 6U CubeSat (ICD smo-vr/1 issue 2,
        /// section 6b: 0.100 x 0.226 x 0.366 m bus, fixed flap wings, 4 UHF elements on the zenith deck).
        /// False: the detailed EOS SAT-1 model from the scene. Read live by CubeSatModel, so it also
        /// works when the demo scene is opened directly.
        /// </summary>
        public static bool UseCubeSat
        {
            get { return PlayerPrefs.GetInt(KeyModel, 1) != 0; }
            set { PlayerPrefs.SetInt(KeyModel, value ? 1 : 0); }
        }

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
            // the address only matters for Live link; Sandbox and Replay never use it
            if (Feed == FeedKind.Tcp)
            {
                app.tcpHost = SimHost;
                app.tcpPort = SimPort;
            }
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
