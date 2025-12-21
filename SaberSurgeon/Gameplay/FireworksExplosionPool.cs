using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SaberSurgeon.Gameplay
{
    public class FireworksExplosionPool : MonoBehaviour
    {
        private static FireworksExplosionPool _instance;
        private static GameObject _go;

        private Material _particleMaterial;
        private readonly Queue<GameObject> _pool = new Queue<GameObject>();

        // ← NEW: Dictionary of available textures
        private static Dictionary<string, Texture2D> _particleTextures = new Dictionary<string, Texture2D>();

        // ← NEW: Currently selected texture
        private static string _selectedTextureType = "Default"; // Default to Sparkle

        public static FireworksExplosionPool Instance
        {
            get
            {
                if (_instance != null) return _instance;
                _go = new GameObject("SaberSurgeonFireworksExplosionPool");
                DontDestroyOnLoad(_go);
                _instance = _go.AddComponent<FireworksExplosionPool>();
                return _instance;
            }
        }

        // ← NEW: Load all particle textures from Beat Saber
        public static void LoadAvailableTextures()
        {
            _particleTextures.Clear();

            var allTextures = Resources.FindObjectsOfTypeAll<Texture2D>();

            // These are the ones found in your logs
            string[] targetNames = { "Sparkle", "Spark", "SmokeParticle", "Default-Particle" };

            foreach (var targetName in targetNames)
            {
                var tex = allTextures.FirstOrDefault(t => t.name == targetName);
                if (tex != null)
                {
                    if (!_particleTextures.ContainsKey(targetName))
                    {
                        _particleTextures[targetName] = tex;
                        Plugin.Log.Info($"FireworksExplosionPool: Loaded texture '{targetName}' ({tex.width}x{tex.height})");
                    }
                }
            }

            if (_particleTextures.Count == 0)
                Plugin.Log.Warn("FireworksExplosionPool: No particle textures loaded!");
            else
                Plugin.Log.Info($"FireworksExplosionPool: Loaded {_particleTextures.Count} particle textures: {string.Join(", ", _particleTextures.Keys)}");

            // Load user's saved preference
            string savedTexture = Plugin.Settings?.BombFireworksTextureType ?? "Sparkle";
            SetTextureType(savedTexture);
        }

        // ← NEW: Get list of available texture options for UI dropdown
        public static List<string> GetAvailableTextureTypes()
        {
            return _particleTextures.Keys.ToList();
        }

        // ← NEW: Set which texture type to use
        public static void SetTextureType(string textureTypeName)
        {
            if (string.IsNullOrWhiteSpace(textureTypeName))
                textureTypeName = "Sparkle";

            if (_particleTextures.ContainsKey(textureTypeName))
            {
                _selectedTextureType = textureTypeName;
                Plugin.Log.Info($"FireworksExplosionPool: Switched to texture type '{textureTypeName}'");

                // Persist to config
                if (Plugin.Settings != null)
                    Plugin.Settings.BombFireworksTextureType = textureTypeName;
            }
            else
            {
                Plugin.Log.Warn($"FireworksExplosionPool: Texture type '{textureTypeName}' not found. Using default.");
                _selectedTextureType = "Sparkle";
            }
        }

        public void SetParticleMaterial(Material mat)
        {
            if (_particleMaterial == null && mat != null)
                _particleMaterial = mat;
        }

        public void Spawn(Vector3 position, Color baseColor, int burstCount = 220, float life = 1.6f)
        {
            var mat = GetOrInitializeParticleMaterial();
            if (mat == null)
            {
                Plugin.Log.Warn("FireworksExplosionPool: Cannot spawn – material is null");
                return;
            }

            var root = GetOrCreateInstance();
            root.transform.position = position;
            root.SetActive(true);

            var color = Color.HSVToRGB(Random.value, 0.85f, 1.0f);
            color.a = 1f;
            color = Color.Lerp(color, baseColor, 0.35f);

            var burst = root.transform.Find("Burst").GetComponent<ParticleSystem>();
            var sparks = root.transform.Find("Sparks").GetComponent<ParticleSystem>();

            ConfigureBurst(burst, color, burstCount, life);
            ConfigureSparks(sparks, color, Mathf.RoundToInt(burstCount * 0.9f), life * 0.9f);

            burst.Play(true);
            sparks.Play(true);

            StartCoroutine(DespawnAfter(root, Mathf.Max(life, 0.1f) + 0.25f));
        }

        private GameObject GetOrCreateInstance()
        {
            // FIX: Loop until we find a valid object or empty the queue
            while (_pool.Count > 0)
            {
                var pooled = _pool.Dequeue();
                // Check if the Unity object is still valid (not destroyed)
                if (pooled != null)
                {
                    return pooled;
                }
            }

            var mat = GetOrInitializeParticleMaterial();
            var root = new GameObject("SaberSurgeonFireworksExplosion");
            root.SetActive(false);
            CreateParticleChild(root.transform, "Burst", mat);
            CreateParticleChild(root.transform, "Sparks", mat);
            return root;
        }


        private static void CreateParticleChild(Transform parent, string name, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            var psr = go.GetComponent<ParticleSystemRenderer>();
            psr.sharedMaterial = mat;

            var main = ps.main;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private static void ConfigureBurst(ParticleSystem ps, Color color, int count, float life)
        {
            var main = ps.main;
            main.startColor = color;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.55f, life);
            main.startSpeed = new ParticleSystem.MinMaxCurve(6f, 14f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
            main.gravityModifier = 0.35f;
            main.maxParticles = 4000;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.03f;

            var emission = ps.emission;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)Mathf.Clamp(count, 1, 2000)) });

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) }
            );
            col.color = new ParticleSystem.MinMaxGradient(grad);
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private static void ConfigureSparks(ParticleSystem ps, Color color, int count, float life)
        {
            var main = ps.main;
            main.startColor = Color.Lerp(color, Color.white, 0.35f);
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.35f, life * 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 7f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.06f);
            main.gravityModifier = 0.9f;
            main.maxParticles = 6000;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.08f;

            var emission = ps.emission;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0.02f, (short)Mathf.Clamp(count, 1, 4000)) });
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private System.Collections.IEnumerator DespawnAfter(GameObject go, float seconds)
        {
            yield return new WaitForSeconds(seconds);
            go.SetActive(false);
            _pool.Enqueue(go);
        }

        private Material GetOrInitializeParticleMaterial()
        {
            if (_particleMaterial != null) return _particleMaterial;

            var particle = Resources.FindObjectsOfTypeAll<ParticleSystemRenderer>()
                .FirstOrDefault(ps => ps.sharedMaterial != null && ps.sharedMaterial.name.Contains("Particle"));

            if (particle != null)
            {
                _particleMaterial = particle.sharedMaterial;
                Plugin.Log.Info("FireworksExplosionPool: Initialized material from game ParticleSystemRenderer");
            }
            else
            {
                var shader = Shader.Find("Standard");
                if (shader != null)
                {
                    _particleMaterial = new Material(shader);
                    _particleMaterial.name = "FireworksParticleMaterial";
                    Plugin.Log.Info("FireworksExplosionPool: Created fallback material from Standard shader");
                }
                else
                {
                    Plugin.Log.Warn("FireworksExplosionPool: Failed to create particle material (no shader found)");
                }
            }

            // ← CRITICAL: Apply the selected texture to the material
            if (_particleMaterial != null && _particleTextures.ContainsKey(_selectedTextureType))
            {
                var tex = _particleTextures[_selectedTextureType];
                _particleMaterial.mainTexture = tex;
                Plugin.Log.Info($"FireworksExplosionPool: Applied texture '{_selectedTextureType}' to material");
            }
            else if (_particleMaterial != null)
            {
                Plugin.Log.Warn($"FireworksExplosionPool: Could not apply texture '{_selectedTextureType}' (not loaded)");
            }

            return _particleMaterial;
        }
    }
}
