using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace ResponsiveUIAssistant
{
    public static class RiskAnalyzer
    {
        public static string SkipReason(RectTransform t)
        {
            var canvas = t.GetComponentInParent<Canvas>();
            if (!canvas || canvas.rootCanvas.renderMode == RenderMode.WorldSpace) return "Requires a screen-space Canvas.";
            if (canvas != canvas.rootCanvas) return "Nested Canvas hierarchy is excluded from this simulator.";
            if (EditorUtility.IsPersistent(t)) return "Prefab source assets are not editable here.";
            if (t.drivenByObject) return "RectTransform is driven by another component.";
            if (t.GetComponent<ContentSizeFitter>() || t.GetComponent<AspectRatioFitter>() || t.GetComponent<LayoutGroup>()) return "Layout component controls geometry.";
            var layout = t.parent ? t.parent.GetComponent<LayoutGroup>() : null;
            var element = t.GetComponent<LayoutElement>();
            if (layout && layout.isActiveAndEnabled && !(element && element.ignoreLayout)) return "Parent Layout Group controls this child.";
            if (t.GetComponentInParent<Animator>() || t.GetComponentInParent<Animation>()) return "Animation in this hierarchy can control geometry.";
            for (Transform p = t; p != null; p = p.parent)
            {
                if (p != t && (p.GetComponent<ContentSizeFitter>() || p.GetComponent<AspectRatioFitter>() || p.GetComponent<LayoutGroup>())) return "Ancestor layout system requires live layout simulation.";
                // Screen-space Canvas scale/rotation is managed by Unity and the camera.
                // Its local coordinate system remains valid; only descendants require identity transforms.
                if (p == canvas.rootCanvas.transform) break;
                if (Quaternion.Angle(p.localRotation, Quaternion.identity) > .01f || (p.localScale - Vector3.one).sqrMagnitude > .000001f) return "Rotated or scaled hierarchy requires manual review outside this simulator.";
            }
            if (!t.gameObject.activeInHierarchy) return "Inactive geometry cannot be verified.";
            if (t.GetComponent<Canvas>()) return "Nested Canvas boundary is excluded.";
            return null;
        }
    }
    public static class GeometryCapture
    {
        public static Rect Bounds(RectTransform t, RectTransform parent)
        {
            var corners = new Vector3[4]; t.GetWorldCorners(corners);
            var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            foreach (var corner in corners) { Vector2 p = parent.InverseTransformPoint(corner); min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
        public static List<Result> Scan(RectTransform root, bool children, float edge, float stretch)
        {
            var list = new List<Result>();
            foreach (var t in root.GetComponentsInChildren<RectTransform>(true))
            {
                if (t == root || (!children && t.parent != root)) continue;
                var parent = t.parent as RectTransform;
                if (!parent) continue;
                var r = new Result { target = t, parent = parent, state = RectState.Capture(t), bounds = Bounds(t, parent), parentSize = parent.rect.size };
                r.risk = RiskAnalyzer.SkipReason(t);
                if (parent.rect.width <= 0 || parent.rect.height <= 0 || !Finite(r.bounds)) r.risk = "Invalid or zero parent geometry.";
                r.candidates = AnchorScorer.Generate(r, edge, stretch);
                r.proposed = r.candidates.OrderByDescending(c => c.score).First();
                r.safety = r.risk != null ? Safety.Skip : Safety.Review;
                r.reason = $"Parent: {parent.name}; margins L {r.bounds.xMin - parent.rect.xMin:F1}, R {parent.rect.xMax - r.bounds.xMax:F1}, B {r.bounds.yMin - parent.rect.yMin:F1}, T {parent.rect.yMax - r.bounds.yMax:F1}; coverage {r.bounds.width / Mathf.Max(1, parent.rect.width):P0} × {r.bounds.height / Mathf.Max(1, parent.rect.height):P0}.";
                if (PrefabUtility.IsPartOfPrefabInstance(t)) r.reason += " Prefab instance: applying creates overrides; explicit selection required.";
                list.Add(r);
            }
            return list;
        }
        public static bool Finite(Rect rect) => !float.IsNaN(rect.x) && !float.IsInfinity(rect.x) && !float.IsNaN(rect.y) && !float.IsInfinity(rect.y) && !float.IsNaN(rect.width) && !float.IsInfinity(rect.width) && !float.IsNaN(rect.height) && !float.IsInfinity(rect.height);
    }
    public static class AnchorScorer
    {
        public static List<Candidate> Generate(Result r, float edge, float stretch)
        {
            var list = new List<Candidate>();
            var p = r.parent.rect;
            Vector2 n = new Vector2((r.bounds.center.x - p.xMin) / Mathf.Max(1, p.width), (r.bounds.center.y - p.yMin) / Mathf.Max(1, p.height));
            float x = Axis(r.bounds.xMin - p.xMin, p.xMax - r.bounds.xMax, p.width, edge);
            float y = Axis(r.bounds.yMin - p.yMin, p.yMax - r.bounds.yMax, p.height, edge);
            bool sx = r.bounds.width / Mathf.Max(1, p.width) >= stretch, sy = r.bounds.height / Mathf.Max(1, p.height) >= stretch;
            string[] xs = { "Left", "Center", "Right" }, ys = { "Bottom", "Middle", "Top" };
            for (int iy = 0; iy < 3; iy++) for (int ix = 0; ix < 3; ix++)
            {
                var a = new Vector2(ix * .5f, iy * .5f);
                list.Add(new Candidate(ys[iy] + " " + xs[ix], a, a) { score = 90 - 50 * (Mathf.Abs(x - a.x) + Mathf.Abs(y - a.y)) - (sx ? 25 : 0) - (sy ? 25 : 0) });
            }
            list.Add(new Candidate("Horizontal Stretch", new Vector2(0, y), new Vector2(1, y)) { score = sx ? (sy ? 65 : 95) : 35 });
            list.Add(new Candidate("Vertical Stretch", new Vector2(x, 0), new Vector2(x, 1)) { score = sy ? (sx ? 65 : 95) : 35 });
            list.Add(new Candidate("Full Stretch", Vector2.zero, Vector2.one) { score = sx && sy ? 98 : 25 });
            list.Add(new Candidate("Relative", n, n) { score = !sx && !sy && x == .5f && y == .5f && (n - Vector2.one * .5f).magnitude > .12f ? 88 : 60 });
            return list;
        }
        static float Axis(float low, float high, float size, float edge)
        {
            if (Mathf.Min(low, high) <= size * edge && Mathf.Abs(low - high) > size * .1f) return low < high ? 0 : 1;
            return .5f;
        }
    }
}
