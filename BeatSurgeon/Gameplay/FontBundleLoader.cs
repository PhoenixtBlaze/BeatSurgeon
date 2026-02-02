using AssetBundleLoadingTools.Utilities;
using IPA.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;

// Alias to avoid ambiguity
using ABTAssetBundleExtensions = AssetBundleLoadingTools.Utilities.AssetBundleExtensions;

namespace BeatSurgeon.Gameplay
{
    internal static class FontBundleLoader
    {
        internal const string BundleFileName = "surgeonfonts";
        internal const string DefaultFontAssetName = "TiltNeon-Regular-VariableFont_XROT,YROT SDF";
        internal const string DefaultSelectionValue = "Default";

        internal static string FontsDir => Path.Combine(UnityGame.InstallPath, "UserData", "BeatSurgeon", "Fonts");
        internal static string BundlePath => Path.Combine(FontsDir, BundleFileName);

        internal static TMP_FontAsset BombUsernameFont { get; private set; }

        private static Task _loadTask;
        private static AssetBundle _bundle;
        private static readonly Dictionary<string, TMP_FontAsset> _fontsByName = new Dictionary<string, TMP_FontAsset>(StringComparer.Ordinal);
        private static readonly List<string> _fontOptions = new List<string>();

        // Cache the safe game shader
        private static Shader _safeTmpShader;

        internal static void EnsureFontsDirExists() => Directory.CreateDirectory(FontsDir);

        internal static void CopyBundleFromPluginFolderIfMissing()
        {
            EnsureFontsDirExists();
            if (File.Exists(BundlePath)) return;

            string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (string.IsNullOrEmpty(pluginDir)) return;

            string[] candidates = {
                Path.Combine(pluginDir, BundleFileName),
                Path.Combine(pluginDir, "Assets", BundleFileName),
            };

            string src = candidates.FirstOrDefault(File.Exists);
            if (src == null) return;

            File.Copy(src, BundlePath, true);
            BeatSurgeon.Plugin.Log.Info($"FontBundleLoader: Copied '{src}' -> '{BundlePath}'");
        }

        internal static Task EnsureLoadedAsync()
        {
            if (_loadTask == null) _loadTask = LoadAsync();
            return _loadTask;
        }

        internal static IReadOnlyList<string> GetBombFontOptions()
        {
            if (_fontOptions.Count == 0) return new[] { DefaultSelectionValue };
            return _fontOptions;
        }

        internal static string GetSelectedBombFontOption()
        {
            return BeatSurgeon.Plugin.Settings?.BombFontType ?? DefaultSelectionValue;
        }

        internal static void SetSelectedBombFontOption(string selection)
        {
            if (string.IsNullOrWhiteSpace(selection)) selection = DefaultSelectionValue;

            if (BeatSurgeon.Plugin.Settings != null)
                BeatSurgeon.Plugin.Settings.BombFontType = selection;

            if (_bundle != null)
                ApplySelectionFromConfig();
            else
                _ = EnsureLoadedAsync(); // LoadAsync ends by ApplySelectionFromConfig()
        }




