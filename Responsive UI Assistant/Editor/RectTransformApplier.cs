using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace ResponsiveUIAssistant
{
    public static class RectTransformApplier
    {
        public static bool Apply(Result r, float tolerance)
        {
            var t = r.target;
            if (!t || r.safety == Safety.Skip || RiskAnalyzer.SkipReason(t) != null || !r.state.Matches(t)) return false;
            var before = new Vector3[4]; t.GetWorldCorners(before);
            var state = RectState.Capture(t);
            Undo.RecordObject(t, "Apply responsive anchors");
            var bounds = GeometryCapture.Bounds(t, r.parent);
            t.anchorMin = r.proposed.min; t.anchorMax = r.proposed.max;
            var anchorLo = r.parent.rect.min + Vector2.Scale(r.parent.rect.size, t.anchorMin);
            var anchorHi = r.parent.rect.min + Vector2.Scale(r.parent.rect.size, t.anchorMax);
            t.offsetMin = bounds.min - anchorLo;
            t.offsetMax = bounds.max - anchorHi;
            var after = new Vector3[4]; t.GetWorldCorners(after);
            for (int i = 0; i < 4; i++)
            {
                float distance = Vector3.Distance(before[i], after[i]);
                if (distance > tolerance || float.IsNaN(distance) || float.IsInfinity(distance)) { state.Restore(t); return false; }
            }
            Record(t); return true;
        }
        public static void Record(RectTransform t)
        {
            if (PrefabUtility.IsPartOfPrefabInstance(t)) PrefabUtility.RecordPrefabInstancePropertyModifications(t);
            EditorUtility.SetDirty(t);
        }
    }
    public static class LayoutSnapshotManager
    {
        [Serializable] sealed class ComponentState { public string id; public Vector2 reference; public int mode, screenMode; public float match; public bool aspect; }
        [Serializable] sealed class Snapshot { public List<RectState> states = new List<RectState>(); public List<ComponentState> components = new List<ComponentState>(); }
        public static string Path => System.IO.Path.GetFullPath("Library/ResponsiveUIAssistantSnapshot.json");
        public static void Save(IEnumerable<Result> results, CanvasScaler scaler = null, IEnumerable<Image> images = null)
        {
            var snapshot = new Snapshot();
            foreach (var r in results) if (r.target) snapshot.states.Add(RectState.Capture(r.target));
            if (scaler) snapshot.components.Add(new ComponentState { id = GlobalObjectId.GetGlobalObjectIdSlow(scaler).ToString(), reference = scaler.referenceResolution, mode = (int)scaler.uiScaleMode, screenMode = (int)scaler.screenMatchMode, match = scaler.matchWidthOrHeight });
            if (images != null) foreach (var image in images) if (image) snapshot.components.Add(new ComponentState { id = GlobalObjectId.GetGlobalObjectIdSlow(image).ToString(), aspect = image.preserveAspect });
            File.WriteAllText(Path, JsonUtility.ToJson(snapshot, true));
        }
        public static int Restore()
        {
            if (!File.Exists(Path)) return 0;
            var snapshot = JsonUtility.FromJson<Snapshot>(File.ReadAllText(Path));
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); int count = 0;
            foreach (var state in snapshot.states)
            {
                if (!GlobalObjectId.TryParse(state.id, out var id)) continue;
                var t = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as RectTransform;
                if (!t || EditorUtility.IsPersistent(t)) continue;
                Undo.RecordObject(t, "Restore responsive layout snapshot"); state.Restore(t); RectTransformApplier.Record(t); count++;
            }
            if (snapshot.components != null) foreach (var state in snapshot.components)
            {
                if (!GlobalObjectId.TryParse(state.id, out var id)) continue;
                var obj = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id);
                if (!obj || EditorUtility.IsPersistent(obj)) continue;
                Undo.RecordObject(obj, "Restore adapted layout settings");
                if (obj is CanvasScaler scaler)
                {
                    scaler.referenceResolution = state.reference; scaler.uiScaleMode = (CanvasScaler.ScaleMode)state.mode;
                    scaler.screenMatchMode = (CanvasScaler.ScreenMatchMode)state.screenMode; scaler.matchWidthOrHeight = state.match;
                }
                else if (obj is Image image) image.preserveAspect = state.aspect;
                if (PrefabUtility.IsPartOfPrefabInstance(obj)) PrefabUtility.RecordPrefabInstancePropertyModifications(obj);
                EditorUtility.SetDirty(obj);
            }
            Undo.CollapseUndoOperations(group); return count;
        }
        public static int RestoreImageAspectOnly()
        {
            if (!File.Exists(Path)) return 0;
            var snapshot = JsonUtility.FromJson<Snapshot>(File.ReadAllText(Path));
            if (snapshot.components == null) return 0;
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(), count = 0;
            foreach (var state in snapshot.components)
            {
                if (!GlobalObjectId.TryParse(state.id, out var id)) continue;
                var image = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as Image;
                if (!image || EditorUtility.IsPersistent(image) || image.preserveAspect == state.aspect) continue;
                Undo.RecordObject(image, "Restore original Preserve Aspect"); image.preserveAspect = state.aspect;
                if (PrefabUtility.IsPartOfPrefabInstance(image)) PrefabUtility.RecordPrefabInstancePropertyModifications(image);
                EditorUtility.SetDirty(image); count++;
            }
            Undo.CollapseUndoOperations(group); return count;
        }
    }
}
