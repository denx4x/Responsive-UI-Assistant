# Responsive UI Assistant v0.1

Editor tooling for existing uGUI Canvas hierarchies, with an optional lightweight runtime frame for fixed composition. Open **Tools → Responsive UI Assistant**. No additional package is required; Layout Tetap installs the runtime ReferenceLayoutFrame component.

## Layout Tetap (optional)

For an identical composition across aspect ratios, select **Layout Tetap**, enter the source resolution where the design is already correct, enter the destination, and click **Pertahankan Layout & Buka Hasil**. The tool creates a **Responsive Layout Frame** beneath the root Canvas and reparents its existing top-level RectTransforms while verifying zero source visual shift. It does not change individual anchors, Canvas Scaler Match, image Preserve Aspect, or active states.

The frame retains the original logical design size and uniformly fits it inside the actual Canvas dimensions. A 1440×960 composition in a 1920×1080 Canvas becomes 1620×1080, centered with 150 pixels on each side. Different aspect ratios can leave horizontal or vertical space. Full-bleed recomposition belongs to **Adaptasi Panel**, which explicitly changes Match to Height and does not promise an identical composition.

After installation, the lightweight `ReferenceLayoutFrame` runtime component responds to Canvas dimension changes in edit and play mode. Save the scene to persist it. The runtime component has no UnityEditor dependency. New elements should be added inside the frame to participate. Re-running preview reuses the original frame rather than capturing an already scaled composition. **Lepas Layout Tetap** restores the direct Canvas hierarchy and original per-element transform settings, with Undo; it also preserves any non-UI children added to the frame. Canvas-root layout drivers or Animator are rejected.

This mode preserves the layout it captures, including existing errors. Restore or correct the source layout before installing it. It adds one hierarchy level; code that relies on direct Canvas children or hard-coded hierarchy paths must account for the frame. Image-reference recognition is not included. Snapshot restore is for anchor/adaptation changes; use Ctrl+Z or **Lepas Layout Tetap** to remove this frame.

## Workflow

### Adaptasi Panel (optional)

1. Save the scene and select its root screen-space Canvas.
2. Set **Resolusi Desain Asal** to the resolution at which the original composition is correct, e.g. **1440 × 960**.
3. Set **Resolusi Preview** to **1920 × 1080**.
4. Click **Adaptasi Layout & Buka Hasil**. The tool opens the original resolution, captures its geometry, adapts panel widths and edge relationships, configures uniform height-based Canvas scaling, and opens Game View at the target resolution.
5. Inspect the result. **Pulihkan Perubahan Terakhir** restores RectTransforms, the changed Canvas Scaler fields, and changed image aspect flags. Unity Undo is available. The scene is left editable; it is not auto-saved.

The original logical design height and font sizes are retained. For a 1440×960 logical design, a 1920×1080 preview uses a 1.125 display scale and a logical Canvas width of 1706.67. Large panels with children or substantial height use their measured normalized horizontal boundaries, so adjacent 29%/71% columns continue to meet while filling the wider composition. Small elements follow their inferred edge relationships. Image Preserve Aspect is always retained from the Inspector. Adaptation does not change this flag for photos, backgrounds, tabs, sliced images, or any other Image; sprite dimensions do not identify the intended visual role.

This is geometric adaptation of the existing Canvas, not image-reference recognition. There is no automatic semantic footer creation, text-wrap optimization, guaranteed collision-free layout, or RawImage aspect handling. Layout/animation-driven subtrees, invalid geometry and intentional baseline overflow are excluded. Inspect the destination in Game View, especially skipped regions. A failed application is reverted; if opening the destination fails after adaptation, the new snapshot is restored.

### Anchor & Preview

Switch to **Anchor & Preview** for the previous non-invasive anchor workflow. Enter a destination and click **Perbaiki Anchor & Buka Preview**. **Preview Resolusi Saja** changes only Game View without editing layout or Canvas Scaler.

**Advanced** is closed by default. It contains thresholds, custom resolution, prefab override opt-out, analysis-only mode, per-object foldouts, manual selection, detailed reports, and simulation preview. No per-object technical report is required for the default workflow.

Prefab instances are included by default and changes are recorded as scene overrides, as indicated beside the primary action. Prefab source assets are never changed. Disable **Override prefab instance** in Advanced to retain manual review for instances.

