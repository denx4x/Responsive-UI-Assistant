using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace ResponsiveUIAssistant
{
    public static class ResolutionSimulator
    {
        public static Vector2 CanvasSize(RectTransform root, Vector2 resolution, Vector2 reference)
        {
            var canvas = root.GetComponentInParent<Canvas>().rootCanvas;
            var scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler && scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize)
            {
                var basis = scaler.referenceResolution;
                float x = resolution.x / Mathf.Max(1, basis.x), y = resolution.y / Mathf.Max(1, basis.y);
                float scale = scaler.screenMatchMode == CanvasScaler.ScreenMatchMode.Expand ? Mathf.Min(x, y) : scaler.screenMatchMode == CanvasScaler.ScreenMatchMode.Shrink ? Mathf.Max(x, y) : Mathf.Pow(2, Mathf.Lerp(Mathf.Log(x, 2), Mathf.Log(y, 2), scaler.matchWidthOrHeight));
                return resolution / Mathf.Max(.0001f, scale);
            }
            return Vector2.Scale((canvas.transform as RectTransform).rect.size, new Vector2(resolution.x / reference.x, resolution.y / reference.y));
        }
        static Vector2 Size(RectTransform t, Canvas canvas, Vector2 canvasDelta, Dictionary<RectTransform, Result> map, bool proposed)
        {
            if (t == canvas.transform) return t.rect.size + canvasDelta;
            var parent = t.parent as RectTransform;
            if (!parent) return t.rect.size;
            var delta = Size(parent, canvas, canvasDelta, map, proposed) - parent.rect.size;
            var span = t.anchorMax - t.anchorMin;
            if (proposed && map.TryGetValue(t, out var r) && r.safety != Safety.Skip) span = r.proposed.max - r.proposed.min;
            return t.rect.size + Vector2.Scale(delta, span);
        }
        public static ResolutionReport Run(RectTransform root, List<Result> results, Vector2 resolution, Vector2 reference, bool proposed = true)
        {
            var report = new ResolutionReport { resolution = resolution };
            var canvas = root.GetComponentInParent<Canvas>().rootCanvas;
            var map = results.ToDictionary(r => r.target);
            var canvasDelta = CanvasSize(root, resolution, reference) - CanvasSize(root, reference, reference);
            foreach (var r in results)
            {
                var delta = Size(r.parent, canvas, canvasDelta, map, proposed) - r.parentSize;
                var min = proposed && r.safety != Safety.Skip ? r.proposed.min : r.state.min;
                var max = proposed && r.safety != Safety.Skip ? r.proposed.max : r.state.max;
                var lo = r.bounds.min + Vector2.Scale(delta, min - r.parent.pivot);
                var hi = r.bounds.max + Vector2.Scale(delta, max - r.parent.pivot);
                var rect = Rect.MinMaxRect(lo.x, lo.y, hi.x, hi.y);
                report.rects[r.target] = rect;
                var parentSize = r.parentSize + delta;
                report.parentSizes[r.parent] = parentSize;
                var parentMin = -Vector2.Scale(parentSize, r.parent.pivot);
                if (r.safety == Safety.Skip) continue;
                if (rect.xMin < parentMin.x - .5f || rect.yMin < parentMin.y - .5f || rect.xMax > parentMin.x + parentSize.x + .5f || rect.yMax > parentMin.y + parentSize.y + .5f)
                    Add(report, r.target.name + ": outside parent bounds", 15, r.target);
                if (rect.width <= 0 || rect.height <= 0 || (r.bounds.width > 1 && rect.width / r.bounds.width > 3) || (r.bounds.height > 1 && rect.height / r.bounds.height > 3))
                    Add(report, r.target.name + ": abnormal stretch", 15, r.target);
            }
            var overlapTargets = results.Where(r => r.safety != Safety.Skip).ToList();
            for (int i = 0; i < overlapTargets.Count; i++) for (int j = i + 1; j < overlapTargets.Count; j++)
            {
                var a = overlapTargets[i]; var b = overlapTargets[j];
                if (a.parent != b.parent || a.safety == Safety.Skip || b.safety == Safety.Skip) continue;
                // Existing overlap can be intentional (backgrounds, decoration). Report only new/worsening overlap.
                float before = Overlap(a.bounds, b.bounds), after = Overlap(report.rects[a.target], report.rects[b.target]);
                if (after > before + .02f) Add(report, a.target.name + " ↔ " + b.target.name + ": increased overlap", after > .2f ? 15 : 5, a.target, b.target);
            }
            var scaler = canvas.GetComponent<CanvasScaler>();
            if (!scaler || scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize) Add(report, "Canvas Scaler is not Scale With Screen Size; physical scaling is approximate.", 10);
            report.score = Mathf.Max(0, report.score);
            return report;
        }
        static void Add(ResolutionReport report, string text, int penalty, params RectTransform[] targets)
        {
            report.issues.Add(text); report.score -= penalty;
            foreach (var target in targets) report.penalties[target] = Penalty(report, target) + penalty;
        }
        static int Penalty(ResolutionReport report, RectTransform target) => report.penalties.TryGetValue(target, out var value) ? value : 0;
        public static bool DoesNotRegress(ResolutionReport before, ResolutionReport after) => after.penalties.All(pair => pair.Value <= Penalty(before, pair.Key));
        static int CandidateIssues(Result result, List<ResolutionReport> baseline, List<ResolutionReport> simulated)
        {
            int count = 0;
            for (int i = 0; i < simulated.Count; i++)
            {
                // Own failures matter, but pre-existing failures on unrelated objects do not.
                count += Penalty(simulated[i], result.target) / 5;
                foreach (var pair in simulated[i].penalties)
                    if (pair.Key != result.target) count += Mathf.Max(0, pair.Value - Penalty(baseline[i], pair.Key)) / 5;
            }
            return count;
        }
        static float Overlap(Rect a, Rect b)
        {
            float area = Mathf.Max(0, Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin)) * Mathf.Max(0, Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin));
            return area / Mathf.Max(1, Mathf.Min(a.width * a.height, b.width * b.height));
        }
        public static List<ResolutionReport> Evaluate(RectTransform root, List<Result> results, List<Vector2> resolutions, Vector2 reference, bool allowPrefabOverrides = false, System.Action<int, int> progress = null)
        {
            var baseline = resolutions.Select(res => Run(root, results, res, reference, false)).ToList();
            var eligible = results.Where(r => r.safety != Safety.Skip).ToList();
            int processed = 0;
            foreach (var r in eligible)
            {
                progress?.Invoke(processed++, eligible.Count);
                foreach (var candidate in r.candidates)
                {
                    r.proposed = candidate;
                    candidate.issues = CandidateIssues(r, baseline, resolutions.Select(res => Run(root, results, res, reference)).ToList());
                }
                var ranked = r.candidates.OrderByDescending(c => c.score - c.issues * 15).ToList();
                r.proposed = ranked[0];
                float gap = ranked[0].score - ranked[0].issues * 15 - (ranked[1].score - ranked[1].issues * 15);
                r.confidence = Mathf.Clamp(r.proposed.score - r.proposed.issues * 5, 0, 99);
                r.safety = gap >= 10 && r.confidence >= 80 && r.proposed.issues == 0 && r.proposed.name != "Relative" && (allowPrefabOverrides || !UnityEditor.PrefabUtility.IsPartOfPrefabInstance(r.target)) ? Safety.Safe : Safety.Review;
            }
            progress?.Invoke(eligible.Count, eligible.Count);
            var reports = resolutions.Select(res => Run(root, results, res, reference)).ToList();
            var scaler = root.GetComponentInParent<Canvas>().rootCanvas.GetComponent<CanvasScaler>();
            foreach (var r in results.Where(r => r.safety == Safety.Safe))
                if (!scaler || scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize || CandidateIssues(r, baseline, reports) > 0) r.safety = Safety.Review;
            return reports;
        }
    }
}
