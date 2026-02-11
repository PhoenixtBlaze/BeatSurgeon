using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace BeatSurgeon.Gameplay
{
    public class FireworksExplosionPool : MonoBehaviour
    {
        private static FireworksExplosionPool _instance;
        private static GameObject _go;

        private GameObject _explosionPrefab;
        private readonly Queue<GameObject> _pool = new Queue<GameObject>();

        // Cache a known-good material from the game itself
        private static Material _gameSafeMaterial;

        public static FireworksExplosionPool Instance
        {
            get
            {
                if (_instance != null) return _instance;
                _go = new GameObject("BeatSurgeonFireworksExplosionPool");
                DontDestroyOnLoad(_go);
                _instance = _go.AddComponent<FireworksExplosionPool>();
                return _instance;
            }
        }

        private void Awake()
        {
            // 1. Find a safe material from the game (Dust, Sparkle, etc.)
            // This guarantees VR Single Pass Instanced support.
            var existingParticle = Resources.FindObjectsOfTypeAll<ParticleSystemRenderer>()
                .FirstOrDefault(r => r.sharedMaterial != null && r.sharedMaterial.shader.name.Contains("Particle"));

            if (existingParticle != null)
            {
                // Create a clone of this safe material
                _gameSafeMaterial = new Material(existingParticle.sharedMaterial);
                // Ensure it uses Additive blending for glow
                _gameSafeMaterial.SetFloat("_Mode", 2); // Additive usually
                _gameSafeMaterial.SetColor("_TintColor", Color.white);
                LogUtils.Debug(() => $"FireworksExplosionPool: Cloned safe game material: {_gameSafeMaterial.name}");
            }
            else
            {
                // Fallback if game hasn't loaded particles yet (unlikely during song)
                Plugin.Log.Warn("FireworksExplosionPool: Could not find game particle material. Trying standard shader.");
                var shader = Shader.Find("Particles/Standard Unlit");
                if (shader) _gameSafeMaterial = new Material(shader);
            }

            LoadAssetBundle();
        }

        private void LoadAssetBundle()
        {
            if (_explosionPrefab != null) return;

            string bundlePath = Path.Combine(Environment.CurrentDirectory, "UserData", "BeatSurgeon", "Effects", "surgeoneffects");
            if (!File.Exists(bundlePath)) return;

            try
            {
                var bundle = AssetBundle.LoadFromFile(bundlePath);
                if (bundle == null) return;

                _explosionPrefab = bundle.LoadAsset<GameObject>("SurgeonExplosion");
                bundle.Unload(false);
            }
            catch (System.Exception ex)
            {
                Plugin.Log.Error($"FireworksExplosionPool: Bundle error: {ex.Message}");
            }
        }

        public void Spawn(Vector3 position, Color baseColor, int burstCount = 220, float life = 1.6f)
        {
            if (_explosionPrefab == null)
            {
                LoadAssetBundle();
                if (_explosionPrefab == null) return;
            }

            GameObject explosion = GetOrCreateInstance();
            explosion.transform.position = position;

            // FIX 1: Force Layer 0 (Default) for VR rendering
            SetLayerRecursively(explosion, 0);

            // Create Rainbow Gradient
            Gradient rainbowGradient = new Gradient();
            rainbowGradient.SetKeys(
                new GradientColorKey[] {
                    new GradientColorKey(Color.red, 0.0f),
                    new GradientColorKey(Color.yellow, 0.15f),
                    new GradientColorKey(Color.green, 0.3f),
                    new GradientColorKey(Color.cyan, 0.5f),
                    new GradientColorKey(Color.blue, 0.65f),
                    new GradientColorKey(Color.magenta, 0.8f),
                    new GradientColorKey(Color.red, 1.0f)
                },
                new GradientAlphaKey[] { new GradientAlphaKey(1.0f, 0.0f), new GradientAlphaKey(0.0f, 1.0f) }
            );

            // FIX 2: Apply Material and Colors
            var renderers = explosion.GetComponentsInChildren<ParticleSystemRenderer>();
            foreach (var r in renderers)
            {
                // Swap to the safe game material (fixes Left Eye)
                // We keep the main texture from your prefab if possible, or use the game's default
                if (_gameSafeMaterial != null)
                {
                    var texture = r.sharedMaterial?.mainTexture; // Save your star texture
                    r.material = _gameSafeMaterial; // Apply safe shader
                    if (texture != null) r.material.mainTexture = texture; // Restore star texture
                }
            }

            var systems = explosion.GetComponentsInChildren<ParticleSystem>();
            foreach (var ps in systems)
            {
                var main = ps.main;

                // Force base color to white so rainbow tint works
                // Use MinMaxGradient to apply rainbow
                main.startColor = new ParticleSystem.MinMaxGradient(rainbowGradient);

                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.Play(true);
            }

            explosion.SetActive(true);
            StartCoroutine(DespawnAfter(explosion, 2.5f));
        }

        private void SetLayerRecursively(GameObject obj, int newLayer)
        {
            obj.layer = newLayer;
            foreach (Transform child in obj.transform)
            {
                SetLayerRecursively(child.gameObject, newLayer);
            }
        }

        private GameObject GetOrCreateInstance()
        {
            while (_pool.Count > 0)
            {
                var pooled = _pool.Dequeue();
                if (pooled != null) return pooled;
            }
            var newObj = Instantiate(_explosionPrefab);
            DontDestroyOnLoad(newObj);
            return newObj;
        }

        private System.Collections.IEnumerator DespawnAfter(GameObject go, float seconds)
        {
            yield return new WaitForSeconds(seconds);
            go.SetActive(false);
            _pool.Enqueue(go);
        }

        // Stubs
        public static void LoadAvailableTextures() { }
        public static List<string> GetAvailableTextureTypes() { return new List<string> { "Default" }; }
        public static void SetTextureType(string t) { }
    }
}
