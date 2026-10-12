using UnityEngine;

namespace ResponsiveUIAssistant
{
    /// <summary>Uniformly fits the original composition into its Canvas without editing individual UI elements.</summary>
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(RectTransform))]
    public sealed class ReferenceLayoutFrame : MonoBehaviour
    {
        [SerializeField] Vector2 designSize = new Vector2(1440, 960);
        [SerializeField] Vector2 designResolution = new Vector2(1440, 960);
        public Vector2 DesignSize => designSize;
        public Vector2 DesignResolution => designResolution;
        bool fitting;
        public void Configure(Vector2 size, Vector2 resolution)
        {
            designSize = size; designResolution = resolution; Fit();
        }
        void OnEnable() => Fit();
        void LateUpdate() => Fit();
        void OnRectTransformDimensionsChange() => Fit();
        void OnValidate() => Fit();
        public void Fit()
        {
            if (fitting || designSize.x <= 0 || designSize.y <= 0) return;
            var rect = transform as RectTransform;
            var parent = transform.parent as RectTransform;
            if (!rect || !parent || parent.rect.width <= 0 || parent.rect.height <= 0) return;
            fitting = true;
            try
            {
                float scale = Mathf.Min(parent.rect.width / designSize.x, parent.rect.height / designSize.y);
                Vector2 position = Vector2.Scale(designSize, rect.pivot - Vector2.one * .5f) * scale;
                if (rect.anchorMin != Vector2.one * .5f) rect.anchorMin = Vector2.one * .5f;
                if (rect.anchorMax != Vector2.one * .5f) rect.anchorMax = Vector2.one * .5f;
                if (rect.sizeDelta != designSize) rect.sizeDelta = designSize;
                var fittedScale = new Vector3(scale, scale, 1);
                if (rect.localScale != fittedScale) rect.localScale = fittedScale;
                if (rect.anchoredPosition != position) rect.anchoredPosition = position;
            }
            finally { fitting = false; }
        }
    }
}