Automatic processing checks the exact growing subset of changes with unselected objects unchanged. Existing problems on unrelated objects do not lower every object's confidence or block the entire Canvas. Candidates that introduce detected problems are retained for review. Objects whose anchors already match the recommendation are not mutated or written to a new snapshot. Layout/animation risks remain excluded.

The primary button captures the actual current Canvas pixel resolution before changing Game View. In Anchor & Preview mode, Canvas Scaler settings remain unchanged. Adaptasi Layout deliberately updates the scaling settings as described above. Game View preview is an Editor setting and does not change player build defaults; custom presets are reused or created in memory for the current Editor session. Unity 6 internal Game View APIs are used and resolution mismatches are reported as errors. Pivot and reference geometry remain preserved. All four world corners are checked with a 0.01 world-unit tolerance; failed applications are reverted. The tool improves verified anchors, not semantic layout or rendered typography, and does not guarantee every Canvas becomes responsive after one click.

## Included

- Nearest-parent geometry and descendant scanning.
- Nine point anchors, horizontal/vertical/full stretch, and relative anchors.
- Configurable edge-distance and coverage heuristics, candidate ranking adjusted by simulation issues, and explanation of observed geometry.
- Screen Space Overlay and Screen Space Camera geometry; World Space is excluded.
- Conservative exclusions for layout drivers, fitters, animation ancestry, inactive objects, nested Canvas boundaries, rotated/scaled ancestry, and prefab source assets. Prefab instances record scene overrides and can opt into automatic application.
- 1920×1080, 1920×1200, 1280×960, 3440×1440, reference, and optional custom resolution reports.
- Canvas Scaler Match / Expand / Shrink calculations; out-of-bounds, abnormal stretch, and new or increased sibling overlap detection.
- Batch Undo, persisted last-batch snapshot, stale-analysis detection, and re-simulation of the exact Apply Safe subset with unselected siblings unchanged.

## Limits

The simulator predicts RectTransform bounds without creating live resolution-specific rendered scenes. Text wrapping, masks, safe areas, custom layout scripts, animation bindings and dynamic content require Game View testing. Baseline sibling overlap is treated as intentional and only worsening overlap is reported. Overlaps across different parents are not detected. Candidate ranking is a deterministic heuristic; confidence percentages are not calibrated probabilities. Selection can apply REVIEW recommendations with reported issues. SAFE is a conservative classification within these checks, not proof of all rendering behavior.

Constant Pixel Size and Constant Physical Size receive a warning and approximate simulation; they are not automatically SAFE. Canvas Scaler settings are modified only by the explicit Adaptasi Layout action and included in recovery. Nested layout-controlled subtrees and nested Canvases are deliberately excluded rather than approximating their dynamic behavior.

Snapshots use GlobalObjectId in `Library/ResponsiveUIAssistantSnapshot.json`. Save scenes before Apply. Restore resolves objects in currently loaded scenes, skips missing objects, and supports Undo. Only the latest batch is retained; deleting Library removes the snapshot. Snapshot recovery does not reopen scenes or recreate deleted objects.

The v0.2 and v0.3 roadmap (profiles, safe area, advanced prefab/animation awareness, semantic clustering, group optimization) is deferred. Margin instability beyond the candidate's anchor constraints and rendered visual intent are not independently inferred in this foundation.

## Verification

Run **Tools → Responsive UI Assistant Verification** to exercise geometry preservation for all 13 candidates, pivot/depth preservation, state restoration, parent-aware scoring, layout/animation/world-space/scale exclusions, Canvas Scaler formulas, preset/custom simulation and read-only analysis. Tests create an isolated preview scene and a temporary prefab asset, then close/delete them afterwards. Results are written to `Library/ResponsiveUIAssistant-verification.txt`. Tests do not modify existing scenes or saved snapshots.

Source plan: [Responsive UI Assistant — Brainstorming & Improvement Plan](https://app.notion.com/p/3f11387760f8816eb52fceeff8f8a53a).




## Recovery of old forced image settings

In Advanced, **Pulihkan Preserve Aspect dari snapshot lama** restores only image aspect flags captured by the previous implementation. It leaves RectTransforms, Canvas Scaler, and image styling unchanged and supports Undo. It requires an existing snapshot containing the earlier values and the affected scene loaded. If the snapshot was overwritten or did not capture the original value, the tool cannot infer it; restore from Undo or the original prefab instead. Use this recovery before running another adaptation, which replaces the last snapshot.




