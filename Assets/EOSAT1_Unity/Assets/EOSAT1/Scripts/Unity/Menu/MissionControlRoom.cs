// EOSAT-1 VR - the mission control room behind the main menu: floor, walls, ceiling
// lights, a plinth under the wall screens and two rows of operator consoles with live
// monitors and chairs. Everything is made from boxes and quads at run time, so the menu
// scene itself stays almost empty. You stand at the origin, at the back, facing +Z.
using UnityEngine;

namespace Eosat1
{
    public static class MissionControlRoom
    {
        public const float HalfWidth = 7f;
        public const float Front = 6f;       // the wall with the big screens
        public const float Back = -4.5f;
        public const float Height = 5.4f;

        static readonly Color[] MonitorColors =
        {
            new Color(0.3f, 0.8f, 1f),      // cyan
            new Color(0.35f, 0.95f, 0.6f),  // green
            new Color(1f, 0.75f, 0.3f),     // amber
            new Color(0.45f, 0.65f, 1f),    // blue
        };

        sealed class Mats
        {
            public Material Floor, Wall, Ceiling, Trim, Desk, DeskTop, Chair, Glow, LightPanel;
            public Shader Console;
        }

        public static Transform Build(Transform parent, Shader roomShader, Shader consoleShader, Color accent)
        {
            SetLighting(accent);

            var m = new Mats();
            m.Floor = RoomMaterial(roomShader, "Room Floor", new Color(0.16f, 0.18f, 0.22f), Color.black, 0.55f, 1f);
            m.Wall = RoomMaterial(roomShader, "Room Wall", new Color(0.26f, 0.29f, 0.36f), Color.black, 0.05f, 0.5f);
            m.Ceiling = RoomMaterial(roomShader, "Room Ceiling", new Color(0.12f, 0.13f, 0.16f), Color.black, 0f, 0.5f);
            m.Trim = RoomMaterial(roomShader, "Room Trim", new Color(0.08f, 0.09f, 0.11f), Color.black, 0.3f, 0f);
            m.Desk = RoomMaterial(roomShader, "Room Desk", new Color(0.22f, 0.24f, 0.29f), Color.black, 0.2f, 0f);
            m.DeskTop = RoomMaterial(roomShader, "Room Desk Top", new Color(0.09f, 0.1f, 0.12f), Color.black, 0.6f, 0f);
            m.Chair = RoomMaterial(roomShader, "Room Chair", new Color(0.07f, 0.08f, 0.1f), Color.black, 0.15f, 0f);
            m.Glow = RoomMaterial(roomShader, "Room Glow Strip", Color.black,
                                  new Color(accent.r * 0.8f, accent.g * 0.8f, accent.b * 0.8f), 0f, 0f);
            m.LightPanel = RoomMaterial(roomShader, "Room Light Panel", Color.black, new Color(0.5f, 0.56f, 0.66f), 0f, 0f);
            m.Console = consoleShader;

            Transform root = MenuKit.Group("Mission Control Room", parent, Vector3.zero);
            float depth = Front - Back;
            float midZ = (Front + Back) * 0.5f;
            float width = HalfWidth * 2f;

            // shell
            MenuKit.BoxObject("Floor", root, m.Floor, new Vector3(0f, -0.05f, midZ), new Vector3(width + 0.4f, 0.1f, depth + 0.4f));
            MenuKit.BoxObject("Ceiling", root, m.Ceiling, new Vector3(0f, Height + 0.05f, midZ), new Vector3(width + 0.4f, 0.1f, depth + 0.4f));
            MenuKit.BoxObject("Front Wall", root, m.Wall, new Vector3(0f, Height * 0.5f, Front + 0.1f), new Vector3(width + 0.4f, Height, 0.2f));
            MenuKit.BoxObject("Back Wall", root, m.Wall, new Vector3(0f, Height * 0.5f, Back - 0.1f), new Vector3(width + 0.4f, Height, 0.2f));
            MenuKit.BoxObject("Left Wall", root, m.Wall, new Vector3(-HalfWidth - 0.1f, Height * 0.5f, midZ), new Vector3(0.2f, Height, depth + 0.4f));
            MenuKit.BoxObject("Right Wall", root, m.Wall, new Vector3(HalfWidth + 0.1f, Height * 0.5f, midZ), new Vector3(0.2f, Height, depth + 0.4f));

            // glowing skirting where the walls meet the floor
            MenuKit.BoxObject("Skirting Front", root, m.Glow, new Vector3(0f, 0.05f, Front - 0.02f), new Vector3(width, 0.03f, 0.03f));
            MenuKit.BoxObject("Skirting Back", root, m.Glow, new Vector3(0f, 0.05f, Back + 0.02f), new Vector3(width, 0.03f, 0.03f));
            MenuKit.BoxObject("Skirting Left", root, m.Glow, new Vector3(-HalfWidth + 0.02f, 0.05f, midZ), new Vector3(0.03f, 0.03f, depth));
            MenuKit.BoxObject("Skirting Right", root, m.Glow, new Vector3(HalfWidth - 0.02f, 0.05f, midZ), new Vector3(0.03f, 0.03f, depth));

            // ceiling light panels
            for (int row = 0; row < 3; row++)
            {
                float z = -2.6f + row * 3.4f;
                for (int col = -1; col <= 1; col++)
                    MenuKit.BoxObject("Ceiling Light", root, m.LightPanel, new Vector3(col * 4f, Height - 0.015f, z), new Vector3(1.8f, 0.03f, 0.45f));
            }

            // pilasters with a light strip along the side walls
            for (int i = 0; i < 4; i++)
            {
                float z = Back + 1.5f + i * 2.8f;
                for (int side = -1; side <= 1; side += 2)
                {
                    MenuKit.BoxObject("Pilaster", root, m.Trim, new Vector3(side * (HalfWidth - 0.1f), Height * 0.5f, z), new Vector3(0.2f, Height, 0.3f));
                    MenuKit.BoxObject("Pilaster Light", root, m.Glow, new Vector3(side * (HalfWidth - 0.21f), 2.5f, z), new Vector3(0.02f, 3.4f, 0.04f));
                }
            }

            // plinth under the wall screens
            MenuKit.BoxObject("Screen Plinth", root, m.Trim, new Vector3(0f, 0.45f, Front - 0.35f), new Vector3(11.5f, 0.9f, 0.7f));
            MenuKit.BoxObject("Plinth Light", root, m.Glow, new Vector3(0f, 0.9f, Front - 0.71f), new Vector3(11.5f, 0.02f, 0.02f));

            // two rows of consoles with an aisle down the middle
            var rng = new System.Random(7);
            float[] rowZ = { 2.2f, 4.0f };
            float[] consoleX = { -4.7f, -2.2f, 2.2f, 4.7f };
            for (int r = 0; r < rowZ.Length; r++)
                for (int c = 0; c < consoleX.Length; c++)
                    BuildConsole(root, new Vector3(consoleX[c], 0f, rowZ[r]), m, rng);

            return root;
        }