        private static async Task LoadAsync()
        {
            EnsureFontsDirExists();
            BombUsernameFont = null;
            _fontsByName.Clear();
            _fontOptions.Clear();
            _fontOptions.Add(DefaultSelectionValue);

            // Find a safe shader from the game to fix Single Pass Instanced issues
            _safeTmpShader = Resources.FindObjectsOfTypeAll<Shader>().FirstOrDefault(s => s.name.Contains("TextMeshPro/Distance Field"));
            if (_safeTmpShader == null) _safeTmpShader = Resources.FindObjectsOfTypeAll<Shader>().FirstOrDefault(s => s.name.Contains("Distance Field"));

            if (!File.Exists(BundlePath))
            {
                BeatSurgeon.Plugin.Log.Warn($"FontBundleLoader: Missing bundle '{BundlePath}'");
                return;
            }

            if (_bundle == null) _bundle = await ABTAssetBundleExtensions.LoadFromFileAsync(BundlePath);
            if (_bundle == null)
            {
                BeatSurgeon.Plugin.Log.Warn($"FontBundleLoader: Failed to load AssetBundle '{BundlePath}'");
                return;
            }

            TMP_FontAsset[] fonts = _bundle.LoadAllAssets<TMP_FontAsset>();
            if (fonts == null || fonts.Length == 0)
            {
                BeatSurgeon.Plugin.Log.Warn("FontBundleLoader: No TMP_FontAsset found in bundle");
                return;
            }

            int successCount = 0;
            foreach (var font in fonts.Where(f => f != null))
            {
                if (font.atlasTexture == null)
                {
                    BeatSurgeon.Plugin.Log.Warn($"FontBundleLoader: Font '{font.name}' has no atlasTexture, skipping");
                    continue;
                }

                // *** NEW: Get or create material (works for both old and new versions) ***
                Material fontMaterial = GetOrCreateFontMaterial(font);

                if (fontMaterial == null)
                {
                    BeatSurgeon.Plugin.Log.Warn($"FontBundleLoader: Font '{font.name}' - could not get/create material, skipping");
                    continue;
                }

                // Ensure texture is assigned
                if (fontMaterial.mainTexture == null)
                    fontMaterial.mainTexture = font.atlasTexture;

                // *** CRITICAL: Replace shader with the game's safe shader ***
                if (_safeTmpShader != null)
                    fontMaterial.shader = _safeTmpShader;
                else
                    BeatSurgeon.Plugin.Log.Warn("FontBundleLoader: Could not find safe TMP shader! Text might render in one eye only.");

                _fontsByName[font.name] = font;
                if (!string.Equals(font.name, DefaultFontAssetName, StringComparison.OrdinalIgnoreCase))
                {
                    if (!_fontOptions.Contains(font.name)) _fontOptions.Add(font.name);
                }
                successCount++;
                BeatSurgeon.Plugin.Log.Debug($"FontBundleLoader: Successfully loaded font '{font.name}'");
            }

            BeatSurgeon.Plugin.Log.Info($"FontBundleLoader: Loaded {successCount}/{fonts.Length} fonts from bundle");
            BeatSurgeon.Plugin.Log.Info($"FontBundleLoader: Available fonts: {string.Join(", ", _fontOptions.Where(x => x != DefaultSelectionValue))}");
            ApplySelectionFromConfig();
        }

        /// <summary>
        /// Gets existing material or creates a new one. Works for both old and new Unity/TMP versions.
        /// </summary>
        internal static Material GetOrCreateFontMaterial(TMP_FontAsset font)
        {
            if (font == null) return null;

            // STEP 1: Try to get existing material via reflection (old versions)
            Material existingMat = TryGetExistingMaterial(font);
            if (existingMat != null)
            {
                BeatSurgeon.Plugin.Log.Debug($"FontBundleLoader: Found existing material for '{font.name}'");
                return existingMat;
            }

            // STEP 2: Material doesn't exist or isn't accessible - create a new one (new versions)
            BeatSurgeon.Plugin.Log.Debug($"FontBundleLoader: Creating new material for '{font.name}'");

            if (font.atlasTexture == null)
            {
                BeatSurgeon.Plugin.Log.Warn($"FontBundleLoader: Cannot create material for '{font.name}' - no atlas texture");
                return null;
            }

            // Find a TMP shader to use
            Shader tmpShader = _safeTmpShader;
            if (tmpShader == null)
            {
                // Try to find any TMP shader
                tmpShader = Shader.Find("TextMeshPro/Distance Field");
                if (tmpShader == null)
                    tmpShader = Shader.Find("TextMeshPro/Mobile/Distance Field");
                if (tmpShader == null)
                    tmpShader = Resources.FindObjectsOfTypeAll<Shader>()
                        .FirstOrDefault(s => s.name.Contains("TextMeshPro") || s.name.Contains("Distance Field"));
            }

            if (tmpShader == null)
            {
                BeatSurgeon.Plugin.Log.Error($"FontBundleLoader: Cannot create material for '{font.name}' - no TMP shader found");
                return null;
            }

            // Create new material
            Material newMat = new Material(tmpShader);
            newMat.name = $"{font.name} Material";
            newMat.mainTexture = font.atlasTexture;

            // Try to assign it back to the font via reflection
            TrySetMaterial(font, newMat);

            BeatSurgeon.Plugin.Log.Info($"FontBundleLoader: Created new material for '{font.name}'");
            return newMat;
        }

