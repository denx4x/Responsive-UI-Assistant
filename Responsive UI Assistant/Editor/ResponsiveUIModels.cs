using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ResponsiveUIAssistant
{
    public enum Safety { Safe, Review, Skip }
    [Serializable] public sealed class RectState
    {
        public string id;
        public Vector2 min, max, pivot, size;
        public Vector3 position;
        public static RectState Capture(RectTransform t) => new RectState { id = GlobalObjectId.GetGlobalObjectIdSlow(t).ToString(), min = t.anchorMin, max = t.anchorMax, pivot = t.pivot, size = t.sizeDelta, position = t.anchoredPosition3D };
        public void Restore(RectTransform t) { t.anchorMin = min; t.anchorMax = max; t.pivot = pivot; t.sizeDelta = size; t.anchoredPosition3D = position; }
        public bool Matches(RectTransform t) => min == t.anchorMin && max == t.anchorMax && pivot == t.pivot && size == t.sizeDelta && position == t.anchoredPosition3D;
    }
    public sealed class Candidate
    {
        public string name;
        public Vector2 min, max;
        public float score;
        public int issues;
        public Candidate(string name, Vector2 min, Vector2 max) { this.name = name; this.min = min; this.max = max; }
    }
    public sealed class Result
    {
        public RectTransform target, parent;
        public RectState state;
        public Rect bounds;
        public Vector2 parentSize;
        public Candidate proposed;
        public List<Candidate> candidates;
        public Safety safety;
        public string reason, risk;
        public float confidence;
        public bool selected;
    }
    public sealed class ResolutionReport
    {
        public Vector2 resolution;
        public int score = 100;
        public readonly List<string> issues = new List<string>();
        public readonly Dictionary<RectTransform, Rect> rects = new Dictionary<RectTransform, Rect>();
        public readonly Dictionary<RectTransform, Vector2> parentSizes = new Dictionary<RectTransform, Vector2>();
        public readonly Dictionary<RectTransform, int> penalties = new Dictionary<RectTransform, int>();
    }
}
