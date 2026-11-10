// EOSAT-1 VR - small building blocks for the main menu: a quad and a box mesh made in
// code (no primitives, no colliders), text, flat panels and a clickable button that is hit
// by plain ray-against-rectangle maths, so it works the same with a mouse or a controller.
using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Eosat1
{
    public static class MenuKit
    {
        public const int OrderPanel = 0;
        public const int OrderButton = 1;
        public const int OrderText = 2;
        public const int OrderMarker = 3;
        public const int OrderFade = 100;

        static Mesh quad;
        static Mesh box;

        /// <summary>1 x 1 in the XY plane, centred, seen from -Z (like Unity's Quad).</summary>
        public static Mesh Quad
        {
            get
            {
                if (quad == null) quad = BuildQuad();
                return quad;
            }
        }

        /// <summary>1 x 1 x 1, centred, with flat-shaded faces.</summary>
        public static Mesh Box
        {
            get
            {
                if (box == null) box = BuildBox();
                return box;
            }
        }

        static Mesh BuildQuad()
        {
            var m = new Mesh();
            m.name = "Menu Quad";
            m.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f),
            };
            m.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            m.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            m.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            m.RecalculateBounds();
            return m;
        }

        static Mesh BuildBox()
        {
            // each face: normal n, and two in-plane axes u, v with u x v = -n (same layout as the quad)
            Vector3[] n = { Vector3.back, Vector3.forward, Vector3.right, Vector3.left, Vector3.up, Vector3.down };
            Vector3[] u = { Vector3.right, Vector3.left, Vector3.forward, Vector3.back, Vector3.right, Vector3.right };
            Vector3[] v = { Vector3.up, Vector3.up, Vector3.up, Vector3.up, Vector3.forward, Vector3.back };
            var verts = new Vector3[24];
            var norms = new Vector3[24];
            var uvs = new Vector2[24];
            var tris = new int[36];
            for (int f = 0; f < 6; f++)
            {
                int b = f * 4;
                Vector3 c = n[f] * 0.5f;
                verts[b + 0] = c - u[f] * 0.5f - v[f] * 0.5f;
                verts[b + 1] = c + u[f] * 0.5f - v[f] * 0.5f;
                verts[b + 2] = c - u[f] * 0.5f + v[f] * 0.5f;
                verts[b + 3] = c + u[f] * 0.5f + v[f] * 0.5f;
                uvs[b + 0] = new Vector2(0f, 0f);
                uvs[b + 1] = new Vector2(1f, 0f);
                uvs[b + 2] = new Vector2(0f, 1f);
                uvs[b + 3] = new Vector2(1f, 1f);
                for (int k = 0; k < 4; k++) norms[b + k] = n[f];
                int t = f * 6;
                tris[t + 0] = b + 0;
                tris[t + 1] = b + 2;
                tris[t + 2] = b + 1;
                tris[t + 3] = b + 2;
                tris[t + 4] = b + 3;
                tris[t + 5] = b + 1;
            }
            var m = new Mesh();
            m.name = "Menu Box";
            m.vertices = verts;
            m.normals = norms;
            m.uv = uvs;
            m.triangles = tris;
            m.RecalculateBounds();
            return m;
        }

        // ------------------------------------------------------------------ objects
        public static Transform Group(string name, Transform parent, Vector3 localPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            return go.transform;
        }

        public static MeshRenderer MeshObject(string name, Transform parent, Mesh mesh, Material mat,
                                              Vector3 localPos, Vector3 localScale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return r;
        }

        public static MeshRenderer BoxObject(string name, Transform parent, Material mat, Vector3 centre, Vector3 size)
        {
            return MeshObject(name, parent, Box, mat, centre, size);
        }

        public static Material NewMaterial(Shader shader, string name)
        {
            var m = new Material(shader);
            m.name = name;
            return m;
        }

        /// <summary>A flat rounded rectangle (EOSAT1/UiPanel) of the given size in metres.</summary>
        public static MeshRenderer Panel(string name, Transform parent, Shader panelShader, Vector3 localPos, Vector2 size,
                                         Color top, Color bottom, Color border, float borderWidth, float radius, int order)
        {
            Material mat = NewMaterial(panelShader, name);
            mat.SetColor("_Color", top);
            mat.SetColor("_Color2", bottom);
            mat.SetColor("_BorderColor", border);
            mat.SetFloat("_Border", borderWidth);
            mat.SetFloat("_Radius", radius);
            mat.SetFloat("_Aspect", size.x / Mathf.Max(size.y, 1e-4f));
            MeshRenderer r = MeshObject(name, parent, Quad, mat, localPos, new Vector3(size.x, size.y, 1f));
            r.sortingOrder = order;
            return r;
        }

        /// <summary>
        /// Text that reads correctly when you look along +Z. lineHeight is the height of one
        /// line in metres (capital letters come out at about 70% of it).
        /// </summary>
        public static TextMesh Text(string name, Transform parent, Vector3 localPos, string text, float lineHeight,
                                    Color color, TextAnchor anchor, Font font, Material textMaterial)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var tm = go.AddComponent<TextMesh>();
            const int fontSize = 64;
            tm.font = font;
            tm.fontSize = fontSize;
            tm.characterSize = lineHeight * 10f / fontSize;
            tm.anchor = anchor;
            if (anchor == TextAnchor.UpperLeft || anchor == TextAnchor.MiddleLeft || anchor == TextAnchor.LowerLeft)
                tm.alignment = TextAlignment.Left;
            else if (anchor == TextAnchor.UpperRight || anchor == TextAnchor.MiddleRight || anchor == TextAnchor.LowerRight)
                tm.alignment = TextAlignment.Right;
            else
                tm.alignment = TextAlignment.Center;
            tm.richText = true;
            tm.color = color;
            tm.text = text;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = textMaterial;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortingOrder = OrderText;
            return tm;
        }

        /// <summary>Put a space between the letters, for headings.</summary>
        public static string Spaced(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new System.Text.StringBuilder(s.Length * 2);
            for (int i = 0; i < s.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(s[i]);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Where a ray meets the z = 0 plane of a transform, in that transform's local
        /// coordinates. False when the ray is parallel to the plane or points away from it.
        /// </summary>
        public static bool RayToLocalPlane(Ray ray, Transform plane, out Vector3 local, out float distance)
        {
            local = Vector3.zero;
            distance = 0f;
            Vector3 normal = plane.forward;
            float denom = Vector3.Dot(ray.direction, normal);
            if (Mathf.Abs(denom) < 1e-5f) return false;
            distance = Vector3.Dot(plane.position - ray.origin, normal) / denom;
            if (distance <= 0f) return false;
            local = plane.InverseTransformPoint(ray.origin + ray.direction * distance);
            return true;
        }
    }

    /// <summary>A rectangular button on a menu screen.</summary>
    public sealed class MenuButton
    {
        public readonly Transform Root;
        public readonly Vector2 Size;
        public readonly TextMesh Label;
        public Action OnClick;
        public bool Enabled = true;

        readonly Material material;
        readonly Color fillTop, fillBottom, fillHover, border, borderHover, textColor, textHover;
        float hover;

        public MenuButton(string name, Transform parent, Vector3 localPos, Vector2 size, string label, float textHeight,
                          Shader panelShader, Font font, Material textMaterial, Color accent, bool primary)
        {
            Size = size;
            Root = MenuKit.Group(name, parent, localPos);
            if (primary)
            {
                fillTop = Color.Lerp(new Color(0.02f, 0.06f, 0.1f, 1f), accent, 0.32f);
                fillBottom = Color.Lerp(new Color(0.02f, 0.06f, 0.1f, 1f), accent, 0.16f);
            }
            else
            {
                fillTop = new Color(0.06f, 0.1f, 0.16f, 1f);
                fillBottom = new Color(0.035f, 0.06f, 0.1f, 1f);
            }
            fillHover = Color.Lerp(new Color(0.03f, 0.08f, 0.12f, 1f), accent, primary ? 0.75f : 0.5f);
            border = new Color(accent.r, accent.g, accent.b, primary ? 0.95f : 0.55f);
            borderHover = Color.Lerp(accent, Color.white, 0.6f);
            textColor = primary ? Color.white : new Color(0.82f, 0.9f, 0.96f, 1f);
            textHover = Color.white;

            MeshRenderer bg = MenuKit.Panel("Background", Root, panelShader, Vector3.zero, size,
                                            fillTop, fillBottom, border, 0.045f, 0.22f, MenuKit.OrderButton);
            material = bg.sharedMaterial;
            Label = MenuKit.Text("Label", Root, new Vector3(0f, 0f, -0.01f), label, textHeight, textColor,
                                 TextAnchor.MiddleCenter, font, textMaterial);
        }

        public bool Visible { get { return Root.gameObject.activeInHierarchy; } }

        /// <summary>Does the ray hit this button's rectangle?</summary>
        public bool Hit(Ray ray, out float distance)
        {
            Vector3 local;
            if (!MenuKit.RayToLocalPlane(ray, Root, out local, out distance)) return false;
            return Mathf.Abs(local.x) <= Size.x * 0.5f && Mathf.Abs(local.y) <= Size.y * 0.5f;
        }

        /// <summary>Ease towards the hovered or resting look.</summary>
        public void Animate(bool hovered, float dt)
        {
            float target = hovered && Enabled ? 1f : 0f;
            hover = Mathf.MoveTowards(hover, target, dt * 9f);
            float dim = Enabled ? 1f : 0.45f;
            material.SetColor("_Color", Color.Lerp(fillTop, fillHover, hover) * new Color(dim, dim, dim, 1f));
            material.SetColor("_Color2", Color.Lerp(fillBottom, fillHover * new Color(0.7f, 0.7f, 0.7f, 1f), hover) * new Color(dim, dim, dim, 1f));
            material.SetColor("_BorderColor", Color.Lerp(border, borderHover, hover));
            Label.color = Color.Lerp(textColor, textHover, hover) * new Color(dim, dim, dim, 1f);
            float s = 1f + 0.035f * hover;
            Root.localScale = new Vector3(s, s, 1f);
        }
    }
}
