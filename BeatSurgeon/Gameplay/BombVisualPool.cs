using System.Collections.Generic;
using UnityEngine;

namespace BeatSurgeon.Gameplay
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

                _go = new GameObject("BeatSurgeon_BombVisualPool_GO");
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
            inst.PlayParticleSystems();

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
            var root = new GameObject("BeatSurgeon_BombVisual");
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

                // Replace bundle shaders that don't support SPI VR rendering on mesh renderers.
                // Bundle shaders such as Custom/FogLighting are compiled without SPI instancing
                // support and render in only one eye. Custom/SimpleLit is a game-side SPI-compatible shader.
                ReplaceBundleMeshShaders(instance);
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
                        if (safeShader != null)
                            _sphereSharedMaterial = CreateEmissiveSimpleLitMaterial(safeShader, "BombSphere");
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

        // Replaces non-SPI-capable bundle shaders on mesh renderers with Custom/SimpleLit.
        // Particle system renderers are intentionally skipped; they are handled by FireworksExplosionPool.
        //
        // FogLighting (and similar) often keep albedo near-black and put brightness in tint/emission.
        // Copying only _Color into SimpleLit made bombs render black. Seed a bright emissive base
        // here; Rent -> ApplyColor then tints that base to the replaced note's color.
        private static void ReplaceBundleMeshShaders(GameObject root)
        {
            Shader safeShader = Shader.Find("Custom/SimpleLit") ?? Shader.Find("Standard");
            if (safeShader == null)
            {
                return;
            }

            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || r is ParticleSystemRenderer)
                {
                    continue;
                }

                Material[] mats = r.sharedMaterials;
                if (mats == null || mats.Length == 0)
                {
                    continue;
                }

                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null)
                    {
                        continue;
                    }

                    Shader s = mats[i].shader;
                    string shaderName = s != null ? (s.name ?? "") : "";

                    bool needsReplacement = s == null
                        || !s.isSupported
                        || shaderName.ToUpperInvariant().Contains("FOGLIGHTING")
                        || shaderName.ToUpperInvariant().Contains("INTERNALERRORSHADER");

                    if (!needsReplacement)
                    {
                        continue;
                    }

                    mats[i] = CreateEmissiveSimpleLitMaterial(safeShader, mats[i].name);
                    changed = true;
                }

                if (changed)
                {
                    r.sharedMaterials = mats;
                }
            }
        }

        /// <summary>
        /// SPI-safe bomb mesh material. White albedo + emission keyword so ApplyColor can tint
        /// with the note color without inheriting FogLighting's near-black _Color.
        /// </summary>
        private static Material CreateEmissiveSimpleLitMaterial(Shader safeShader, string sourceName)
        {
            Material mat = new Material(safeShader)
            {
                name = (string.IsNullOrEmpty(sourceName) ? "BombMesh" : sourceName) + "_BeatSurgeonVrSafe"
            };

            // Neutral bright base — note color is applied per rent via MaterialPropertyBlock.
            Color baseColor = Color.white;
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", baseColor);
            if (mat.HasProperty("_SimpleColor")) mat.SetColor("_SimpleColor", baseColor);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", baseColor);
            if (mat.HasProperty("_TintColor")) mat.SetColor("_TintColor", baseColor);
            if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", baseColor);

            try
            {
                mat.EnableKeyword("_EMISSION");
            }
            catch { }

            return mat;
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

        public void PlayParticleSystems()
        {
            var systems = GetComponentsInChildren<ParticleSystem>(true);
            foreach (var ps in systems)
            {
                if (ps == null) continue;
                try
                {
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    ps.Play(true);
                }
                catch { }
            }
        }

        public void ApplyColor(Color noteColor)
        {
            if (_renderers == null || _renderers.Length == 0)
                CacheRenderers();

            // Match the replaced note's hue; keep alpha opaque and boost emission so bombs
            // stay readable under GameCore lighting after the FogLighting -> SimpleLit swap.
            Color albedo = noteColor;
            albedo.a = 1f;
            if (albedo.maxColorComponent < 0.05f)
            {
                albedo = Color.white;
            }

            Color emission = noteColor * 2f;
            emission.a = 1f;
            if (emission.maxColorComponent < 0.1f)
            {
                emission = Color.white * 1.5f;
                emission.a = 1f;
            }

            foreach (var r in _renderers)
            {
                if (r == null) continue;

                var mats = r.sharedMaterials;
                if (mats == null || mats.Length == 0) continue;

                for (int i = 0; i < mats.Length; i++)
                {
                    var mat = mats[i];
                    if (mat == null) continue;

                    try
                    {
                        mat.EnableKeyword("_EMISSION");
                    }
                    catch { }

                    _mpb.Clear();
                    bool any = false;

                    if (mat.HasProperty(ColorId)) { _mpb.SetColor(ColorId, albedo); any = true; }
                    if (mat.HasProperty(SimpleColorId)) { _mpb.SetColor(SimpleColorId, albedo); any = true; }
                    if (mat.HasProperty(BaseColorId)) { _mpb.SetColor(BaseColorId, albedo); any = true; }
                    if (mat.HasProperty(TintColorId)) { _mpb.SetColor(TintColorId, albedo); any = true; }
                    if (mat.HasProperty(EmissionColorId)) { _mpb.SetColor(EmissionColorId, emission); any = true; }

                    if (any) r.SetPropertyBlock(_mpb, i);
                }
            }
        }
    }
}