        /// <summary>
        /// Attempts to get existing material via reflection (for old versions).
        /// </summary>
        private static Material TryGetExistingMaterial(TMP_FontAsset font)
        {
            if (font == null) return null;

            try
            {
                // Try 1: TMP_FontAsset.material property (most common in older versions)
                var fontAssetProp = typeof(TMP_FontAsset).GetProperty("material",
                    BindingFlags.Public | BindingFlags.Instance);
                if (fontAssetProp != null && fontAssetProp.CanRead)
                {
                    var mat = fontAssetProp.GetValue(font) as Material;
                    if (mat != null) return mat;
                }

                // Try 2: TMP_Asset base class field
                var assetField = typeof(TMP_Asset).GetField("material",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (assetField != null)
                {
                    var mat = assetField.GetValue(font) as Material;
                    if (mat != null) return mat;
                }

                // Try 3: m_material backing field
                var backingField = typeof(TMP_Asset).GetField("m_material",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (backingField != null)
                {
                    var mat = backingField.GetValue(font) as Material;
                    if (mat != null) return mat;
                }

                // Try 4: Check if there's a "sourceMaterial" property (some TMP versions)
                var sourceMaterialProp = typeof(TMP_FontAsset).GetProperty("sourceMaterial",
                    BindingFlags.Public | BindingFlags.Instance);
                if (sourceMaterialProp != null && sourceMaterialProp.CanRead)
                {
                    var mat = sourceMaterialProp.GetValue(font) as Material;
                    if (mat != null) return mat;
                }
            }
            catch (Exception ex)
            {
                BeatSurgeon.Plugin.Log.Debug($"FontBundleLoader: Reflection attempt failed for '{font.name}': {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Attempts to set material on font via reflection.
        /// </summary>
        private static void TrySetMaterial(TMP_FontAsset font, Material material)
        {
            if (font == null || material == null) return;

            try
            {
                // Try setting via property
                var materialProp = typeof(TMP_FontAsset).GetProperty("material",
                    BindingFlags.Public | BindingFlags.Instance);
                if (materialProp != null && materialProp.CanWrite)
                {
                    materialProp.SetValue(font, material);
                    return;
                }

                // Try setting via field
                var materialField = typeof(TMP_Asset).GetField("material",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (materialField != null)
                {
                    materialField.SetValue(font, material);
                    return;
                }

                // Try m_material backing field
                var backingField = typeof(TMP_Asset).GetField("m_material",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (backingField != null)
                {
                    backingField.SetValue(font, material);
                    return;
                }
            }
            catch (Exception ex)
            {
                BeatSurgeon.Plugin.Log.Debug($"FontBundleLoader: Could not set material via reflection: {ex.Message}");
            }
        }

        private static void ApplySelectionFromConfig()
        {
            string selection = GetSelectedBombFontOption();
            TMP_FontAsset chosen = null;

            if (string.Equals(selection, DefaultSelectionValue, StringComparison.OrdinalIgnoreCase))
            {
                if (!_fontsByName.TryGetValue(DefaultFontAssetName, out chosen)) chosen = _fontsByName.Values.FirstOrDefault();
            }
            else
            {
                if (!_fontsByName.TryGetValue(selection, out chosen))
                {
                    chosen = _fontsByName.Where(kvp => kvp.Key != null && kvp.Key.IndexOf(selection, StringComparison.OrdinalIgnoreCase) >= 0).Select(kvp => kvp.Value).FirstOrDefault();
                }
                if (chosen == null && !_fontsByName.TryGetValue(DefaultFontAssetName, out chosen)) chosen = _fontsByName.Values.FirstOrDefault();
            }

            BombUsernameFont = chosen;
            if (BombUsernameFont != null) BeatSurgeon.Plugin.Log.Info($"FontBundleLoader: Selected bomb font '{BombUsernameFont.name}' (option='{selection}')");
            else BeatSurgeon.Plugin.Log.Warn($"FontBundleLoader: No usable font could be selected (option='{selection}')");
        }

        internal static async Task ReloadAsync()
        {
            // Reset selection output
            BombUsernameFont = null;

            // Clear cached lists/maps
            _fontsByName.Clear();
            _fontOptions.Clear();

            // Force next EnsureLoadedAsync() to run LoadAsync() again
            _loadTask = null;

            if (_bundle != null)
            {
                _bundle.Unload(true);
                _bundle = null;
            }

            await EnsureLoadedAsync();
        }


    }
}
