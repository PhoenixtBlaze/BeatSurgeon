using System.Collections.Generic;
using UnityEngine;

namespace SaberSurgeon.Gameplay
{
    internal sealed class BombVisualPool : MonoBehaviour
    {
        private static BombVisualPool _instance;
        private static GameObject _go;

        private readonly Queue<BombVisualInstance> _pool = new Queue<BombVisualInstance>();

        private static Material _sphereSharedMaterial;

        public static BombVisualPool Instance
        {
            get
            {
                if (_instance != null) return _instance;

                _go = new GameObject("SaberSurgeon_BombVisualPool_GO");
                Object.DontDestroyOnLoad(_go);
                _instance = _go.AddComponent<BombVisualPool>();
                return _instance;
            }
        }

        public BombVisualInstance Rent(Transform noteParent, int layer, Color color, GameObject bombPrefabGoOrNull)
        {
            var inst = GetOrCreate(bombPrefabGoOrNull);

            var t = inst.transform;
            t.SetParent(noteParent, false);
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;

            SetLayerRecursively(t, layer);

            inst.gameObject.SetActive(true);
            inst.ApplyColor(color);

            return inst;
        }

        public void Return(BombVisualInstance inst)
        {
            if (inst == null) return;

            inst.gameObject.SetActive(false);
            inst.transform.SetParent(_go.transform, false);
            _pool.Enqueue(inst);
        }

        private BombVisualInstance GetOrCreate(GameObject bombPrefabGoOrNull)
        {
            while (_pool.Count > 0)
            {
                var inst = _pool.Dequeue();
                if (inst != null) return inst;
            }

            return CreateNew(bombPrefabGoOrNull);
        }

        private BombVisualInstance CreateNew(GameObject bombPrefabGoOrNull)
        {
            var root = new GameObject("SaberSurgeon_BombVisual");
            root.transform.SetParent(_go.transform, false);
            root.SetActive(false);

            if (bombPrefabGoOrNull != null)
            {
                var instance = Object.Instantiate(bombPrefabGoOrNull, root.transform);
                instance.name = "BombPrefabInstance";
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one;
                instance.SetActive(true);

                // One-time cleanup only (instead of every bomb spawn)
                foreach (var bomb in instance.GetComponentsInChildren<BombNoteController>(true))
                {
                    bomb.enabled = false;
                    Object.Destroy(bomb);
                }

                foreach (var col in instance.GetComponentsInChildren<Collider>(true))
                    Object.Destroy(col);
            }
            else
            {
                // One-time sphere fallback only
                var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere.name = "BombSphere";
                sphere.transform.SetParent(root.transform, false);
                sphere.transform.localPosition = Vector3.zero;
                sphere.transform.localRotation = Quaternion.identity;
                sphere.transform.localScale = Vector3.one * 0.45f;
                sphere.SetActive(true);

                var col = sphere.GetComponent<Collider>();
                if (col != null) Object.Destroy(col);

                var mr = sphere.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    if (_sphereSharedMaterial == null)
                    {
                        var safeShader = Shader.Find("Custom/SimpleLit") ?? Shader.Find("Standard");
                        if (safeShader != null) _sphereSharedMaterial = new Material(safeShader);
                    }

                    if (_sphereSharedMaterial != null)
                        mr.sharedMaterial = _sphereSharedMaterial;
                }
            }

            var instComp = root.AddComponent<BombVisualInstance>();
            instComp.CacheRenderers();
            return instComp;
        }

        private static void SetLayerRecursively(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++)
                SetLayerRecursively(t.GetChild(i), layer);
        }
    }

    internal sealed class BombVisualInstance : MonoBehaviour
    {
        private Renderer[] _renderers;

        // Keep these in sync with BombNotePatch's shader property usage
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int SimpleColorId = Shader.PropertyToID("_SimpleColor");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int TintColorId = Shader.PropertyToID("_TintColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private static readonly MaterialPropertyBlock _mpb = new MaterialPropertyBlock();

        public void CacheRenderers()
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
        }

        public void ApplyColor(Color noteColor)
        {
            if (_renderers == null || _renderers.Length == 0)
                CacheRenderers();

            foreach (var r in _renderers)
            {
                if (r == null) continue;

                var mats = r.sharedMaterials;
                if (mats == null || mats.Length == 0) continue;

                for (int i = 0; i < mats.Length; i++)
                {
                    var mat = mats[i];
                    if (mat == null) continue;

                    _mpb.Clear();
                    bool any = false;

                    if (mat.HasProperty(ColorId)) { _mpb.SetColor(ColorId, noteColor); any = true; }
                    if (mat.HasProperty(SimpleColorId)) { _mpb.SetColor(SimpleColorId, noteColor); any = true; }
                    if (mat.HasProperty(BaseColorId)) { _mpb.SetColor(BaseColorId, noteColor); any = true; }
                    if (mat.HasProperty(TintColorId)) { _mpb.SetColor(TintColorId, noteColor); any = true; }
                    if (mat.HasProperty(EmissionColorId)) { _mpb.SetColor(EmissionColorId, noteColor); any = true; }

                    if (any) r.SetPropertyBlock(_mpb, i);
                }
            }
        }
    }
}
