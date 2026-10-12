using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace ResponsiveUIAssistant
{
    public static class ReferenceLayoutInstaller
    {
        public static ReferenceLayoutFrame Find(RectTransform canvas)
        {
            if (!canvas) return null;
            for (int i = 0; i < canvas.childCount; i++)
                if (canvas.GetChild(i).TryGetComponent<ReferenceLayoutFrame>(out var frame)) return frame;
            return null;
        }
        public static ReferenceLayoutFrame Install(RectTransform root, Vector2 sourceResolution)
        {
            var canvas = root ? root.GetComponent<Canvas>() : null;
            if (!canvas || canvas != canvas.rootCanvas || canvas.renderMode == RenderMode.WorldSpace || EditorUtility.IsPersistent(root))
                throw new InvalidOperationException("Pilih root Canvas screen-space di scene.");
            var existing = Find(root);
            if (existing) { existing.Fit(); return existing; }
            if (root.GetComponent<LayoutGroup>() || root.GetComponent<ContentSizeFitter>() || root.GetComponent<AspectRatioFitter>() || root.GetComponent<Animator>())
                throw new InvalidOperationException("Root Canvas dikontrol layout/animasi; frame tidak dapat dipasang dengan aman.");
            var children = root.Cast<Transform>().OfType<RectTransform>().ToList();
            if (children.Count == 0 || root.rect.width <= 0 || root.rect.height <= 0) throw new InvalidOperationException("Canvas tidak memiliki UI atau ukuran yang valid.");
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Install fixed reference UI layout");
            try
            {
                var go = new GameObject("Responsive Layout Frame", typeof(RectTransform));
                Undo.RegisterCreatedObjectUndo(go, "Create reference layout frame");
                var rect = go.GetComponent<RectTransform>(); rect.SetParent(root, false);
                rect.pivot = root.pivot;
                var frame = Undo.AddComponent<ReferenceLayoutFrame>(go);
                frame.Configure(root.rect.size, sourceResolution);
                foreach (var child in children)
                {
                    var state = RectState.Capture(child); var rotation = child.localRotation; var scale = child.localScale;
                    var before = new Vector3[4]; child.GetWorldCorners(before);
                    Undo.SetTransformParent(child, rect, "Move UI into reference frame");
                    Undo.RecordObject(child, "Preserve reference geometry");
                    state.Restore(child); child.localRotation = rotation; child.localScale = scale;
                    var after = new Vector3[4]; child.GetWorldCorners(after);
                    for (int i = 0; i < 4; i++) if (Vector3.Distance(before[i], after[i]) > .01f)
                        throw new InvalidOperationException("Visual berubah saat memasang frame: " + child.name);
                    RectTransformApplier.Record(child);
                }
                EditorUtility.SetDirty(frame);
                return frame;
            }
            catch { Undo.FlushUndoRecordObjects(); Undo.RevertAllDownToGroup(group); throw; }
            finally { Undo.CollapseUndoOperations(group); }
        }
        public static void Remove(ReferenceLayoutFrame frame)
        {
            if (!frame) return;
            var parent = frame.transform.parent as RectTransform;
            if (!parent) return;
            var children = frame.transform.Cast<Transform>().ToList();
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Remove fixed reference UI layout");
            foreach (var child in children)
            {
                if (!(child is RectTransform rect)) { Undo.SetTransformParent(child, parent, "Preserve child object"); continue; }
                var state = RectState.Capture(rect); var rotation = child.localRotation; var scale = child.localScale;
                Undo.SetTransformParent(child, parent, "Restore original UI hierarchy");
                Undo.RecordObject(child, "Restore design transforms"); state.Restore(rect); child.localRotation = rotation; child.localScale = scale;
                RectTransformApplier.Record(rect);
            }
            Undo.DestroyObjectImmediate(frame.gameObject); Undo.CollapseUndoOperations(group);
        }
    }
}
