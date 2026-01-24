using TMPro;
using UnityEngine;

namespace BeatSurgeon.Gameplay
{
    public class BeatSurgeonTextMover : MonoBehaviour
    {
        private Vector3 _start;
        private Vector3 _end;
        private float _life;
        private float _t;
        private TextMeshPro _tmp;
        private Color _baseColor;

        public void Init(Vector3 startPos, Vector3 endPos, float lifetime)
        {
            _start = startPos;
            _end = endPos;
            _life = Mathf.Max(0.1f, lifetime);
        }

        private void Awake()
        {
            _tmp = GetComponent<TextMeshPro>();
            if (_tmp != null)
                _baseColor = _tmp.color;
        }

        private void Update()
        {
            if (_life <= 0f || _tmp == null) return;

            _t += Time.deltaTime / _life;
            float clamped = Mathf.Clamp01(_t);
            transform.position = Vector3.Lerp(_start, _end, clamped);

            var c = _baseColor;
            c.a = 1f - clamped;
            _tmp.color = c;
        }
    }
}
