using UnityEngine;

namespace Aprender
{
    public sealed class WorldLabel : MonoBehaviour
    {
        public bool FaceCamera { get; set; }

        private void LateUpdate()
        {
            if (FaceCamera && Camera.main != null)
                transform.rotation = Camera.main.transform.rotation;
        }
    }
}
