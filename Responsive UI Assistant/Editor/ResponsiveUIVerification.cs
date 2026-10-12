using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ResponsiveUIAssistant
{
    // Explicit request file lets repository automation run verification in the open Editor.
    [InitializeOnLoad]
    public static class ResponsiveUIVerification
    {
        static readonly string Request = "Library/ResponsiveUIAssistant.verify-request";
        static ResponsiveUIVerification() { EditorApplication.delayCall += RunRequested; }
        static void RunRequested() { if (!File.Exists(Request)) return; File.Delete(Request); Run(); }
        [MenuItem("Tools/Responsive UI Assistant Verification")]
        public static void Run()
        {
            var log = new List<string>();
            var scene = EditorSceneManager.NewPreviewScene();
            string prefabPath = "Assets/ResponsiveUIVerification-" + Guid.NewGuid().ToString("N") + ".prefab";
            try
            {
                var go = new GameObject("Verification Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
                SceneManager.MoveGameObjectToScene(go, scene);
                var canvas = go.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                var scaler = go.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
                var root = Child(go.transform, "Panel", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                // Independent known parent dimensions for geometry assertions.
                root.anchorMin = root.anchorMax = Vector2.one * .5f; root.sizeDelta = new Vector2(1000, 600);
                var button = Child(root, "Settings", Vector2.one * .5f, Vector2.one * .5f, new Vector2(430, 250), new Vector2(100, 60));
                button.pivot = new Vector2(.2f, .8f); button.anchoredPosition3D = new Vector3(400, 240, 7);
                var list = GeometryCapture.Scan(root, true, .25f, .8f);
                var r = list.Single();
                Check(r.candidates.Count == 13, "All 13 candidate types", log);
                Check(r.proposed.name == "Top Right", "Edge-aware Top Right recommendation", log);
                Check(r.parent == root, "Nearest-parent geometry", log);
                var baseline = RectState.Capture(button); var corners = new Vector3[4]; button.GetWorldCorners(corners);
                r.safety = Safety.Review;
                Check(RectTransformApplier.Apply(r, .01f), "Verified anchor application", log);
                var after = new Vector3[4]; button.GetWorldCorners(after);
                Check(corners.Zip(after, (a, b) => Vector3.Distance(a, b)).Max() < .01f, "Zero visual shift", log);
                Check(button.pivot == baseline.pivot && button.anchoredPosition3D.z == 7, "Pivot and depth preserved", log);
                baseline.Restore(button);
                Check(baseline.Matches(button), "Snapshot state round trip", log);
                foreach (var candidate in r.candidates)
                {
                    r.state = RectState.Capture(button); r.proposed = candidate;
                    Check(RectTransformApplier.Apply(r, .01f), "Geometry preserved: " + candidate.name, log);
                    baseline.Restore(button);
                }
                var bg = Child(root, "Background", Vector2.one * .5f, Vector2.one * .5f, Vector2.zero, new Vector2(1000, 600));
                Check(GeometryCapture.Scan(root, true, .25f, .8f).Single(x => x.target == bg).proposed.name == "Full Stretch", "Coverage-aware full stretch", log);
                button.gameObject.AddComponent<ContentSizeFitter>();
                Check(RiskAnalyzer.SkipReason(button) != null, "ContentSizeFitter exclusion", log);
                UnityEngine.Object.DestroyImmediate(button.GetComponent<ContentSizeFitter>());
                root.gameObject.AddComponent<HorizontalLayoutGroup>();
                Check(RiskAnalyzer.SkipReason(button) != null, "Layout Group exclusion", log);
                UnityEngine.Object.DestroyImmediate(root.GetComponent<HorizontalLayoutGroup>());
                root.gameObject.AddComponent<Animator>();
                Check(RiskAnalyzer.SkipReason(button) != null, "Animator exclusion", log);
                UnityEngine.Object.DestroyImmediate(root.GetComponent<Animator>());
                canvas.renderMode = RenderMode.WorldSpace;
                Check(RiskAnalyzer.SkipReason(button) != null, "World Space exclusion", log);
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                root.localScale = new Vector3(2, 1, 1);
                Check(RiskAnalyzer.SkipReason(button) != null, "Scaled hierarchy exclusion", log);
                root.localScale = Vector3.one;
                var reference = new Vector2(1920, 1080);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                Check((ResolutionSimulator.CanvasSize(root, reference, reference) - reference).magnitude < .01f, "Reference Canvas Scaler size", log);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
                Check((ResolutionSimulator.CanvasSize(root, new Vector2(1280, 960), reference) - new Vector2(1920, 1440)).magnitude < .01f, "Expand Canvas Scaler math", log);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Shrink;
                Check((ResolutionSimulator.CanvasSize(root, new Vector2(1280, 960), reference) - new Vector2(1440, 1080)).magnitude < .01f, "Shrink Canvas Scaler math", log);
                list = GeometryCapture.Scan(root, true, .25f, .8f);
                var states = list.Select(x => RectState.Capture(x.target)).ToList();
                var resolutions = new List<Vector2> { reference, new Vector2(1920, 1200), new Vector2(1280, 960), new Vector2(3440, 1440), new Vector2(2560, 1440) };
                var reports = ResolutionSimulator.Evaluate(root, list, resolutions, reference);
                Check(reports.Count == 5 && reports.All(x => x.score >= 0 && x.score <= 100), "Preset and custom resolution reports", log);
                Check(list.Select((x, i) => states[i].Matches(x.target)).All(x => x), "Analysis and simulation do not mutate", log);
                UnityEngine.Object.DestroyImmediate(bg.gameObject);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Shrink;
                root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one;
                root.sizeDelta = new Vector2(1000, 600) - (go.transform as RectTransform).rect.size;
                button.pivot = Vector2.one * .5f; button.sizeDelta = new Vector2(300, 100); button.anchoredPosition = new Vector2(300, 0);
                var left = Child(root, "Left", Vector2.one * .5f, Vector2.one * .5f, new Vector2(-300, 0), new Vector2(300, 100));
                list = GeometryCapture.Scan(root, true, .25f, .8f);
                foreach (var item in list) { item.safety = Safety.Review; var anchor = item.target == left ? new Vector2(0, .5f) : new Vector2(1, .5f); item.proposed = new Candidate("Test edge", anchor, anchor); }
                var small = new Vector2(1280, 960);
                var report = ResolutionSimulator.Run(root, list, small, reference);
                var rightResult = list.Single(x => x.target == button);
                var delta = ResolutionSimulator.CanvasSize(root, small, reference) - ResolutionSimulator.CanvasSize(root, reference, reference);
                Check(Mathf.Abs(report.rects[button].xMin - (rightResult.bounds.xMin + delta.x * .5f)) < .01f, "Nested stretch parent propagates resolution delta", log);
                Check(report.issues.Any(x => x.Contains("increased overlap")), "New sibling overlap detected", log);
                button.anchoredPosition = new Vector2(2000, 0);
                list = GeometryCapture.Scan(root, true, .25f, .8f);
                Check(ResolutionSimulator.Run(root, list, reference, reference).issues.Any(x => x.Contains("outside parent bounds")), "Out-of-bounds detected", log);
                button.anchoredPosition = new Vector2(300, 0);
                list = GeometryCapture.Scan(root, true, .25f, .8f);
                rightResult = list.Single(x => x.target == button); rightResult.safety = Safety.Review;
                var undoState = RectState.Capture(button);
                Undo.IncrementCurrentGroup();
                Check(RectTransformApplier.Apply(rightResult, .01f), "Undo test application (risk: " + RiskAnalyzer.SkipReason(button) + "; matching baseline: " + rightResult.state.Matches(button) + ")", log);
                Undo.FlushUndoRecordObjects();
                var appliedState = RectState.Capture(button);
                Undo.PerformUndo();
                Check(undoState.Matches(button), "Undo restores original anchors and offsets", log);
                Undo.PerformRedo();
                Check(appliedState.Matches(button), "Redo restores applied anchors and offsets", log);
                Undo.ClearUndo(button);
                var isolated = Child(go.transform, "Isolated scoring", Vector2.one * .5f, Vector2.one * .5f, Vector2.zero, new Vector2(1000, 600));
                var good = Child(isolated, "Good corner", Vector2.one * .5f, Vector2.one * .5f, new Vector2(430, 250), new Vector2(100, 60));
                var broken = Child(isolated, "Existing out-of-bounds", Vector2.one * .5f, Vector2.one * .5f, new Vector2(-2000, -2000), new Vector2(100, 60));
                var isolatedResults = GeometryCapture.Scan(isolated, true, .25f, .8f);
                ResolutionSimulator.Evaluate(isolated, isolatedResults, resolutions, reference);
                var goodResult = isolatedResults.Single(x => x.target == good);
                Check(goodResult.confidence >= 80 && goodResult.safety == Safety.Safe, "Unrelated existing issues do not zero confidence or block safe anchors", log);
                Check(ResponsiveUIBatch.NonRegressingSubset(isolated, isolatedResults, new List<Result> { goodResult }, resolutions, reference).Count == 1, "Automatic subset tolerates unchanged existing problems", log);
                var beforeSubset = RectState.Capture(good);
                ResponsiveUIBatch.NonRegressingSubset(isolated, isolatedResults, new List<Result> { goodResult }, resolutions, reference);
                Check(beforeSubset.Matches(good), "Batch preflight is read-only", log);
                Check(ResponsiveUIBatch.Changed(goodResult), "Changed anchor is eligible", log);
                var originalProposal = goodResult.proposed;
                goodResult.proposed = new Candidate("Already correct", goodResult.state.min, goodResult.state.max);
                Check(!ResponsiveUIBatch.Changed(goodResult), "Already-correct anchors do not produce empty mutations", log);
                goodResult.proposed = originalProposal;
                var source = new GameObject("Prefab verification", typeof(RectTransform));
                SceneManager.MoveGameObjectToScene(source, scene);
                var prefab = PrefabUtility.SaveAsPrefabAsset(source, prefabPath);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
                var instanceRect = instance.GetComponent<RectTransform>();
                instanceRect.anchoredPosition = Vector2.zero;
                list = GeometryCapture.Scan(root, true, .25f, .8f);
                ResolutionSimulator.Evaluate(root, list, resolutions, reference);
                var prefabResult = list.Single(x => x.target == instanceRect);
                Check(prefabResult.safety == Safety.Review, "Prefab instance requires explicit review", log);
                var cleanPrefab = (GameObject)PrefabUtility.InstantiatePrefab(prefab, isolated);
                var cleanPrefabRect = cleanPrefab.GetComponent<RectTransform>(); cleanPrefabRect.anchoredPosition = new Vector2(-430, 250);
                var prefabResults = GeometryCapture.Scan(isolated, true, .25f, .8f);
                ResolutionSimulator.Evaluate(isolated, prefabResults, resolutions, reference, true);
                Check(prefabResults.Single(x => x.target == cleanPrefabRect).safety == Safety.Safe, "Opted-in prefab overrides can use verified automatic anchors", log);
                prefabResult.proposed = new Candidate("Top Left", new Vector2(0, 1), new Vector2(0, 1));
                Check(RectTransformApplier.Apply(prefabResult, .01f), "Explicit prefab instance application", log);
                Check(PrefabUtility.GetPropertyModifications(instance).Any(x => x.propertyPath.Contains("m_Anchor")), "Prefab anchor overrides recorded", log);
                Undo.ClearUndo(instanceRect);
                UnityEngine.Object.DestroyImmediate(instance);
                Check(GameViewResolution.Aspect(new Vector2(1920, 1080)) == "16:9", "16:9 preview aspect label", log);
                Check(GameViewResolution.Aspect(new Vector2(1920, 1200)) == "16:10", "16:10 preview aspect label", log);
                Check(!GameViewResolution.Valid(new Vector2(0, 1080)) && !GameViewResolution.Valid(new Vector2(1920, 9000)), "Invalid preview resolutions rejected", log);
                var actualPreview = GameViewResolution.Set(new Vector2(1920, 1200), false);
                Check(actualPreview == new Vector2(1920, 1200), "Game View actually selects 1920 x 1200", log);
                actualPreview = GameViewResolution.Set(new Vector2(1600, 1000), false);
                Check(actualPreview == new Vector2(1600, 1000), "Custom fixed resolution is created and selected", log);
                actualPreview = GameViewResolution.Set(new Vector2(1920, 1080), false);
                Check(actualPreview == new Vector2(1920, 1080), "Game View can switch back to 1920 x 1080", log);
                VerifyAdaptation(log);
                VerifyFixedLayout(log);
                log.Add("SUCCESS: " + log.Count + " checks passed");
            }
            catch (Exception e) { log.Add("FAILED: " + e); Debug.LogException(e); }
            finally { EditorSceneManager.ClosePreviewScene(scene); if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath)) AssetDatabase.DeleteAsset(prefabPath); File.WriteAllLines("Library/ResponsiveUIAssistant-verification.txt", log); }
            Debug.Log(string.Join("\n", log));
        }
        static RectTransform Child(Transform parent, string name, Vector2 min, Vector2 max, Vector2 position, Vector2 size)
        {
            var t = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            t.SetParent(parent, false); t.anchorMin = min; t.anchorMax = max; t.sizeDelta = size; t.anchoredPosition = position; return t;
        }
        static void VerifyAdaptation(List<string> log)
        {
            var priorScene = SceneManager.GetActiveScene();
            string baselinePath = null;
            if (string.IsNullOrEmpty(priorScene.path))
            {
                if (!Application.isBatchMode) { log.Add("SKIP: Save the current scene to run persistent adaptation recovery checks."); return; }
                baselinePath = "Assets/ResponsiveUIBaseline-" + Guid.NewGuid().ToString("N") + ".unity";
                EditorSceneManager.SaveScene(priorScene, baselinePath);
            }
            var testScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            string scenePath = "Assets/ResponsiveUIAdaptVerification-" + Guid.NewGuid().ToString("N") + ".unity";
            byte[] savedSnapshot = File.Exists(LayoutSnapshotManager.Path) ? File.ReadAllBytes(LayoutSnapshotManager.Path) : null;
            Texture2D texture = null; Sprite sprite = null;
            try
            {
                var go = new GameObject("Adaptation test", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
                SceneManager.MoveGameObjectToScene(go, testScene);
                var canvas = go.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.enabled = false;
                var t = go.GetComponent<RectTransform>(); t.localScale = Vector3.one;
                t.sizeDelta = new Vector2(1440, 960); t.pivot = Vector2.one * .5f;
                var scaler = go.GetComponent<CanvasScaler>(); scaler.referenceResolution = new Vector2(1440, 960); scaler.matchWidthOrHeight = .35f;
                var left = Child(t, "Text column", Vector2.one * .5f, Vector2.one * .5f, new Vector2(-510, 0), new Vector2(420, 960));
                var right = Child(t, "Image column", Vector2.one * .5f, Vector2.one * .5f, new Vector2(210, 0), new Vector2(1020, 960));
                var footer = Child(left, "Footer", Vector2.one * .5f, Vector2.one * .5f, new Vector2(0, -430), new Vector2(400, 60));
                var image = right.gameObject.AddComponent<Image>();
                texture = new Texture2D(128, 128); sprite = Sprite.Create(texture, new Rect(0, 0, 128, 128), Vector2.one * .5f); image.sprite = sprite;
                EditorSceneManager.SaveScene(testScene, scenePath);
                var leftState = RectState.Capture(left); var rightState = RectState.Capture(right);
                var plan = LayoutAdapter.Plan(t);
                Check(plan.Single(r => r.target == left).proposed.max.x > .29f && plan.Single(r => r.target == left).proposed.max.x < .3f, "Measured left column boundary retained", log);
                var leftBounds = GeometryCapture.Bounds(left, t);
                Check(LayoutAdapter.Apply(t, plan, true) >= 2, "Layout adaptation applies panel anchors", log);
                Check((GeometryCapture.Bounds(left, t).min - leftBounds.min).sqrMagnitude < .001f, "Source composition preserved during adaptation", log);
                Check(scaler.referenceResolution == new Vector2(1440, 960) && scaler.matchWidthOrHeight == 1, "Adaptation preserves design units and uses uniform height scaling", log);
                Check(!image.preserveAspect, "Large panel images keep Preserve Aspect false", log);
                var panelAnchors = RectState.Capture(right);
                image.preserveAspect = true;
                LayoutAdapter.Apply(t, LayoutAdapter.Plan(t), false);
                Check(image.preserveAspect, "Existing Preserve Aspect true is retained even with legacy toggle false", log);
                image.preserveAspect = false;
                // Save a legacy snapshot to verify targeted recovery of the old forced flag.
                LayoutSnapshotManager.Save(plan, scaler, new[] { image });
                image.preserveAspect = true;
                Check(LayoutSnapshotManager.RestoreImageAspectOnly() == 1 && !image.preserveAspect, "Legacy forced Preserve Aspect can be restored selectively", log);
                Check(panelAnchors.Matches(right) && scaler.matchWidthOrHeight == 1, "Image-only recovery leaves anchors and Canvas Scaler unchanged", log);
                // The geometry snapshot below must represent the original pre-adaptation layout.
                leftState.Restore(left); rightState.Restore(right);
                scaler.matchWidthOrHeight = .35f;
                plan = LayoutAdapter.Plan(t);
                LayoutAdapter.Apply(t, plan, true);
                // 1920 x 1080 at height scale 1080/960 has a logical Canvas width of 1706.6667.
                t.sizeDelta = new Vector2(1920f * 960 / 1080, 960);
                Check(Mathf.Abs(left.rect.width / t.rect.width - 420f / 1440) < .001f, "3:2 to 16:9 preserves column allocation", log);
                Check(Mathf.Abs(GeometryCapture.Bounds(left, t).xMax - GeometryCapture.Bounds(right, t).xMin) < .01f, "Adapted columns meet without gaps or overlaps", log);
                Check(Mathf.Abs(footer.rect.height - 60) < .01f, "Footer height stays stable in design units", log);
                t.sizeDelta = new Vector2(1440, 960);
                image.color = Color.red;
                Check(LayoutSnapshotManager.Restore() == 3, "Adaptation snapshot resolves saved scene objects", log);
                Check(leftState.Matches(left) && rightState.Matches(right), "Adaptation snapshot restores original geometry", log);
                Check(Mathf.Abs(scaler.matchWidthOrHeight - .35f) < .001f && !image.preserveAspect, "Snapshot restores Canvas Scaler and image aspect settings", log);
                Check(image.color == Color.red, "Snapshot leaves unrelated image styling unchanged", log);
                Undo.ClearUndo(left); Undo.ClearUndo(right); Undo.ClearUndo(footer); Undo.ClearUndo(scaler); Undo.ClearUndo(image);
            }
            finally
            {
                if (priorScene.IsValid()) SceneManager.SetActiveScene(priorScene);
                EditorSceneManager.CloseScene(testScene, true); AssetDatabase.DeleteAsset(scenePath);
                if (baselinePath != null) AssetDatabase.DeleteAsset(baselinePath);
                if (sprite) UnityEngine.Object.DestroyImmediate(sprite); if (texture) UnityEngine.Object.DestroyImmediate(texture);
                if (savedSnapshot != null) File.WriteAllBytes(LayoutSnapshotManager.Path, savedSnapshot);
                else if (File.Exists(LayoutSnapshotManager.Path)) File.Delete(LayoutSnapshotManager.Path);
            }
        }
        static void Check(bool condition, string name, List<string> log) { if (!condition) throw new InvalidOperationException(name); log.Add("PASS: " + name); }
        static void VerifyFixedLayout(List<string> log)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var go = new GameObject("Fixed layout test", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
                SceneManager.MoveGameObjectToScene(go, scene);
                var canvas = go.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.enabled = false;
                var root = go.GetComponent<RectTransform>(); root.localScale = Vector3.one; root.pivot = Vector2.zero; root.sizeDelta = new Vector2(1440, 960);
                var scaler = go.GetComponent<CanvasScaler>(); scaler.matchWidthOrHeight = .37f;
                var item = Child(root, "Panel", Vector2.zero, Vector2.zero, new Vector2(300, 500), new Vector2(400, 200));
                item.anchoredPosition3D = new Vector3(300, 500, 7);
                var inactive = Child(root, "Inactive screen", Vector2.one * .5f, Vector2.one * .5f, Vector2.zero, new Vector2(200, 100)); inactive.gameObject.SetActive(false);
                var image = item.gameObject.AddComponent<Image>(); image.preserveAspect = false;
                var original = RectState.Capture(item); var corners = new Vector3[4]; item.GetWorldCorners(corners);
                var frame = ReferenceLayoutInstaller.Install(root, new Vector2(1440, 960));
                var fitted = new Vector3[4]; item.GetWorldCorners(fitted);
                Check(corners.Zip(fitted, (a, b) => Vector3.Distance(a, b)).Max() < .01f, "Fixed frame preserves exact source appearance including zero pivot", log);
                Check(inactive.parent == frame.transform && !inactive.gameObject.activeSelf, "Inactive screens retained without activation", log);
                Check(ReferenceLayoutInstaller.Install(root, new Vector2(1920, 1080)) == frame, "Repeated installation reuses the original design frame", log);
                root.sizeDelta = new Vector2(1920, 1080); frame.Fit();
                Check(Mathf.Abs(frame.transform.localScale.x - 1.125f) < .001f && frame.transform.localScale.x == frame.transform.localScale.y, "3:2 to 16:9 uses uniform fit scale", log);
                var frameBounds = GeometryCapture.Bounds(frame.transform as RectTransform, root);
                Check(Mathf.Abs(frameBounds.xMin - 150) < .01f && Mathf.Abs(frameBounds.xMax - 1770) < .01f, "Extra 300 pixels become symmetric margins", log);
                item.GetWorldCorners(fitted);
                Check(Mathf.Abs(Vector3.Distance(fitted[0], fitted[1]) / Vector3.Distance(corners[0], corners[1]) - 1.125f) < .001f, "Element sizes scale together without individual anchor changes", log);
                Check(original.Matches(item) && Mathf.Abs(scaler.matchWidthOrHeight - .37f) < .001f && !image.preserveAspect, "Original anchors, Match and Preserve Aspect unchanged", log);
                Check(Mathf.Abs(fitted[0].z - corners[0].z) < .001f, "Fixed layout retains UI depth", log);
                root.sizeDelta = new Vector2(1920, 1200); frame.Fit();
                Check(Mathf.Abs(frame.transform.localScale.x - 1.25f) < .001f, "Frame responds to subsequent 16:10 resolution changes", log);
                root.sizeDelta = new Vector2(960, 1440); frame.Fit();
                Check(Mathf.Abs(frame.transform.localScale.x - 2f / 3) < .001f, "Portrait resolution fits by width without clipping", log);
                var extra = new GameObject("Non-UI child"); extra.transform.SetParent(frame.transform, false);
                ReferenceLayoutInstaller.Remove(frame);
                Check(item.parent == root && original.Matches(item), "Removing frame restores original hierarchy and RectTransform settings", log);
                Check(extra && extra.transform.parent == root, "Removing frame preserves added non-UI children", log);
                Undo.ClearUndo(item); Undo.ClearUndo(inactive);
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
    }
}
