using UnityEngine;

namespace Aprender
{
    [DefaultExecutionOrder(100)]
    public sealed class WorldLabel : MonoBehaviour
    {
        public bool FaceCamera { get; set; }
        public float MinimumPixelHeight { get; set; }
        public float ReferenceHeight { get; set; } = 1f;
        public float MaximumScale { get; set; } = 1.35f;

        private Camera labelCamera;

        private void LateUpdate()
        {
            if (!FaceCamera && MinimumPixelHeight <= 0) return;
            if (labelCamera == null) labelCamera = Camera.main;
            if (labelCamera == null) return;
            if (FaceCamera) transform.rotation = labelCamera.transform.rotation;
            if (MinimumPixelHeight <= 0 || !labelCamera.orthographic) return;

            // Small landscape viewports need a little more sign area. Bound the
            // enlargement so neighbouring portal signs retain their own space.
            var pixelsPerUnit = labelCamera.pixelHeight / (2f * labelCamera.orthographicSize);
            var scale = Mathf.Clamp(MinimumPixelHeight / Mathf.Max(1f, ReferenceHeight * pixelsPerUnit),
                1f, MaximumScale);
            transform.localScale = Vector3.one * scale;
        }
    }
}
