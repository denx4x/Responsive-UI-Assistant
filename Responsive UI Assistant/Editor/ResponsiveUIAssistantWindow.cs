using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace ResponsiveUIAssistant
{
    public sealed class ResponsiveUIAssistantWindow : EditorWindow
    {
        [SerializeField] RectTransform root;
        [SerializeField] Vector2 reference = new Vector2(1920, 1080), custom = new Vector2(2560, 1440);
        [SerializeField] float edge = .25f, stretch = .8f;
        [SerializeField] bool children = true, useCustom;
        [SerializeField] Vector2 targetResolution = new Vector2(1920, 1080);
        [SerializeField] Vector2 sourceResolution = new Vector2(1440, 960);
        [SerializeField] bool adaptLayout = true, preserveImages = true;
        [SerializeField] bool fixedLayout = true;
        bool busy;
        List<Result> results;
        List<ResolutionReport> reports;
        string fingerprint, message;
        Vector2 scroll;
        int filter;
        bool preview;
        RectTransform previewParent;
        int previewResolution;
        [MenuItem("Tools/Responsive UI Assistant")]
        public static void Open() { GetWindow<ResponsiveUIAssistantWindow>("Responsive UI Assistant").minSize = new Vector2(480, 340); }
        void OnEnable() { if (!root) root = Selection.activeTransform as RectTransform; SceneView.duringSceneGui += DrawPreview; Undo.undoRedoPerformed += Repaint; }
        void OnDisable() { SceneView.duringSceneGui -= DrawPreview; Undo.undoRedoPerformed -= Repaint; }
        List<Vector2> Resolutions()
        {
            var list = new List<Vector2> { reference, targetResolution, new Vector2(1920, 1080), new Vector2(1920, 1200), new Vector2(1280, 960), new Vector2(3440, 1440) };
            if (useCustom) list.Add(custom);
            return list.Distinct().ToList();
        }
        string Fingerprint()
        {
            if (!root) return "";
            var s = new StringBuilder();
            s.Append(root.GetInstanceID()).Append(reference).Append(targetResolution).Append(custom).Append(useCustom).Append(edge).Append(stretch).Append(children).Append(allowPrefabOverrides);
            foreach (var t in root.GetComponentsInChildren<RectTransform>(true)) Append(s, t);
            for (var t = root.parent; t; t = t.parent) Append(s, t);
            return s.ToString();
        }
        static void Append(StringBuilder s, Transform t)
        {
            s.Append(t.GetInstanceID()).Append(t.parent ? t.parent.GetInstanceID() : 0).Append(t.gameObject.activeInHierarchy);
            foreach (var c in t.GetComponents<Component>()) if (c) s.Append(EditorJsonUtility.ToJson(c));
            if (t is RectTransform r) s.Append(r.rect).Append(r.drivenByObject ? r.drivenByObject.GetInstanceID() : 0);
            if (t is RectTransform rt) { var corners = new Vector3[4]; rt.GetWorldCorners(corners); foreach (var corner in corners) s.Append(corner.ToString("R")); }
            if (t.TryGetComponent<Canvas>(out var canvas)) s.Append(canvas.pixelRect).Append(canvas.scaleFactor);
        }
        bool Stale => results != null && fingerprint != Fingerprint();
        void OnSelectionChange()
        {
            if (!root && Selection.activeTransform is RectTransform selected) root = selected;
            Repaint();
        }
        bool advanced, showResults, showTests;
        [SerializeField] bool allowPrefabOverrides = true;
        readonly HashSet<int> expanded = new HashSet<int>();
        void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            GUILayout.BeginHorizontal(); GUILayout.FlexibleSpace();
            GUILayout.BeginVertical(GUILayout.MaxWidth(760));
            GUILayout.Space(12);
            EditorGUILayout.LabelField("Responsive UI Assistant", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Masukkan resolusi tujuan. Perbaiki anchor, lalu lihat hasilnya langsung di Game View.", EditorStyles.wordWrappedLabel);
            GUILayout.Space(10);
            EditorGUILayout.BeginHorizontal();
            var nextRoot = (RectTransform)EditorGUILayout.ObjectField("Canvas / Panel", root, typeof(RectTransform), true);
            if (nextRoot != root) { root = nextRoot; results = null; reports = null; message = null; }
            if (GUILayout.Button("Dari Selection", GUILayout.Width(110)))
            {
                root = Selection.activeTransform as RectTransform; results = null; reports = null; message = null;
            }
            EditorGUILayout.EndHorizontal();
            int mode = GUILayout.Toolbar(fixedLayout ? 0 : adaptLayout ? 1 : 2, new[] { "Layout Tetap", "Adaptasi Panel", "Anchor" });
            fixedLayout = mode == 0; adaptLayout = mode == 1;
            if (fixedLayout || adaptLayout)
            {
                EditorGUILayout.BeginHorizontal(); EditorGUILayout.PrefixLabel("Resolusi Desain Asal");
                sourceResolution.x = EditorGUILayout.IntField((int)sourceResolution.x);
                GUILayout.Label("×", GUILayout.Width(15)); sourceResolution.y = EditorGUILayout.IntField((int)sourceResolution.y);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.LabelField(fixedLayout ? "Komposisi sama, skala proporsional. Rasio berbeda menyisakan ruang kosong. Match dan Preserve Aspect tetap." : "Panel mengikuti pembagian lebar. Mode ini mengubah Canvas Scaler menjadi Match Height.", EditorStyles.wordWrappedLabel);
            }
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Resolusi Preview");
            targetResolution.x = EditorGUILayout.IntField((int)targetResolution.x);
            GUILayout.Label("×", GUILayout.Width(15));
            targetResolution.y = EditorGUILayout.IntField((int)targetResolution.y);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("16:9")) targetResolution = new Vector2(1920, 1080);
            if (GUILayout.Button("16:10")) targetResolution = new Vector2(1920, 1200);
            if (GUILayout.Button("4:3")) targetResolution = new Vector2(1280, 960);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField($"Tujuan: {targetResolution.x:F0} × {targetResolution.y:F0} ({GameViewResolution.Aspect(targetResolution)})", EditorStyles.miniLabel);
            if (!GameViewResolution.Valid(targetResolution)) EditorGUILayout.HelpBox("Isi lebar dan tinggi antara 16 dan 8192 piksel.", MessageType.Warning);
            var canvasRoot = root ? root.GetComponentInParent<Canvas>() : null;
            bool valid = root && canvasRoot && !EditorUtility.IsPersistent(root) && canvasRoot.rootCanvas.renderMode != RenderMode.WorldSpace && canvasRoot.rootCanvas.pixelRect.width > 0 && canvasRoot.rootCanvas.pixelRect.height > 0 && !EditorApplication.isPlayingOrWillChangePlaymode;
            bool saved = root && !string.IsNullOrEmpty(root.gameObject.scene.path);
            if (!root) EditorGUILayout.LabelField("Pilih Canvas di Hierarchy atau masukkan ke kolom di atas.", EditorStyles.wordWrappedLabel);
            else if (!valid) EditorGUILayout.HelpBox("Gunakan Canvas screen-space di scene, di luar Play Mode. Buka Game View agar ukurannya tersedia.", MessageType.Warning);
            else
            {
                Vector2 current = canvasRoot.rootCanvas.pixelRect.size;
                EditorGUILayout.LabelField($"Ukuran saat ini: {current.x:F0} × {current.y:F0} · dites juga pada 16:9, 16:10, 4:3 dan ultrawide.", EditorStyles.wordWrappedLabel);
                if (!saved) EditorGUILayout.HelpBox("Simpan scene terlebih dahulu agar hasil bisa dipulihkan lewat snapshot.", MessageType.Warning);
            }
            GUILayout.Space(8);
            bool frameActive = ReferenceLayoutInstaller.Find(root);
            bool canAdapt = ((!adaptLayout && !fixedLayout) || (GameViewResolution.Valid(sourceResolution) && root && root.GetComponent<Canvas>() && root.GetComponent<Canvas>() == canvasRoot.rootCanvas)) && (fixedLayout || !frameActive);
            if (frameActive && !fixedLayout) EditorGUILayout.HelpBox("Lepas Layout Tetap sebelum menjalankan mode yang mengubah anchor atau Canvas Scaler.", MessageType.Warning);
            if ((adaptLayout || fixedLayout) && root && !canAdapt) EditorGUILayout.HelpBox("Pilih root Canvas dan isi resolusi asal yang valid.", MessageType.Warning);
            using (new EditorGUI.DisabledScope(busy || !valid || !saved || !canAdapt || !GameViewResolution.Valid(targetResolution) || (useCustom && (custom.x <= 0 || custom.y <= 0))))
                if (GUILayout.Button(busy ? "Memproses…" : fixedLayout ? "Pertahankan Layout & Buka Hasil" : adaptLayout ? "Adaptasi Layout & Buka Hasil" : "Perbaiki Anchor & Buka Preview", GUILayout.Height(42)))
                {
                    if (fixedLayout) StartFixedLayout(); else if (adaptLayout) StartAdaptation(); else StartAutomatic();
                }
            var installedFrame = ReferenceLayoutInstaller.Find(root);
            if (installedFrame)
            {
                EditorGUILayout.LabelField("Layout Tetap aktif: perubahan resolusi berikutnya diikuti otomatis.", EditorStyles.wordWrappedLabel);
                using (new EditorGUI.DisabledScope(busy || EditorApplication.isPlayingOrWillChangePlaymode))
                    if (GUILayout.Button("Lepas Layout Tetap")) { ReferenceLayoutInstaller.Remove(installedFrame); results = null; reports = null; message = "Frame dilepas. Hierarki kembali langsung di bawah Canvas. Ctrl+Z tersedia."; }
            }
            using (new EditorGUI.DisabledScope(busy || !GameViewResolution.Valid(targetResolution) || EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button("Preview Resolusi Saja"))
                {
                    try { var actual = GameViewResolution.Set(targetResolution); message = $"Game View: {actual.x:F0} × {actual.y:F0} ({GameViewResolution.Aspect(actual)}). Anchor tidak diubah."; results = null; reports = null; }
                    catch (System.Exception e) { message = "Preview gagal: " + e.GetBaseException().Message; Debug.LogException(e); }
                }
            EditorGUILayout.LabelField(allowPrefabOverrides ? "Ctrl+Z untuk Undo. Prefab instance diubah sebagai override di scene." : "Ctrl+Z untuk Undo. Prefab instance hanya dianalisis.", EditorStyles.miniLabel);
            if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, MessageType.Info);
            bool stale = Stale;
            if (results != null)
            {
                int ready = results.Count(r => r.safety == Safety.Safe && ResponsiveUIBatch.Changed(r));
                int correct = results.Count(r => r.safety == Safety.Safe && !ResponsiveUIBatch.Changed(r));
                int review = results.Count(r => r.safety == Safety.Review), skip = results.Count(r => r.safety == Safety.Skip);
                EditorGUILayout.LabelField($"{ready} siap diperbaiki · {correct} sudah sesuai · {review} perlu diperiksa · {skip} dilewati", EditorStyles.wordWrappedLabel);
                if (stale) EditorGUILayout.HelpBox("Layout berubah. Jalankan lagi untuk menghitung ulang.", MessageType.Warning);
            }
            using (new EditorGUI.DisabledScope(!File.Exists(LayoutSnapshotManager.Path) || EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button("Pulihkan Perubahan Terakhir"))
                {
                    message = $"{LayoutSnapshotManager.Restore()} objek dipulihkan."; results = null; reports = null;
                }
            GUILayout.Space(12);
            advanced = EditorGUILayout.Foldout(advanced, "Advanced — pengaturan dan pemeriksaan manual", true);
            if (advanced) DrawAdvanced(valid, stale);
            GUILayout.EndVertical(); GUILayout.FlexibleSpace(); GUILayout.EndHorizontal();
            EditorGUILayout.EndScrollView();
        }
        void DrawAdvanced(bool valid, bool stale)
        {
            reference = EditorGUILayout.Vector2Field("Reference resolution", reference);
            edge = EditorGUILayout.Slider("Edge threshold", edge, .05f, .45f);
            stretch = EditorGUILayout.Slider("Stretch coverage", stretch, .5f, 1f);
            children = EditorGUILayout.Toggle("Include descendants", children);
            allowPrefabOverrides = EditorGUILayout.Toggle("Override prefab instance", allowPrefabOverrides);
            EditorGUILayout.LabelField("Preserve Aspect gambar mengikuti Inspector; tidak diubah oleh adaptasi.", EditorStyles.wordWrappedLabel);
            using (new EditorGUI.DisabledScope(!File.Exists(LayoutSnapshotManager.Path) || busy || EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button("Pulihkan Preserve Aspect dari snapshot lama"))
                    message = $"{LayoutSnapshotManager.RestoreImageAspectOnly()} pengaturan gambar dipulihkan. Layout dan Canvas Scaler tetap.";
            useCustom = EditorGUILayout.Toggle("Custom test resolution", useCustom);
            if (useCustom) custom = EditorGUILayout.Vector2Field("Custom resolution", custom);
            EditorGUILayout.LabelField("Simulasi berdasarkan geometri; teks, masking, dan layout dinamis tetap perlu diperiksa di Game View.", EditorStyles.wordWrappedLabel);
            using (new EditorGUI.DisabledScope(!valid || (useCustom && (custom.x <= 0 || custom.y <= 0))))
                if (GUILayout.Button("Analisis saja (tanpa mengubah UI)"))
                {
                    reference = root.GetComponentInParent<Canvas>().rootCanvas.pixelRect.size; Analyze();
                }
            if (results == null) return;
            stale = Stale;
            showResults = EditorGUILayout.Foldout(showResults, $"Detail objek ({results.Count})", true);
            if (showResults)
            {
                filter = EditorGUILayout.Popup("Filter", filter, new[] { "All", "SAFE", "REVIEW", "SKIP" });
                foreach (var r in results)
                {
                    if (filter != 0 && (int)r.safety != filter - 1) continue;
                    if (!r.target) continue;
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    EditorGUILayout.BeginHorizontal();
                    using (new EditorGUI.DisabledScope(r.safety == Safety.Skip)) r.selected = EditorGUILayout.Toggle(r.selected, GUILayout.Width(18));
                    int id = r.target.GetInstanceID();
                    bool open = EditorGUILayout.Foldout(expanded.Contains(id), $"{r.target.name} · {r.safety} · {r.proposed.name}", true);
                    if (open) expanded.Add(id); else expanded.Remove(id);
                    EditorGUILayout.EndHorizontal();
                    if (open)
                    {
                        EditorGUILayout.ObjectField(r.target, typeof(RectTransform), true);
                        if (r.safety != Safety.Skip) EditorGUILayout.LabelField($"Confidence heuristik: {r.confidence:F0}%");
                        EditorGUILayout.LabelField(r.risk ?? r.reason, EditorStyles.wordWrappedLabel);
                        if (GUILayout.Button("Preview parent pada resolusi lain")) previewParent = r.parent;
                    }
                    EditorGUILayout.EndVertical();
                }
                using (new EditorGUI.DisabledScope(stale || !valid || !results.Any(r => r.selected && r.safety != Safety.Skip)))
                    if (GUILayout.Button("Terapkan pilihan manual (termasuk REVIEW)")) Apply(true);
            }
            showTests = EditorGUILayout.Foldout(showTests, "Detail tes resolusi", true);
            if (showTests && reports != null)
                foreach (var report in reports)
                {
                    EditorGUILayout.LabelField($"{report.resolution.x:F0} × {report.resolution.y:F0} · {report.score}/100 · {report.issues.Count} masalah");
                    foreach (var issue in report.issues.Take(20)) EditorGUILayout.LabelField("• " + issue, EditorStyles.wordWrappedLabel);
                    if (report.issues.Count > 20) EditorGUILayout.LabelField($"… {report.issues.Count - 20} masalah lainnya");
                }
            preview = EditorGUILayout.Toggle("Outline di Scene View", preview);
            DrawSimulationPreview();
            if (preview) SceneView.RepaintAll();
        }
        void Analyze(bool showProgress = false)
        {
            Canvas.ForceUpdateCanvases();
            results = GeometryCapture.Scan(root, children, edge, stretch);
            System.Action<int, int> progress = showProgress ? new System.Action<int, int>((current, total) => EditorUtility.DisplayProgressBar("Responsive UI Assistant", $"Menguji anchor {current}/{total}…", total > 0 ? .1f + .6f * current / total : .7f)) : null;
            reports = ResolutionSimulator.Evaluate(root, results, Resolutions(), reference, allowPrefabOverrides, progress);
            fingerprint = Fingerprint(); expanded.Clear();
            message = "Analisis selesai. Belum ada perubahan pada UI.";
        }
        int Apply(bool selected, bool refresh = true)
        {
            if (Stale) { message = "Layout berubah sebelum penerapan. Silakan jalankan lagi."; return 0; }
            var chosen = results.Where(r => r.safety != Safety.Skip && ResponsiveUIBatch.Changed(r) && (selected ? r.selected : r.safety == Safety.Safe)).ToList();
            if (!selected) chosen = ResponsiveUIBatch.NonRegressingSubset(root, results, chosen, Resolutions(), reference);
            if (chosen.Count == 0)
            {
                int correct = results.Count(r => r.safety == Safety.Safe && !ResponsiveUIBatch.Changed(r));
                message = $"0 anchor berubah: {correct} sudah sesuai, {results.Count(r => r.safety == Safety.Review)} belum lolos tes, {results.Count(r => r.safety == Safety.Skip)} dikontrol layout/animasi atau tidak didukung.";
                return 0;
            }
            if (chosen.Any(r => string.IsNullOrEmpty(r.target.gameObject.scene.path))) { message = "Simpan scene sebelum menerapkan perubahan."; return 0; }
            LayoutSnapshotManager.Save(chosen);
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Responsive UI anchors");
            int applied = 0;
            try
            {
                foreach (var r in chosen) if (RectTransformApplier.Apply(r, .01f)) applied++;
            }
            catch (System.Exception e)
            {
                Undo.RevertAllDownToGroup(group); message = "Perubahan dibatalkan: " + e.Message; results = null; reports = null; return -1;
            }
            finally { Undo.CollapseUndoOperations(group); }
            int review = results.Count(r => r.safety == Safety.Review), skip = results.Count(r => r.safety == Safety.Skip);
            message = $"{applied} objek diperbaiki · {review} perlu diperiksa · {skip} dilewati. Gunakan Ctrl+Z atau Pulihkan untuk membatalkan.";
            // Capture the post-apply scene; old analysis must never be re-used.
            if (refresh) Analyze();
            message = $"{applied} anchor berubah · {results.Count(r => r.safety == Safety.Review)} perlu diperiksa · {results.Count(r => r.safety == Safety.Skip)} dilewati. Ctrl+Z atau Pulihkan untuk membatalkan.";
            return applied;
        }
        void StartAutomatic()
        {
            busy = true; message = "Menganalisis anchor dan menguji resolusi…";
            var requestedRoot = root; var destination = targetResolution;
            EditorApplication.delayCall += () =>
            {
                if (!this) return;
                try
                {
                    if (!requestedRoot || root != requestedRoot || EditorApplication.isPlayingOrWillChangePlaymode) throw new System.InvalidOperationException("Target berubah atau Editor masuk Play Mode.");
                    reference = root.GetComponentInParent<Canvas>().rootCanvas.pixelRect.size;
                    EditorUtility.DisplayProgressBar("Responsive UI Assistant", "Menganalisis dan menguji anchor…", .25f);
                    Analyze(true);
                    EditorUtility.DisplayProgressBar("Responsive UI Assistant", "Menerapkan perubahan yang lolos pemeriksaan…", .75f);
                    int changed = Apply(false, false);
                    if (changed < 0) throw new System.InvalidOperationException(message);
                    string outcome = message;
                    EditorUtility.ClearProgressBar();
                    var actual = GameViewResolution.Set(destination);
                    message = $"Game View: {actual.x:F0} × {actual.y:F0} ({GameViewResolution.Aspect(actual)}).\n{outcome}\nPeriksa layout di Game View pada resolusi ini.";
                    // Intentional viewport change invalidates the geometry captured before preview.
                    results = null; reports = null;
                    System.IO.File.WriteAllText("Library/ResponsiveUIAssistant-last-action.txt", $"{System.DateTime.Now:O}\n{message}\nAnchor changes: {changed}");
                }
                catch (System.Exception e) { message = "Proses gagal: " + e.GetBaseException().Message; Debug.LogException(e); }
                finally { EditorUtility.ClearProgressBar(); busy = false; Repaint(); }
            };
        }
        void StartAdaptation()
        {
            busy = true; var requestedRoot = root; var source = sourceResolution; var target = targetResolution;
            bool applied = false;
            message = "Membuka desain pada resolusi asal…";
            try { GameViewResolution.Set(source); }
            catch (System.Exception e) { busy = false; message = "Resolusi asal gagal: " + e.GetBaseException().Message; return; }
            // Canvas pixel rect updates after Game View has processed the new render size.
            EditorApplication.delayCall += () => EditorApplication.delayCall += () =>
            {
                if (!this) return;
                try
                {
                    if (!requestedRoot || requestedRoot != root || EditorApplication.isPlayingOrWillChangePlaymode) throw new System.InvalidOperationException("Target berubah sebelum adaptasi.");
                    Canvas.ForceUpdateCanvases();
                    if ((root.GetComponent<Canvas>().pixelRect.size - source).sqrMagnitude > 4) throw new System.InvalidOperationException("Canvas belum berada pada resolusi asal. Buka Game View dan jalankan lagi.");
                    EditorUtility.DisplayProgressBar("Adaptasi Layout", "Membaca komposisi desain asal…", .3f);
                    var plan = LayoutAdapter.Plan(root);
                    if (!allowPrefabOverrides) foreach (var r in plan) if (PrefabUtility.IsPartOfPrefabInstance(r.target)) r.safety = Safety.Skip;
                    EditorUtility.DisplayProgressBar("Adaptasi Layout", "Mengatur panel dan skala Canvas…", .7f);
                    int count = LayoutAdapter.Apply(root, plan, preserveImages);
                    applied = true;
                    EditorUtility.ClearProgressBar();
                    var actual = GameViewResolution.Set(target);
                    results = null; reports = null;
                    message = $"Layout {source.x:F0} × {source.y:F0} → {actual.x:F0} × {actual.y:F0}.\n{count} anchor/panel disesuaikan; {plan.Count(r => r.safety == Safety.Skip)} elemen dilewati. Canvas memakai skala berdasarkan tinggi.\nPeriksa hasil di Game View. Ctrl+Z atau Pulihkan untuk mengembalikan layout dan Canvas Scaler.";
                }
                catch (System.Exception e)
                {
                    if (applied) LayoutSnapshotManager.Restore();
                    message = "Adaptasi gagal; perubahan dibatalkan: " + e.GetBaseException().Message; Debug.LogException(e);
                }
                finally { EditorUtility.ClearProgressBar(); busy = false; Repaint(); }
            };
        }
        void StartFixedLayout()
        {
            var existing = ReferenceLayoutInstaller.Find(root);
            if (existing)
            {
                try { GameViewResolution.Set(targetResolution); existing.Fit(); message = "Preview diperbarui; komposisi desain tetap mengikuti frame asal."; results = null; reports = null; }
                catch (System.Exception e) { message = "Preview gagal: " + e.GetBaseException().Message; }
                return;
            }
            busy = true; var requestedRoot = root; var source = sourceResolution; var target = targetResolution;
            try { GameViewResolution.Set(source); }
            catch (System.Exception e) { busy = false; message = "Resolusi asal gagal: " + e.GetBaseException().Message; return; }
            EditorApplication.delayCall += () => EditorApplication.delayCall += () =>
            {
                if (!this) return;
                ReferenceLayoutFrame created = null;
                try
                {
                    if (!root || root != requestedRoot || EditorApplication.isPlayingOrWillChangePlaymode) throw new System.InvalidOperationException("Target berubah.");
                    Canvas.ForceUpdateCanvases();
                    if ((root.GetComponent<Canvas>().pixelRect.size - source).sqrMagnitude > 4) throw new System.InvalidOperationException("Canvas belum berada pada resolusi asal. Buka Game View dan coba lagi.");
                    created = ReferenceLayoutInstaller.Install(root, source);
                    var actual = GameViewResolution.Set(target); created.Fit();
                    results = null; reports = null;
                    message = $"Layout Tetap aktif: {source.x:F0} × {source.y:F0} → {actual.x:F0} × {actual.y:F0}.\nSeluruh komposisi diskalakan proporsional. Selanjutnya perubahan resolusi diikuti otomatis, termasuk saat game berjalan.\nMatch dan Preserve Aspect tidak diubah. Simpan scene untuk menyimpan frame; Ctrl+Z atau Lepas Layout Tetap untuk membatalkan.";
                }
                catch (System.Exception e)
                {
                    if (created) ReferenceLayoutInstaller.Remove(created);
                    message = "Layout Tetap gagal: " + e.GetBaseException().Message; Debug.LogException(e);
                }
                finally { busy = false; Repaint(); }
            };
        }
        void DrawPreview(SceneView view)
        {
            if (!preview || results == null || Stale) return;
            foreach (var r in results)
            {
                if (!r.target) continue;
                var corners = new Vector3[4]; r.target.GetWorldCorners(corners);
                Handles.color = r.safety == Safety.Safe ? Color.green : r.safety == Safety.Review ? Color.yellow : Color.gray;
                Handles.DrawAAPolyLine(2, corners[0], corners[1], corners[2], corners[3], corners[0]);
                Handles.Label(corners[1], r.proposed.name + " · " + r.safety);
            }
        }
        void DrawSimulationPreview()
        {
            if (!previewParent || reports == null || reports.Count == 0 || Stale) return;
            EditorGUILayout.LabelField("Simulated parent: " + previewParent.name, EditorStyles.boldLabel);
            previewResolution = EditorGUILayout.Popup("Resolution", Mathf.Clamp(previewResolution, 0, reports.Count - 1), reports.Select(r => $"{r.resolution.x:F0} × {r.resolution.y:F0}").ToArray());
            var report = reports[previewResolution];
            if (!report.parentSizes.TryGetValue(previewParent, out var size) || size.x <= 0 || size.y <= 0) return;
            var area = GUILayoutUtility.GetRect(100, 240, GUILayout.ExpandWidth(true));
            float scale = Mathf.Min((area.width - 40) / size.x, (area.height - 40) / size.y);
            var frame = new Rect(area.center.x - size.x * scale / 2, area.center.y - size.y * scale / 2, size.x * scale, size.y * scale);
            GUI.Box(frame, "");
            Vector2 parentMin = -Vector2.Scale(size, previewParent.pivot);
            foreach (var r in results.Where(r => r.parent == previewParent))
            {
                if (!report.rects.TryGetValue(r.target, out var rect)) continue;
                var draw = new Rect(frame.x + (rect.xMin - parentMin.x) * scale, frame.yMax - (rect.yMax - parentMin.y) * scale, rect.width * scale, rect.height * scale);
                var color = r.safety == Safety.Safe ? Color.green : r.safety == Safety.Review ? Color.yellow : Color.gray;
                EditorGUI.DrawRect(new Rect(draw.x, draw.y, draw.width, 1), color);
                EditorGUI.DrawRect(new Rect(draw.x, draw.yMax, draw.width, 1), color);
                EditorGUI.DrawRect(new Rect(draw.x, draw.y, 1, draw.height), color);
                EditorGUI.DrawRect(new Rect(draw.xMax, draw.y, 1, draw.height), color);
                GUI.Label(draw, r.target.name, EditorStyles.miniLabel);
            }
        }
    }
}



