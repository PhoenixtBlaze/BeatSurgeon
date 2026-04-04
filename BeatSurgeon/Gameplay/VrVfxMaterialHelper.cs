using System;
using System.Linq;
using AssetBundleLoadingTools.Utilities;
using UnityEngine;

namespace BeatSurgeon.Gameplay
{
    internal static class VrVfxMaterialHelper
    {
        private const string InvalidShaderName = "ShaderBundleInternal/Invalid";
        private static Material _safeParticleMaterialBase;

        internal static void RepairShaders(GameObject root, string context)
        {
            if (root == null)
            {
                return;
            }

            try
            {
                var result = ShaderRepair.FixShadersOnGameObject(root);
                if (!result.AllShadersReplaced && result.MissingShaderNames.Count > 0)
                {
                    Plugin.Log.Warn(context + ": shader repair missing replacements for " + string.Join(", ", result.MissingShaderNames.Distinct()));
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn(context + ": shader repair failed: " + ex.Message);
            }
        }

        internal static Material CreateSafeParticleMaterial(Material sourceMaterial, Texture fallbackTexture = null)
        {
            if (CanPreserveSourceMaterial(sourceMaterial))
            {
                Material preservedMaterial = new Material(sourceMaterial)
                {
                    name = sourceMaterial.name + "_BeatSurgeonVrSafe"
                };

                ApplySharedParticleDefaults(preservedMaterial, fallbackTexture, preserveTint: true);
                return preservedMaterial;
            }

            Material material = null;
            Material baseMaterial = GetSafeParticleMaterialBase();

            if (baseMaterial != null)
            {
                material = new Material(baseMaterial);
                if (sourceMaterial != null)
                {
                    material.name = sourceMaterial.name + "_BeatSurgeonVrSafeFallback";
                    CopyCommonParticleProperties(sourceMaterial, material);
                }
            }
            else if (sourceMaterial != null)
            {
                material = new Material(sourceMaterial)
                {
                    name = sourceMaterial.name + "_BeatSurgeonFallback"
                };
            }
            else
            {
                Shader shader = Shader.Find("Particles/Standard Unlit")
                    ?? Shader.Find("Particles/Alpha Blended")
                    ?? Shader.Find("Sprites/Default");
                if (shader != null)
                {
                    material = new Material(shader);
                }
            }

            ApplySharedParticleDefaults(material, fallbackTexture, preserveTint: false);
            return material;
        }

        private static bool CanPreserveSourceMaterial(Material sourceMaterial)
        {
            return sourceMaterial != null
                && sourceMaterial.shader != null
                && !string.IsNullOrWhiteSpace(sourceMaterial.shader.name)
                && !string.Equals(sourceMaterial.shader.name, InvalidShaderName, StringComparison.OrdinalIgnoreCase);
        }

        private static void CopyCommonParticleProperties(Material sourceMaterial, Material destinationMaterial)
        {
            if (sourceMaterial == null || destinationMaterial == null)
            {
                return;
            }

            TryCopyTexture(sourceMaterial, destinationMaterial, "_MainTex");
            TryCopyTexture(sourceMaterial, destinationMaterial, "_BaseMap");
            TryCopyColor(sourceMaterial, destinationMaterial, "_TintColor");
            TryCopyColor(sourceMaterial, destinationMaterial, "_Color");
            TryCopyColor(sourceMaterial, destinationMaterial, "_BaseColor");
            TryCopyColor(sourceMaterial, destinationMaterial, "_EmissionColor");
            TryCopyFloat(sourceMaterial, destinationMaterial, "_InvFade");
            TryCopyFloat(sourceMaterial, destinationMaterial, "_Cutoff");
        }

        private static void ApplySharedParticleDefaults(Material material, Texture fallbackTexture, bool preserveTint)
        {
            if (material == null)
            {
                return;
            }

            if (material.mainTexture == null && fallbackTexture != null)
            {
                material.mainTexture = fallbackTexture;
            }

            try
            {
                if (material.HasProperty("_EnableCloseToCameraDisappear"))
                {
                    material.SetFloat("_EnableCloseToCameraDisappear", 0f);
                }

                if (material.HasProperty("_ZWrite"))
                {
                    material.SetFloat("_ZWrite", 0f);
                }

                if (!preserveTint && material.HasProperty("_TintColor"))
                {
                    Color tint = material.GetColor("_TintColor");
                    if (tint.maxColorComponent <= 0f || tint.a <= 0f)
                    {
                        material.SetColor("_TintColor", Color.white);
                    }
                }

                if (material.renderQueue < 3000)
                {
                    material.renderQueue = 3100;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn("VrVfxMaterialHelper: Failed to apply shared particle defaults: " + ex.Message);
            }
        }

        private static Material GetSafeParticleMaterialBase()
        {
            if (_safeParticleMaterialBase != null)
            {
                return _safeParticleMaterialBase;
            }

            ParticleSystemRenderer renderer = FindVanillaParticleRenderer();
            if (renderer?.sharedMaterial == null)
            {
                LogUtils.Debug(() => "VrVfxMaterialHelper: Could not find a deterministic vanilla particle material for VR-safe VFX.");
                return null;
            }

            _safeParticleMaterialBase = new Material(renderer.sharedMaterial)
            {
                name = renderer.sharedMaterial.name + "_BeatSurgeonParticleBase"
            };

            LogUtils.Debug(() =>
                "VrVfxMaterialHelper: Using vanilla particle material '"
                + renderer.sharedMaterial.name
                + "' from '"
                + GetTransformPath(renderer.transform)
                + "' as the VR-safe base.");

            return _safeParticleMaterialBase;
        }

        private static ParticleSystemRenderer FindVanillaParticleRenderer()
        {
            foreach (NoteCutCoreEffectsSpawner spawner in Resources.FindObjectsOfTypeAll<NoteCutCoreEffectsSpawner>())
            {
                ParticleSystemRenderer spawnerRenderer = spawner
                    .GetComponentsInChildren<ParticleSystemRenderer>(true)
                    .FirstOrDefault(IsUsableVanillaParticleRenderer);

                if (spawnerRenderer != null)
                {
                    return spawnerRenderer;
                }
            }

            return Resources.FindObjectsOfTypeAll<ParticleSystemRenderer>()
                .Where(IsUsableVanillaParticleRenderer)
                .OrderByDescending(GetRendererScore)
                .FirstOrDefault();
        }

        private static bool IsUsableVanillaParticleRenderer(ParticleSystemRenderer renderer)
        {
            if (renderer == null || renderer.sharedMaterial == null || renderer.sharedMaterial.shader == null)
            {
                return false;
            }

            string path = GetTransformPath(renderer.transform).ToLowerInvariant();
            return !path.Contains("beatsurgeon")
                && !path.Contains("surgeonexplosion")
                && !path.Contains("outlineparticles")
                && !path.Contains("twitch")
                && !path.Contains("subscriber")
                && !path.Contains("follower")
                && !path.Contains("bitshypercube");
        }

        private static int GetRendererScore(ParticleSystemRenderer renderer)
        {
            string path = GetTransformPath(renderer.transform).ToLowerInvariant();
            int score = 0;

            if (path.Contains("notecut")) score += 500;
            if (path.Contains("shockwave")) score -= 400;
            if (path.Contains("saber")) score += 300;
            if (path.Contains("spark")) score += 150;
            if (path.Contains("burn")) score += 100;
            if (path.Contains("dust")) score += 100;
            if (path.Contains("core")) score += 50;
            if (renderer.sharedMaterial != null && renderer.sharedMaterial.mainTexture != null) score += 25;

            return score;
        }

        private static void TryCopyTexture(Material sourceMaterial, Material destinationMaterial, string propertyName)
        {
            if (!sourceMaterial.HasProperty(propertyName) || !destinationMaterial.HasProperty(propertyName))
            {
                return;
            }

            try
            {
                Texture texture = sourceMaterial.GetTexture(propertyName);
                if (texture != null)
                {
                    destinationMaterial.SetTexture(propertyName, texture);
                }
            }
            catch { }
        }

        private static void TryCopyColor(Material sourceMaterial, Material destinationMaterial, string propertyName)
        {
            if (!sourceMaterial.HasProperty(propertyName) || !destinationMaterial.HasProperty(propertyName))
            {
                return;
            }

            try
            {
                destinationMaterial.SetColor(propertyName, sourceMaterial.GetColor(propertyName));
            }
            catch { }
        }

        private static void TryCopyFloat(Material sourceMaterial, Material destinationMaterial, string propertyName)
        {
            if (!sourceMaterial.HasProperty(propertyName) || !destinationMaterial.HasProperty(propertyName))
            {
                return;
            }

            try
            {
                destinationMaterial.SetFloat(propertyName, sourceMaterial.GetFloat(propertyName));
            }
            catch { }
        }

        private static string GetTransformPath(Transform transform)
        {
            if (transform == null)
            {
                return string.Empty;
            }

            return transform.parent == null
                ? transform.name
                : GetTransformPath(transform.parent) + "/" + transform.name;
        }
    }
}