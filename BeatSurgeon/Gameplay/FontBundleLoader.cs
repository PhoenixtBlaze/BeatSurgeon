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
            _safeTmpShader = Resources.FindObjectsOfTypeAll<Shader>().FirstOrDefault(s => s.name.Contains("TextMeshPro/Distance Field")); // Standard TMP shader usually works if game loaded it
            if (_safeTmpShader == null) _safeTmpShader = Resources.FindObjectsOfTypeAll<Shader>().FirstOrDefault(s => s.name.Contains("Distance Field")); // Fallback

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

            foreach (var font in fonts.Where(f => f != null))
            {
                if (font.atlasTexture == null || font.material == null) continue;

                if (font.material.mainTexture == null) font.material.mainTexture = font.atlasTexture;

                // *** CRITICAL FIX: Replace shader with the game's safe shader ***
                // This fixes the "Left Eye Only" bug caused by using a bundle-baked shader that doesn't support SPI.
                // Shader fix (keep this)
                if (_safeTmpShader != null) font.material.shader = _safeTmpShader;

                else
                {
                    BeatSurgeon.Plugin.Log.Warn("FontBundleLoader: Could not find safe TMP shader! Text might render in one eye only.");
                }


                _fontsByName[font.name] = font;
                if (!string.Equals(font.name, DefaultFontAssetName, StringComparison.OrdinalIgnoreCase))
                {
                    if (!_fontOptions.Contains(font.name)) _fontOptions.Add(font.name);
                }
            }

            BeatSurgeon.Plugin.Log.Info($"FontBundleLoader: Fonts in bundle: {string.Join(", ", _fontOptions.Where(x => x != DefaultSelectionValue))}");
            ApplySelectionFromConfig();
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