        /// <summary>The fake lights the Room shader reads: the three wall screens and a soft overhead fill.</summary>
        static void SetLighting(Color accent)
        {
            var pos = new Vector4[4];
            var col = new Vector4[4];
            pos[0] = new Vector4(0f, 2.95f, 5.2f, 0.035f);
            col[0] = new Vector4(0.12f + accent.r * 0.7f, 0.12f + accent.g * 0.6f, 0.12f + accent.b * 0.8f, 1f);
            pos[1] = new Vector4(-4.2f, 2.9f, 4.6f, 0.06f);
            col[1] = new Vector4(0.15f, 0.36f, 0.48f, 1f);
            pos[2] = new Vector4(4.2f, 2.9f, 4.6f, 0.06f);
            col[2] = new Vector4(0.15f, 0.36f, 0.48f, 1f);
            pos[3] = new Vector4(0f, 4.8f, -0.5f, 0.05f);
            col[3] = new Vector4(0.46f, 0.44f, 0.4f, 1f);
            Shader.SetGlobalVectorArray("_EosatRoomLightPos", pos);
            Shader.SetGlobalVectorArray("_EosatRoomLightCol", col);
            Shader.SetGlobalVector("_EosatRoomAmbientSky", new Vector4(0.2f, 0.23f, 0.3f, 1f));
            Shader.SetGlobalVector("_EosatRoomAmbientGround", new Vector4(0.07f, 0.08f, 0.11f, 1f));
        }

