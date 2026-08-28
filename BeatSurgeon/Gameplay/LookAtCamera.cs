using UnityEngine;

namespace BeatSurgeon.Gameplay
{
    public class LookAtCamera : MonoBehaviour
    {
        private void LateUpdate()
        {
            if (!PlayerViewCamera.TryGet(out Camera cam)) return;
            transform.LookAt(transform.position + cam.transform.forward);
        }
    }
}
