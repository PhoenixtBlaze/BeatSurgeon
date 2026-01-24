using UnityEngine;

namespace BeatSurgeon.Gameplay
{
    public class LookAtCamera : MonoBehaviour
    {
        private Transform _cam;

        private void Start()
        {
            var cam = Camera.main;
            if (cam != null)
                _cam = cam.transform;
        }

        private void LateUpdate()
        {
            if (_cam == null) return;
            transform.LookAt(transform.position + _cam.forward);
        }
    }
}
