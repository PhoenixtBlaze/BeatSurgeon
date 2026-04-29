using System;
using System.Linq;
using AssetBundleLoadingTools.Utilities;
using UnityEngine;

namespace BeatSurgeon.Gameplay
{
    internal static class VrVfxMaterialHelper
    {
        private const string InvalidShaderName = "ShaderBundleInternal/Invalid";
        private static readonly string[] DeterministicTransparentParticleShaders =
        {
            "Particles/Alpha Blended",
            "Particles/Standard Unlit",
            "Legacy Shaders/Particles/Alpha Blended",
            "Sprites/Default"
        };

        private static readonly string[] RejectedFallbackShaderTokens =
        {
            "screendisplacement",
            "distortion",
            "obstacle",
            "mirror",
            "water"
        };

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

        internal static void RepairShader(Material material, string context)
        {
            if (material == null)
            {
                return;
            }

            try
            {
                var result = ShaderRepair.FixShaderOnMaterial(material);
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

        internal static Material CreatePreparedParticleMaterial(Material sourceMaterial, string context, Texture fallbackTexture = null)
        {
            if (sourceMaterial == null)
            {
                return CreateSafeParticleMaterial(null, fallbackTexture);
            }

            Texture resolvedFallbackTexture = fallbackTexture ?? GetBestAvailableTexture(sourceMaterial);
            Material repairedClone = new Material(sourceMaterial)
            {
                name = sourceMaterial.name + "_BeatSurgeonPrepared"
            };

            RepairShader(repairedClone, context);
            if (CanPreserveSourceMaterial(repairedClone))
            {
                ApplySharedParticleDefaults(repairedClone, resolvedFallbackTexture, preserveTint: true);
                return repairedClone;
            }

            try
            {
                UnityEngine.Object.Destroy(repairedClone);
            }
            catch { }

            return CreateSafeParticleMaterial(sourceMaterial, resolvedFallbackTexture);
        }

        internal static Material CreateForcedSafeParticleMaterial(Material sourceMaterial, Texture fallbackTexture = null)
        {
            Texture resolvedFallbackTexture = fallbackTexture ?? GetBestAvailableTexture(sourceMaterial);

            Material material = CreateDeterministicTransparentParticleMaterial(sourceMaterial, "_BeatSurgeonForcedSafe");
            if (material == null)
            {
                Material baseMaterial = GetSafeParticleMaterialBase();
                if (baseMaterial != null)
                {
                    material = new Material(baseMaterial);
                    if (sourceMaterial != null)
                    {
                        material.name = sourceMaterial.name + "_BeatSurgeonForcedSafeFallback";
                        CopyCommonParticleProperties(sourceMaterial, material);
                    }
                }
                else if (sourceMaterial != null)
                {
                    material = new Material(sourceMaterial)
                    {
                        name = sourceMaterial.name + "_BeatSurgeonForcedSafeFallback"
                    };
                }
            }

            ApplySharedParticleDefaults(material, resolvedFallbackTexture, preserveTint: false);
            ApplyDeterministicParticleBlend(material);
            return material;
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

        internal static bool HasUsableShader(Material sourceMaterial)
        {
            return CanPreserveSourceMaterial(sourceMaterial);
        }

        internal static Texture GetBestAvailableTexture(Material sourceMaterial)
        {
            if (sourceMaterial == null)
            {
                return null;
            }

            try
            {
                if (sourceMaterial.mainTexture != null)
                {
                    return sourceMaterial.mainTexture;
                }
            }
            catch { }

            string[] commonTextureProperties =
            {
                "_MainTex",
                "_BaseMap",
                "_EmissionMap",
                "_AlphaTex",
                "_MaskTex",
                "_DetailAlbedoMap"
            };

            foreach (string propertyName in commonTextureProperties)
            {
                try
                {
                    if (!sourceMaterial.HasProperty(propertyName))
                    {
                        continue;
                    }

                    Texture texture = sourceMaterial.GetTexture(propertyName);
                    if (texture != null)
                    {
                        return texture;
                    }
                }
                catch { }
            }

            try
            {
                foreach (string propertyName in sourceMaterial.GetTexturePropertyNames())
                {
                    Texture texture = sourceMaterial.GetTexture(propertyName);
                    if (texture != null)
                    {
                        return texture;
                    }
                }
            }
            catch { }

            return null;
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

            Texture bestTexture = GetBestAvailableTexture(sourceMaterial);
            if (bestTexture != null)
            {
                TryAssignTexture(destinationMaterial, bestTexture);
            }
        }

        private static void ApplySharedParticleDefaults(Material material, Texture fallbackTexture, bool preserveTint)
        {
            if (material == null)
            {
                return;
            }

            if (fallbackTexture == null)
            {
                fallbackTexture = GetBestAvailableTexture(material);
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

                if (!preserveTint)
                {
                    NormalizeVisibleColor(material, "_Color");
                    NormalizeVisibleColor(material, "_BaseColor");
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

        private static void TryAssignTexture(Material material, Texture texture)
        {
            if (material == null || texture == null)
            {
                return;
            }

            string[] commonTextureProperties =
            {
                "_MainTex",
                "_BaseMap"
            };

            foreach (string propertyName in commonTextureProperties)
            {
                try
                {
                    if (material.HasProperty(propertyName))
                    {
                        material.SetTexture(propertyName, texture);
                    }
                }
                catch { }
            }

            try
            {
                if (material.mainTexture == null)
                {
                    material.mainTexture = texture;
                }
            }
            catch { }
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
            string shaderName = renderer.sharedMaterial.shader.name;
            return !path.Contains("beatsurgeon")
                && !path.Contains("surgeonexplosion")
                && !path.Contains("outlineparticles")
                && !path.Contains("twitch")
                && !path.Contains("subscriber")
                && !path.Contains("follower")
                && !path.Contains("bitshypercube")
                && !IsRejectedFallbackShader(shaderName);
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
            if (renderer.sharedMaterial != null && IsLikelyParticleShader(renderer.sharedMaterial.shader?.name)) score += 100;

            return score;
        }

        private static Material CreateDeterministicTransparentParticleMaterial(Material sourceMaterial, string nameSuffix)
        {
            Shader shader = FindDeterministicTransparentParticleShader();
            if (shader == null)
            {
                return null;
            }

            Material material = new Material(shader)
            {
                name = sourceMaterial != null
                    ? sourceMaterial.name + nameSuffix
                    : "BeatSurgeonForcedSafeParticle"
            };

            if (sourceMaterial != null)
            {
                CopyCommonParticleProperties(sourceMaterial, material);
            }

            return material;
        }

        private static Shader FindDeterministicTransparentParticleShader()
        {
            foreach (string shaderName in DeterministicTransparentParticleShaders)
            {
                Shader shader = Shader.Find(shaderName);
                if (shader != null)
                {
                    return shader;
                }
            }

            return null;
        }

        private static void ApplyDeterministicParticleBlend(Material material)
        {
            if (material == null)
            {
                return;
            }

            TrySetFloat(material, "_Mode", 2f);
            TrySetFloat(material, "_Surface", 1f);
            TrySetFloat(material, "_Blend", 0f);
            TrySetFloat(material, "_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            TrySetFloat(material, "_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            TrySetFloat(material, "_AlphaClip", 0f);
            TrySetFloat(material, "_Cutoff", 0f);

            if (material.renderQueue < 3000)
            {
                material.renderQueue = 3100;
            }
        }

        private static bool IsRejectedFallbackShader(string shaderName)
        {
            if (string.IsNullOrWhiteSpace(shaderName))
            {
                return true;
            }

            string normalizedShaderName = shaderName.ToLowerInvariant();
            return RejectedFallbackShaderTokens.Any(normalizedShaderName.Contains);
        }

        private static bool IsLikelyParticleShader(string shaderName)
        {
            if (string.IsNullOrWhiteSpace(shaderName))
            {
                return false;
            }

            string normalizedShaderName = shaderName.ToLowerInvariant();
            return normalizedShaderName.Contains("particle")
                || normalizedShaderName.Contains("particles")
                || normalizedShaderName.Contains("sprite")
                || normalizedShaderName.Contains("alpha");
        }

        private static void NormalizeVisibleColor(Material material, string propertyName)
        {
            if (material == null || !material.HasProperty(propertyName))
            {
                return;
            }

            try
            {
                Color color = material.GetColor(propertyName);
                if (color.maxColorComponent <= 0f || color.a <= 0f)
                {
                    material.SetColor(propertyName, Color.white);
                }
            }
            catch { }
        }

        private static void TrySetFloat(Material material, string propertyName, float value)
        {
            if (material == null || !material.HasProperty(propertyName))
            {
                return;
            }

            try
            {
                material.SetFloat(propertyName, value);
            }
            catch { }
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