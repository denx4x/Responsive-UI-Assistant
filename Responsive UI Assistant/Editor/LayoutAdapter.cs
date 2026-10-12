using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace ResponsiveUIAssistant
{
    public static class LayoutAdapter
    {
        public static Candidate Adaptation(Result r)
        {
            var parent = r.parent.rect;
            var preferred = r.proposed;
            var min = preferred.min; var max = preferred.max;
            float coverage = r.bounds.width / parent.width;
            // Use measured column boundaries rather than stretching both columns across the entire parent.
            bool panel = coverage >= .2f && (r.target.childCount > 0 || r.bounds.height / parent.height >= .7f);
            if (panel)
            {
                min.x = Mathf.Clamp01((r.bounds.xMin - parent.xMin) / parent.width);
                max.x = Mathf.Clamp01((r.bounds.xMax - parent.xMin) / parent.width);
            }
            return new Candidate(panel ? "Adaptive panel width" : preferred.name, min, max);
        }
        public static List<Result> Plan(RectTransform root)
        {
            var canvas = root ? root.GetComponent<Canvas>() : null;
            if (!canvas || canvas != canvas.rootCanvas || canvas.renderMode == RenderMode.WorldSpace || EditorUtility.IsPersistent(root))
                throw new InvalidOperationException("Adaptasi memerlukan root Canvas screen-space di scene.");
            var list = GeometryCapture.Scan(root, true, .25f, .8f);
            foreach (var r in list.Where(r => r.safety != Safety.Skip))
            {
                var parent = r.parent.rect;
                // Preserve deliberate overflow rather than forcing it back inside an unknown layout.
                if (r.bounds.xMin < parent.xMin - 1 || r.bounds.yMin < parent.yMin - 1 || r.bounds.xMax > parent.xMax + 1 || r.bounds.yMax > parent.yMax + 1)
                { r.safety = Safety.Skip; r.risk = "Baseline extends outside its parent; manual layout required."; continue; }
                r.proposed = Adaptation(r);
                r.safety = Safety.Safe;
            }
            // Stretching an ancestor of a layout driver would resize an unverified dynamic subtree.
            foreach (var r in list.Where(r => r.safety != Safety.Skip).ToList())
                if (r.proposed.max != r.proposed.min && list.Any(child => child.safety == Safety.Skip && child.target.IsChildOf(r.target)))
                { r.safety = Safety.Skip; r.risk = "Dynamic or unsupported descendant needs live layout validation."; }
            return list;
        }
        public static int Apply(RectTransform root, List<Result> plan, bool preserveImages)
        {
            var canvas = root.GetComponent<Canvas>();
            var scaler = root.GetComponent<CanvasScaler>();
            if (!scaler || !scaler.enabled) throw new InvalidOperationException("Canvas harus memiliki Canvas Scaler aktif.");
            if (plan.Any(r => !r.target || !r.state.Matches(r.target) || (r.parent.rect.size - r.parentSize).sqrMagnitude > .01f))
                throw new InvalidOperationException("Layout berubah sejak perhitungan. Jalankan adaptasi lagi.");
            if (root.rect.height <= 0 || root.rect.width <= 0) throw new InvalidOperationException("Ukuran Canvas tidak valid.");
            var chosen = plan.Where(r => r.safety != Safety.Skip).ToList();
            // Preserve Aspect is a design choice. Sprite dimensions cannot tell content from a panel fill.
            // Retain the legacy argument for callers, but never override any Image setting.
            LayoutSnapshotManager.Save(chosen, scaler);
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Adapt UI layout resolution");
            try
            {
                int count = 0;
                foreach (var r in chosen)
                {
                    if (!RectTransformApplier.Apply(r, .01f)) throw new InvalidOperationException("Geometry tidak dapat dipertahankan: " + r.target.name);
                    if (ResponsiveUIBatch.Changed(r)) count++;
                }
                // Retain logical design coordinates and font sizes. Match height gives uniform visual scaling.
                Vector2 designSize = root.rect.size;
                Undo.RecordObject(scaler, "Adapt Canvas scaling");
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = designSize;
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 1;
                Record(scaler);
                return count;
            }
            catch { Undo.FlushUndoRecordObjects(); Undo.RevertAllDownToGroup(group); throw; }
            finally { Undo.CollapseUndoOperations(group); }
        }
        static void Record(UnityEngine.Object obj)
        {
            if (PrefabUtility.IsPartOfPrefabInstance(obj)) PrefabUtility.RecordPrefabInstancePropertyModifications(obj);
            EditorUtility.SetDirty(obj);
        }
    }
}