        static Material RoomMaterial(Shader shader, string name, Color color, Color emission, float gloss, float grid)
        {
            Material mat = MenuKit.NewMaterial(shader, name);
            mat.SetColor("_Color", color);
            mat.SetColor("_Emission", emission);
            mat.SetFloat("_Gloss", gloss);
            mat.SetFloat("_Grid", grid);
            return mat;
        }

        static void BuildConsole(Transform root, Vector3 at, Mats m, System.Random rng)
        {
            Transform c = MenuKit.Group("Console", root, at);

            // desk: the operator sits on the -Z side and looks towards the wall screens
            MenuKit.BoxObject("Desk Top", c, m.DeskTop, new Vector3(0f, 0.74f, 0f), new Vector3(2f, 0.05f, 0.85f));
            MenuKit.BoxObject("Desk Front", c, m.Desk, new Vector3(0f, 0.36f, 0.4f), new Vector3(2f, 0.72f, 0.05f));
            MenuKit.BoxObject("Desk Side L", c, m.Desk, new Vector3(-0.975f, 0.36f, 0f), new Vector3(0.05f, 0.72f, 0.85f));
            MenuKit.BoxObject("Desk Side R", c, m.Desk, new Vector3(0.975f, 0.36f, 0f), new Vector3(0.05f, 0.72f, 0.85f));
            MenuKit.BoxObject("Riser", c, m.Desk, new Vector3(0f, 0.81f, 0.31f), new Vector3(2f, 0.09f, 0.22f));
            MenuKit.BoxObject("Edge Light", c, m.Glow, new Vector3(0f, 0.708f, -0.42f), new Vector3(1.9f, 0.012f, 0.012f));
            MenuKit.BoxObject("Keyboard", c, m.Trim, new Vector3(0f, 0.775f, -0.14f), new Vector3(0.44f, 0.02f, 0.15f));

            // three monitors, the outer two turned in towards the operator
            for (int i = -1; i <= 1; i++)
            {
                Transform pivot = MenuKit.Group("Monitor", c, new Vector3(i * 0.64f, 0.855f, 0.31f));
                pivot.localRotation = Quaternion.Euler(12f, i * 14f, 0f);
                MenuKit.BoxObject("Stand", pivot, m.Trim, new Vector3(0f, 0.02f, 0.03f), new Vector3(0.08f, 0.06f, 0.04f));
                MenuKit.BoxObject("Bezel", pivot, m.Trim, new Vector3(0f, 0.22f, 0.012f), new Vector3(0.6f, 0.38f, 0.02f));
                Material screen = MenuKit.NewMaterial(m.Console, "Console Screen");
                screen.SetColor("_Color", MonitorColors[rng.Next(MonitorColors.Length)]);
                screen.SetFloat("_Seed", (float)(rng.NextDouble() * 50.0));
                MenuKit.MeshObject("Screen", pivot, MenuKit.Quad, screen, new Vector3(0f, 0.22f, -0.002f), new Vector3(0.56f, 0.34f, 1f));
            }

            // chair
            const float cz = -0.85f;
            MenuKit.BoxObject("Chair Seat", c, m.Chair, new Vector3(0f, 0.47f, cz), new Vector3(0.5f, 0.07f, 0.5f));
            MenuKit.BoxObject("Chair Back", c, m.Chair, new Vector3(0f, 0.82f, cz - 0.24f), new Vector3(0.46f, 0.6f, 0.06f));
            MenuKit.BoxObject("Chair Post", c, m.Trim, new Vector3(0f, 0.24f, cz), new Vector3(0.07f, 0.42f, 0.07f));
            MenuKit.BoxObject("Chair Foot A", c, m.Trim, new Vector3(0f, 0.03f, cz), new Vector3(0.56f, 0.04f, 0.07f));
            MenuKit.BoxObject("Chair Foot B", c, m.Trim, new Vector3(0f, 0.03f, cz), new Vector3(0.07f, 0.04f, 0.56f));
        }
    }
}
