using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ResponsiveUIAssistant
{
    public static class ResponsiveUIBatch
    {
        public static bool Changed(Result r) => (r.state.min - r.proposed.min).sqrMagnitude > .000001f || (r.state.max - r.proposed.max).sqrMagnitude > .000001f;
        public static List<Result> NonRegressingSubset(RectTransform root, List<Result> results, List<Result> chosen, List<Vector2> resolutions, Vector2 reference)
        {
            var baseline = resolutions.Select(res => ResolutionSimulator.Run(root, results, res, reference, false)).ToList();
            var originals = results.ToDictionary(r => r, r => r.proposed);
            var accepted = new List<Result>();
            try
            {
                // Greedy batch verification: every accepted addition is tested with unselected anchors unchanged.
                foreach (var r in results) r.proposed = new Candidate("Unchanged", r.state.min, r.state.max);
                foreach (var r in chosen)
                {
                    r.proposed = originals[r];
                    bool safe = true;
                    for (int i = 0; i < resolutions.Count; i++)
                        if (!ResolutionSimulator.DoesNotRegress(baseline[i], ResolutionSimulator.Run(root, results, resolutions[i], reference))) { safe = false; break; }
                    if (safe) accepted.Add(r);
                    else { r.proposed = new Candidate("Unchanged", r.state.min, r.state.max); r.safety = Safety.Review; }
                }
                return accepted;
            }
            finally { foreach (var pair in originals) pair.Key.proposed = pair.Value; }
        }
    }
}
