using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.ViewControllers;
using BeatSaberMarkupLanguage.Components.Settings;
using HMUI;
using BeatSurgeon.Chat;
using BeatSurgeon.Gameplay;
using BeatSurgeon.Twitch;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using IPA.Utilities.Async; // for UnityMainThreadTaskScheduler



namespace BeatSurgeon.UI.Controllers
{
    [ViewDefinition("BeatSurgeon.UI.Views.BeatSurgeonCooldowns.bsml")]
    [HotReload(RelativePathToLayout = @"..\Views\BeatSurgeonCooldowns.bsml")]
    public class BeatSurgeonCooldownViewController : BSMLAutomaticViewController
    {


        // === Cooldown bindings ===

        [UIValue("global_cd_enabled")]
        public bool GlobalCooldownEnabled
        {
            get => CommandHandler.GlobalCooldownEnabled;
            set
            {
                CommandHandler.GlobalCooldownEnabled = value;
                if (Plugin.Settings != null)
                    Plugin.Settings.GlobalCooldownEnabled = value;

                NotifyPropertyChanged(nameof(GlobalCooldownEnabled));
            }
        }

        [UIValue("global_cd_seconds")]
        public float GlobalCooldownSeconds
        {
            get => CommandHandler.GlobalCooldownSeconds;
            set
            {
                float clamped = Mathf.Clamp(value, 0f, 300f);
                CommandHandler.GlobalCooldownSeconds = clamped;
                if (Plugin.Settings != null)
                    Plugin.Settings.GlobalCooldownSeconds = clamped;

                NotifyPropertyChanged(nameof(GlobalCooldownSeconds));
            }
        }

        [UIValue("per_command_cd_enabled")]
        public bool PerCommandCooldownsEnabled
        {
            get => CommandHandler.PerCommandCooldownsEnabled;
            set
            {
                CommandHandler.PerCommandCooldownsEnabled = value;
                if (Plugin.Settings != null)
                    Plugin.Settings.PerCommandCooldownsEnabled = value;

                NotifyPropertyChanged(nameof(PerCommandCooldownsEnabled));
            }
        }

        [UIValue("faster_cd_seconds")]
        public float FasterCooldownSeconds
        {
            get => CommandHandler.FasterCooldownSeconds;
            set
            {
                float clamped = Mathf.Clamp(value, 0f, 300f);
                CommandHandler.FasterCooldownSeconds = clamped;
                if (Plugin.Settings != null)
                    Plugin.Settings.FasterCooldownSeconds = clamped;

                NotifyPropertyChanged(nameof(FasterCooldownSeconds));
            }
        }


        [UIValue("bomb_cd_seconds")]
        public float BombCooldownSeconds
        {
            get => CommandHandler.BombCooldownSeconds;
            set
            {
                float clamped = Mathf.Clamp(value, 0f, 300f);
                CommandHandler.BombCooldownSeconds = clamped;
                if (Plugin.Settings != null)
                    Plugin.Settings.BombCooldownSeconds = clamped;

                NotifyPropertyChanged(nameof(BombCooldownSeconds));
            }
        }


        [UIValue("bomb_command")]
        public string BombCommand
        {
            get
            {
                // Show with leading '!'
                string name = CommandHandler.BombCommandName;
                if (string.IsNullOrWhiteSpace(name))
                    name = "bomb";
                return "!" + name;
            }
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                    return;

                // Strip spaces and leading '!'
                string cleaned = value.Trim();
                if (cleaned.StartsWith("!"))
                    cleaned = cleaned.Substring(1);

                cleaned = cleaned.ToLowerInvariant();

                if (string.IsNullOrWhiteSpace(cleaned))
                    return;

                // Update runtime behavior
                CommandHandler.BombCommandName = cleaned;

                // Persist to config
                if (Plugin.Settings != null)
                    Plugin.Settings.BombCommandName = cleaned;

                NotifyPropertyChanged(nameof(BombCommand));
            }
        }

        protected override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
        {
            base.DidActivate(firstActivation, addedToHierarchy, screenSystemEnabling);

            if (firstActivation)
            {
                TwitchApiClient.OnSubscriberStatusChanged += HandleSubscriberStatusChanged;
                // Ensure font bundle is loaded before modal opens
                _ = FontBundleLoader.EnsureLoadedAsync();
                //StartCoroutine(PreInitializeModal());
            }

            UpdateBombVisualsButtonVisibility();
            UpdateRainbowVisualsButtonVisibility();
            UpdateGhostVisualsButtonVisibility();
            UpdateDisappearVisualsButtonVisibility();
            UpdateFlashbangVisualsButtonVisibility();
        }

        protected override void DidDeactivate(bool removedFromHierarchy, bool screenSystemDisabling)
        {
            base.DidDeactivate(removedFromHierarchy, screenSystemDisabling);

            if (removedFromHierarchy)
            {
                TwitchApiClient.OnSubscriberStatusChanged -= HandleSubscriberStatusChanged;
                CleanupWorldSpacePreview();
            }
        }

        private void HandleSubscriberStatusChanged()
        {
            // Ensure UI work runs on Unity’s main thread.
            UnityMainThreadTaskScheduler.Factory.StartNew(() =>
            {
                UpdateBombVisualsButtonVisibility();
                UpdateRainbowVisualsButtonVisibility();
                UpdateGhostVisualsButtonVisibility();
                UpdateDisappearVisualsButtonVisibility();
                UpdateFlashbangVisualsButtonVisibility();
                // Optional – if you ever bind `show_bomb_visuals_button` directly in BSML:
                // NotifyPropertyChanged(nameof(ShowBombVisualsButton));
            });
        }

        private void UpdateBombVisualsButtonVisibility()
        {
            if (_bombEditButton != null)
            {
                bool allowed = ShowBombVisualsButton;
                _bombEditButton.gameObject.SetActive(allowed);
            }
        }

        private void UpdateRainbowVisualsButtonVisibility()
        {
            if (_rainbowEditButton != null)
            {
                bool allowed = ShowBombVisualsButton; // Reuse same logic
                _rainbowEditButton.gameObject.SetActive(allowed);
            }
        }

        private void UpdateGhostVisualsButtonVisibility()
        {
            if (_ghostEditButton != null)
            {
                bool allowed = ShowBombVisualsButton;
                _ghostEditButton.gameObject.SetActive(allowed);
            }
        }

        private void UpdateDisappearVisualsButtonVisibility()
        {
            if (_disappearEditButton != null)
            {
                bool allowed = ShowBombVisualsButton;
                _disappearEditButton.gameObject.SetActive(allowed);
            }
        }

        private void UpdateFlashbangVisualsButtonVisibility()
        {
            if (_flashbangEditButton != null)
            {
                bool allowed = ShowBombVisualsButton;
                _flashbangEditButton.gameObject.SetActive(allowed);
            }
        }

        // ==================== BOMB VISUALS ====================

        [UIComponent("bomb-visuals-modal")]
        private ModalView _bombVisualsModal;

        [UIComponent("bomb-edit-button")]
        private UnityEngine.UI.Button _bombEditButton;


        [UIValue("show_bomb_visuals_button")]
        public bool ShowBombVisualsButton
        {
            get
            {
                // 1. Must be authenticated with your backend
                bool backendConnected = TwitchAuthManager.Instance.IsAuthenticated;

                // 2. Must be at least Tier1 supporter to your channel
                bool isSupporter =
                    SupporterState.CurrentTier != SupporterTier.None ||
                    (Plugin.Settings?.CachedSupporterTier ?? 0) > 0;

                return backendConnected && isSupporter;
            }
        }


        [UIAction("OnBombEditVisualsClicked")]
        private void OnBombEditVisualsClicked()
        {
            _ = OnBombEditVisualsClickedAsync();
        }

        private async System.Threading.Tasks.Task OnBombEditVisualsClickedAsync()
        {
            bool allowed = false;
            try
            {
                allowed = await TwitchApiClient.Instance.CheckVisualsPermissionAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn("Bomb visuals permission check failed: " + ex.Message);
                allowed = false;
            }

            if (!allowed)
            {
                Plugin.Log.Warn($"[SECURITY] Visuals access denied for user {TwitchAuthManager.Instance.BroadcasterId}. " +
                       $"Client tier: {SupporterState.CurrentTier}, Server rejected.");
                return;
            }

            // Show modal on Unity main thread - simple and direct
            await IPA.Utilities.Async.UnityMainThreadTaskScheduler.Factory.StartNew(() =>
            {
                ShowBombModal();
            });
        }

        private void ShowBombModal()
        {
            if (_bombVisualsModal == null)
            {
                Plugin.Log.Warn("Bomb visuals modal was null when trying to show it.");
                return;
            }

            // Show modal immediately - BSML handles initialization
            _bombVisualsModal.Show(true);

            // Start coroutines after modal is shown
            StartCoroutine(RefreshBombFontDropdown());
            StartBombFontPreview();
        }


        [UIComponent("bomb-font-preview")]
        private TMP_Text _bombFontPreview;

        private Coroutine _bombFontPreviewCoroutine;

        [UIComponent("bomb-font-dropdown")]
        private DropDownListSetting _bombFontDropdown;


        [UIValue("bomb_text_height")]
        public float BombTextHeight
        {
            get => Plugin.Settings?.BombTextHeight ?? 1.0f;
            set
            {
                float clamped = Mathf.Clamp(value, 0.5f, 5f);
                if (Plugin.Settings != null)
                    Plugin.Settings.BombTextHeight = clamped;
                NotifyPropertyChanged(nameof(BombTextHeight));
            }
        }

        [UIValue("bomb_text_width")]
        public float BombTextWidth
        {
            get => Plugin.Settings?.BombTextWidth ?? 1.0f;
            set
            {
                float clamped = Mathf.Clamp(value, 0.5f, 5f);
                if (Plugin.Settings != null)
                    Plugin.Settings.BombTextWidth = clamped;
                NotifyPropertyChanged(nameof(BombTextWidth));
            }
        }

        [UIValue("bomb_spawn_distance")]
        public float BombSpawnDistance
        {
            get => Plugin.Settings?.BombSpawnDistance ?? 10.0f;
            set
            {
                float clamped = Mathf.Clamp(value, 2f, 20f);
                if (Plugin.Settings != null)
                    Plugin.Settings.BombSpawnDistance = clamped;
                NotifyPropertyChanged(nameof(BombSpawnDistance));
            }
        }


        // *** Font Selection Logic ***

        [UIValue("bomb_font_options")]
        public List<object> BombFontOptions
        {
            get
            {
                // Pull dynamic options from FontBundleLoader
                // BSML expects List<object> for dropdown choices
                var options = FontBundleLoader.GetBombFontOptions();
                return options.Cast<object>().ToList();
            }
        }

        [UIValue("bomb_font_selected")]
        public string BombFontSelected
        {
            get => FontBundleLoader.GetSelectedBombFontOption();
            set
            {
                StopBombFontPreview();

                FontBundleLoader.SetSelectedBombFontOption(value);
                NotifyPropertyChanged(nameof(BombFontSelected));
                // Stop current preview animation
                
                StartCoroutine(ApplyFontChangeDelayed());
                
            }
        }

        private IEnumerator ApplyFontChangeDelayed()
        {
            // Disable the component to prevent layout calculations during transition
            if (_bombFontPreview != null)
            {
                _bombFontPreview.enabled = false;
            }

            // Wait 2 frames to ensure FontBundleLoader updates BombUsernameFont
            yield return null;
            yield return null;

            // Now apply the new font fresh
            ApplyBombFontPreviewStatic();

            // Re-enable the component
            if (_bombFontPreview != null)
            {
                _bombFontPreview.enabled = true;
            }

            // Restart the color animation
            if (_bombFontPreview != null && _bombFontPreview.gameObject.activeInHierarchy)
            {
                _bombFontPreviewCoroutine = StartCoroutine(BombFontPreviewRoutine());
            }
        }

        // *** Gradient Color Logic ***

        [UIValue("bomb_gradient_start")]
        public Color BombGradientStart
        {
            get => Plugin.Settings?.BombGradientStart ?? Color.yellow;
            set
            {
                if (Plugin.Settings != null) Plugin.Settings.BombGradientStart = value;
                NotifyPropertyChanged(nameof(BombGradientStart));
            }
        }

        [UIValue("bomb_gradient_end")]
        public Color BombGradientEnd
        {
            get => Plugin.Settings?.BombGradientEnd ?? Color.red;
            set
            {
                if (Plugin.Settings != null) Plugin.Settings.BombGradientEnd = value;
                NotifyPropertyChanged(nameof(BombGradientEnd));
            }
        }

        /*

        [UIValue("bomb_fireworks_texture_options")]
        public List<object> BombFireworksTextureOptions
        {
            get
            {
                var options = FireworksExplosionPool.GetAvailableTextureTypes();
                if (options == null || options.Count == 0)
                    return new List<object> { "Sparkle" }; // Fallback
                return options.Cast<object>().ToList();
            }
        }

        [UIValue("bomb_fireworks_texture")]
        public string BombFireworksTexture
        {
            get => Plugin.Settings?.BombFireworksTextureType ?? "Sparkle";
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                    return;

                FireworksExplosionPool.SetTextureType(value);

                if (Plugin.Settings != null)
                    Plugin.Settings.BombFireworksTextureType = value;

                NotifyPropertyChanged(nameof(BombFireworksTexture));
            }
        }

        */

        // ==================== RAINBOW VISUALS ====================
        [UIComponent("rainbow-edit-button")]
        private UnityEngine.UI.Button _rainbowEditButton;

        [UIComponent("rainbow-visuals-modal")]
        private ModalView _rainbowVisualsModal;

        [UIComponent("rainbow-note-preview-container")]
        private Transform _rainbowNotePreviewContainer;

        private GameObject _previewParent;
        private GameObject _previewLeftNote;
        private GameObject _previewRightNote;
        private Coroutine _rainbowPreviewCoroutine;
        private Renderer _leftNoteRenderer;
        private Renderer _rightNoteRenderer;
        private bool _isPreviewInitialized = false;

        [UIValue("rainbow_gradient_fade_enabled")]
        public bool RainbowGradientFadeEnabled
        {
            get => Plugin.Settings?.RainbowGradientFadeEnabled ?? true;
            set
            {
                if (Plugin.Settings != null)
                    Plugin.Settings.RainbowGradientFadeEnabled = value;
                NotifyPropertyChanged(nameof(RainbowGradientFadeEnabled));
            }
        }

        [UIValue("rainbow_gradient_cycle_speed")]
        public float RainbowGradientCycleSpeed
        {
            get => Plugin.Settings?.RainbowCycleSpeed ?? 0.1f;
            set
            {
                float clamped = Mathf.Clamp(value, 0.01f, 5f);
                if (Plugin.Settings != null)
                    Plugin.Settings.RainbowCycleSpeed = clamped;

                Gameplay.RainbowManager.RainbowCycleSpeed = clamped;
                NotifyPropertyChanged(nameof(RainbowGradientCycleSpeed));
            }
        }

        [UIAction("OnRainbowEditVisualsClicked")]
        private void OnRainbowEditVisualsClicked()
        {
            if (_rainbowVisualsModal != null)
            {
                _rainbowVisualsModal.Show(true);
                StartCoroutine(DelayedStartPreview());
            }
            else
            {
                Plugin.Log.Warn("Rainbow visuals modal was null when trying to show it.");
            }
        }

        private IEnumerator DelayedStartPreview()
        {
            yield return new WaitForSeconds(0.1f);
            StartRainbowNotePreview();
        }

        [UIAction("CloseRainbowVisuals")]
        private void CloseRainbowVisuals()
        {
            if (_rainbowVisualsModal != null)
                _rainbowVisualsModal.Hide(true);

            StopRainbowNotePreview();
        }

        private void StartRainbowNotePreview()
        {
            StopRainbowNotePreview();

            LogUtils.Debug(() => "Starting rainbow note preview - initializing world space preview");

            if (!_isPreviewInitialized)
            {
                StartCoroutine(InitializeWorldSpacePreview());
            }
            else
            {
                if (_previewLeftNote != null) _previewLeftNote.SetActive(true);
                if (_previewRightNote != null) _previewRightNote.SetActive(true);

                // Update position in case modal moved
                // UpdatePreviewPosition();

                _rainbowPreviewCoroutine = StartCoroutine(RainbowPreviewRoutine());
            }
        }

        private IEnumerator InitializeWorldSpacePreview()
        {
            LogUtils.Debug(() => "Loading gameplay scene to extract note prefabs...");

            // Find the menu transitions helper to get scene info
            var menuTransitionsHelper = Resources.FindObjectsOfTypeAll<MenuTransitionsHelper>().FirstOrDefault();
            if (menuTransitionsHelper == null)
            {
                Plugin.Log.Error("MenuTransitionsHelper not found!");
                yield break;
            }

            // Use reflection to access the private _standardLevelScenesTransitionSetupData field
            var setupDataField = typeof(MenuTransitionsHelper).GetField("_standardLevelScenesTransitionSetupData",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (setupDataField == null)
            {
                Plugin.Log.Error("Could not find _standardLevelScenesTransitionSetupData field!");
                yield break;
            }

            var standardLevelScenesTransitionSetupData = setupDataField.GetValue(menuTransitionsHelper) as StandardLevelScenesTransitionSetupDataSO;
            if (standardLevelScenesTransitionSetupData == null)
            {
                Plugin.Log.Error("standardLevelScenesTransitionSetupData was null!");
                yield break;
            }

            // Use reflection to access the private standardGameplaySceneInfo field
            var gameplaySceneInfoField = typeof(StandardLevelScenesTransitionSetupDataSO).GetField("_standardGameplaySceneInfo",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (gameplaySceneInfoField == null)
            {
                Plugin.Log.Error("Could not find _standardGameplaySceneInfo field!");
                yield break;
            }

            var gameplaySceneInfo = gameplaySceneInfoField.GetValue(standardLevelScenesTransitionSetupData) as SceneInfo;
            if (gameplaySceneInfo == null)
            {
                Plugin.Log.Error("gameplaySceneInfo was null!");
                yield break;
            }

            // Use reflection to access the private gameCoreSceneInfo field
            var gameCoreSceneInfoField = typeof(StandardLevelScenesTransitionSetupDataSO).GetField("_gameCoreSceneInfo",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (gameCoreSceneInfoField == null)
            {
                Plugin.Log.Error("Could not find _gameCoreSceneInfo field!");
                yield break;
            }

            var gameCoreSceneInfo = gameCoreSceneInfoField.GetValue(standardLevelScenesTransitionSetupData) as SceneInfo;
            if (gameCoreSceneInfo == null)
            {
                Plugin.Log.Error("gameCoreSceneInfo was null!");
                yield break;
            }

            // Load GameCore scene first
            var gameCoreLoad = SceneManager.LoadSceneAsync(gameCoreSceneInfo.sceneName, LoadSceneMode.Additive);
            yield return gameCoreLoad;

            // Load StandardGameplay scene
            var gameplayLoad = SceneManager.LoadSceneAsync(gameplaySceneInfo.sceneName, LoadSceneMode.Additive);
            yield return gameplayLoad;

            LogUtils.Debug(() => "Gameplay scenes loaded, extracting note prefabs...");

            // Find the BeatmapObjectsInstaller to get note prefabs
            var beatmapObjectsInstaller = Resources.FindObjectsOfTypeAll<BeatmapObjectsInstaller>().FirstOrDefault();
            if (beatmapObjectsInstaller == null)
            {
                Plugin.Log.Error("BeatmapObjectsInstaller not found!");
                yield break;
            }

            // Use reflection to find the note prefab field (field name may have changed)
            var prefabField = typeof(BeatmapObjectsInstaller).GetFields(
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .FirstOrDefault(f => f.FieldType == typeof(GameNoteController) && f.Name.Contains("normal"));

            if (prefabField == null)
            {
                Plugin.Log.Error("Could not find note prefab field in BeatmapObjectsInstaller!");

                // Log all available fields for debugging
                var allFields = typeof(BeatmapObjectsInstaller).GetFields(
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                LogUtils.Debug(() => $"Available fields in BeatmapObjectsInstaller:");
                foreach (var field in allFields)
                {
                    LogUtils.Debug(() => $"  - {field.Name} : {field.FieldType.Name}");
                }

                yield break;
            }

            var normalBasicNotePrefab = prefabField.GetValue(beatmapObjectsInstaller) as GameNoteController;
            if (normalBasicNotePrefab == null)
            {
                Plugin.Log.Error("Note prefab was null!");
                yield break;
            }

            // Get the note prefab - the visual mesh
            var noteTemplate = GameObject.Instantiate(normalBasicNotePrefab.transform.GetChild(0).gameObject);
            noteTemplate.SetActive(false);
            GameObject.DontDestroyOnLoad(noteTemplate);
            LogUtils.Debug(() => "Note template extracted, creating preview parent...");

            // === FIXED: Create a standalone world-space parent ===
            _previewParent = new GameObject("RainbowNotePreviewParent");

            // Position it in world space in front of the camera
            _previewParent.transform.position = new Vector3(1.7f, 1.5f, 1.5f);
            _previewParent.transform.rotation = Quaternion.Euler(0f, 0f, 0f);
            
            // Mark as root object to persist across scenes
            GameObject.DontDestroyOnLoad(_previewParent);

            // Create the preview notes
            _previewLeftNote = GameObject.Instantiate(noteTemplate);
            _previewRightNote = GameObject.Instantiate(noteTemplate);

            _previewLeftNote.transform.SetParent(_previewParent.transform, false);
            _previewRightNote.transform.SetParent(_previewParent.transform, false);

            _previewLeftNote.name = "RainbowPreviewLeft";
            _previewRightNote.name = "RainbowPreviewRight";

            // Position them side by side
            _previewLeftNote.transform.localPosition = new Vector3(0.3f, 0f, 0.4f);
            _previewRightNote.transform.localPosition = new Vector3(-0.1f, 0f, 0.7f);

            // Rotate to face camera
            _previewLeftNote.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            _previewRightNote.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            // Scale them down
            _previewLeftNote.transform.localScale = Vector3.one * 0.4f;
            _previewRightNote.transform.localScale = Vector3.one * 0.4f;

            // Get player colors
            var playerData = Resources.FindObjectsOfTypeAll<PlayerDataModel>().First().playerData;
            var colorScheme = playerData.colorSchemesSettings.overrideDefaultColors
                ? playerData.colorSchemesSettings.GetSelectedColorScheme()
                : null;

            Color leftColor = colorScheme?.saberAColor ?? new Color(0.659f, 0.125f, 0.125f);
            Color rightColor = colorScheme?.saberBColor ?? new Color(0.125f, 0.392f, 0.659f);

            // Patch the notes - apply materials and setup
            PatchNoteForPreview(_previewLeftNote, leftColor);
            PatchNoteForPreview(_previewRightNote, rightColor);

            // Get renderers for color updates
            _leftNoteRenderer = _previewLeftNote.GetComponent<Renderer>();
            _rightNoteRenderer = _previewRightNote.GetComponent<Renderer>();

            // Activate and show
            _previewLeftNote.SetActive(true);
            _previewRightNote.SetActive(true);

            LogUtils.Debug(() => "Preview notes created successfully!");

            // Unload the gameplay scenes
            SceneManager.UnloadSceneAsync(gameplaySceneInfo.sceneName);
            SceneManager.UnloadSceneAsync(gameCoreSceneInfo.sceneName);

            GameObject.Destroy(noteTemplate);

            _isPreviewInitialized = true;

            // Start the rainbow animation
            _rainbowPreviewCoroutine = StartCoroutine(RainbowPreviewRoutine());
        }



        // Helper to set layer recursively (needed for UI rendering)
        private void SetLayerRecursively(GameObject obj, int layer)
        {
            obj.layer = layer;
            foreach (Transform child in obj.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }

        // Update position when modal moves (called when reopening preview)
        /*
        private void UpdatePreviewPosition()
        {
            if (_previewParent != null && _rainbowNotePreviewContainer != null)
            {
                _previewParent.transform.position = _rainbowNotePreviewContainer.position +
                                                    _rainbowNotePreviewContainer.forward * 0.4f;
                _previewParent.transform.rotation = _rainbowNotePreviewContainer.rotation;
            }
        }
        */

        private void PatchNoteForPreview(GameObject noteObject, Color baseColor)
        {
            noteObject.SetActive(true);
            foreach (Transform child in noteObject.transform)
            {
                child.gameObject.SetActive(true);
            }

            var renderers = noteObject.GetComponentsInChildren<MeshRenderer>();
            foreach (var renderer in renderers)
            {
                renderer.receiveShadows = false;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            var materialControllers = noteObject.GetComponentsInChildren<MaterialPropertyBlockController>();
            foreach (var controller in materialControllers)
            {
                controller.materialPropertyBlock.SetColor(Shader.PropertyToID("_Color"), baseColor);
                controller.materialPropertyBlock.SetFloat(Shader.PropertyToID("_EnableRimDim"), 0f);
                controller.materialPropertyBlock.SetFloat(Shader.PropertyToID("_EnableFog"), 0f);
                controller.ApplyChanges();
            }

            var arrow = noteObject.transform.Find("NoteArrow");
            if (arrow != null) arrow.gameObject.SetActive(true);

            var arrowGlow = noteObject.transform.Find("NoteArrowGlow");
            if (arrowGlow != null) arrowGlow.gameObject.SetActive(true);

            var circle = noteObject.transform.Find("NoteCircleGlow");
            if (circle != null) circle.gameObject.SetActive(false);
        }

        private IEnumerator RainbowPreviewRoutine()
        {
            LogUtils.Debug(() => "Rainbow preview routine started (world space)");

            int frameCount = 0;
            while (_previewLeftNote != null && _previewRightNote != null &&
                   _previewLeftNote.activeInHierarchy && _previewRightNote.activeInHierarchy)
            {
                frameCount++;

                if (frameCount % 60 == 0)
                {
                    LogUtils.Debug(() => $"Rainbow preview running - Frame {frameCount}");
                }

                // Only update position every 10 frames (optimization)

                /*
                if (frameCount % 10 == 0 && _rainbowNotePreviewContainer != null)
                {
                    _previewParent.transform.position = _rainbowNotePreviewContainer.position +
                                                        _rainbowNotePreviewContainer.forward * 0.4f;
                    _previewParent.transform.rotation = _rainbowNotePreviewContainer.rotation;
                }
                */

                float cycleProgress = (Time.unscaledTime * RainbowGradientCycleSpeed) % 1f;
                float leftHue = cycleProgress;
                float rightHue = (cycleProgress + 0.5f) % 1f;

                Color leftRainbowColor = Color.HSVToRGB(leftHue, 0.85f, 1f);
                Color rightRainbowColor = Color.HSVToRGB(rightHue, 0.85f, 1f);

                UpdateNoteColor(_previewLeftNote, leftRainbowColor);
                UpdateNoteColor(_previewRightNote, rightRainbowColor);

                _previewLeftNote.transform.Rotate(Vector3.up, 30f * Time.unscaledDeltaTime);
                _previewRightNote.transform.Rotate(Vector3.up, 30f * Time.unscaledDeltaTime);

                yield return null;
            }

            LogUtils.Debug(() => "Rainbow preview routine ended");
            _rainbowPreviewCoroutine = null;
        }

        private void UpdateNoteColor(GameObject noteObject, Color newColor)
        {
            var materialControllers = noteObject.GetComponentsInChildren<MaterialPropertyBlockController>();
            foreach (var controller in materialControllers)
            {
                controller.materialPropertyBlock.SetColor(Shader.PropertyToID("_Color"), newColor);
                controller.materialPropertyBlock.SetColor(Shader.PropertyToID("_EmissionColor"), newColor * 1.5f);
                controller.ApplyChanges();
            }
        }

        private void StopRainbowNotePreview()
        {
            if (_rainbowPreviewCoroutine != null)
            {
                StopCoroutine(_rainbowPreviewCoroutine);
                _rainbowPreviewCoroutine = null;
            }

            if (_previewLeftNote != null)
            {
                _previewLeftNote.SetActive(false);
            }

            if (_previewRightNote != null)
            {
                _previewRightNote.SetActive(false);
            }
        }

        private void CleanupWorldSpacePreview()
        {
            if (_previewLeftNote != null)
            {
                GameObject.Destroy(_previewLeftNote);
                _previewLeftNote = null;
            }

            if (_previewRightNote != null)
            {
                GameObject.Destroy(_previewRightNote);
                _previewRightNote = null;
            }

            if (_previewParent != null)
            {
                GameObject.Destroy(_previewParent);
                _previewParent = null;
            }

            _isPreviewInitialized = false;
        }



        // ==================== GHOST NOTES VISUALS ====================

        [UIComponent("ghost-edit-button")]
        private UnityEngine.UI.Button _ghostEditButton;

        [UIComponent("ghost-visuals-modal")]
        private ModalView _ghostVisualsModal;

        [UIAction("OnGhostEditVisualsClicked")]
        private void OnGhostEditVisualsClicked()
        {
            if (_ghostVisualsModal != null)
            {
                _ghostVisualsModal.Show(true);
            }
            else
            {
                Plugin.Log.Warn("Ghost visuals modal was null when trying to show it.");
            }
        }

        [UIAction("CloseGhostVisuals")]
        private void CloseGhostVisuals()
        {
            if (_ghostVisualsModal != null)
                _ghostVisualsModal.Hide(true);
        }

        // ==================== DISAPPEARING ARROWS VISUALS ====================

        [UIComponent("disappear-edit-button")]
        private UnityEngine.UI.Button _disappearEditButton;

        [UIComponent("disappear-visuals-modal")]
        private ModalView _disappearVisualsModal;

        [UIAction("OnDisappearEditVisualsClicked")]
        private void OnDisappearEditVisualsClicked()
        {
            if (_disappearVisualsModal != null)
            {
                _disappearVisualsModal.Show(true);
            }
            else
            {
                Plugin.Log.Warn("Disappear visuals modal was null when trying to show it.");
            }
        }

        [UIAction("CloseDisappearVisuals")]
        private void CloseDisappearVisuals()
        {
            if (_disappearVisualsModal != null)
                _disappearVisualsModal.Hide(true);
        }

        // ==================== FLASHBANG VISUALS ====================

        [UIComponent("flashbang-edit-button")]
        private UnityEngine.UI.Button _flashbangEditButton;

        [UIComponent("flashbang-visuals-modal")]
        private ModalView _flashbangVisualsModal;

        [UIValue("flashbang_brightness_multiplier")]
        public int FlashbangBrightnessMultiplier
        {
            get => Plugin.Settings?.FlashbangBrightnessMultiplier ?? 90;
            set
            {
                int clamped = Mathf.Clamp(value, 1, 200);
                if (Plugin.Settings != null)
                    Plugin.Settings.FlashbangBrightnessMultiplier = clamped;
                NotifyPropertyChanged(nameof(FlashbangBrightnessMultiplier));
            }
        }

        [UIAction("OnFlashbangEditVisualsClicked")]
        private void OnFlashbangEditVisualsClicked()
        {
            if (_flashbangVisualsModal != null)
            {
                _flashbangVisualsModal.Show(true);
            }
            else
            {
                Plugin.Log.Warn("Flashbang visuals modal was null when trying to show it.");
            }
        }

        [UIAction("CloseFlashbangVisuals")]
        private void CloseFlashbangVisuals()
        {
            if (_flashbangVisualsModal != null)
                _flashbangVisualsModal.Hide(true);
        }



        private IEnumerator RefreshBombFontDropdown()
        {
            
            // If you don’t care about hot-swapping bundles during runtime, you can use EnsureLoadedAsync() instead.
            var task = FontBundleLoader.EnsureLoadedAsync();
            while (!task.IsCompleted) yield return null;

            // Update BSML-bound properties
            NotifyPropertyChanged(nameof(BombFontOptions));
            NotifyPropertyChanged(nameof(BombFontSelected));

            // Force the dropdown to rebuild its UI list
            if (_bombFontDropdown != null)
            {
                _bombFontDropdown.Values = BombFontOptions;
                _bombFontDropdown.UpdateChoices();
                _bombFontDropdown.ReceiveValue();
            }

            ApplyBombFontPreviewStatic();
        }

        private void StartBombFontPreview()
        {
            StopBombFontPreview();

            if (_bombFontPreview == null)
                return;

            ApplyBombFontPreviewStatic(); // apply font + scale immediately
            _bombFontPreviewCoroutine = StartCoroutine(BombFontPreviewRoutine());
        }

        private void StopBombFontPreview()
        {
            if (_bombFontPreviewCoroutine != null)
            {
                StopCoroutine(_bombFontPreviewCoroutine);
                _bombFontPreviewCoroutine = null;
            }
        }

        private void ApplyBombFontPreviewStatic()
        {
            if (_bombFontPreview == null)
                return;

            // Get the newly selected font
            var font = BeatSurgeon.Gameplay.FontBundleLoader.BombUsernameFont;
            if (font == null)
            {
                Plugin.Log.Warn("BombUsernameFont is null in ApplyBombFontPreviewStatic");
                return;
            }

            // *** VERSION-SAFE: Use reflection helper instead of direct .material access ***
            // NEW:
            Material fontMaterial = BeatSurgeon.Gameplay.FontBundleLoader.GetOrCreateFontMaterial(font);
            if (fontMaterial == null)
            {
                Plugin.Log.Warn($"Could not get material for font '{font.name}' in ApplyBombFontPreviewStatic");
                return;
            }

            // Apply font and material
            _bombFontPreview.font = font;
            _bombFontPreview.fontSharedMaterial = fontMaterial;  //Use the reflection-retrieved material

            // Set text (do this AFTER setting font)
            _bombFontPreview.text = "PreviewUsername";

            // Mimic gameplay "shape" controls
            _bombFontPreview.rectTransform.localScale = new Vector3(BombTextWidth, BombTextHeight, 1f);

            // Optional styling similar to your in-game text
            _bombFontPreview.outlineWidth = 0.2f;
            _bombFontPreview.outlineColor = Color.black;

            // *** CRITICAL: Force TextMeshPro to regenerate mesh with new font ***
            _bombFontPreview.SetAllDirty();
            _bombFontPreview.ForceMeshUpdate();
        }



        private IEnumerator BombFontPreviewRoutine()
        {
            // If you want it to feel like your in-game 2s flight, keep this at 2.0
            const float cycleSeconds = 2.0f;

            while (_bombFontPreview != null && _bombFontPreview.gameObject.activeInHierarchy)
            {
                // 0..1..0..1...
                float t = Mathf.PingPong(Time.unscaledTime / cycleSeconds, 1f);

                // use your existing settings as the gradient endpoints
                Color c = Color.Lerp(BombGradientStart, BombGradientEnd, t);
                c.a = 1f;

                _bombFontPreview.color = c;

                // If user changes options while it’s open, keep it in sync.
                // (Cheap enough to do every frame)
                //ApplyBombFontPreviewStatic();

                yield return null;
            }

            _bombFontPreviewCoroutine = null;
        }



        [UIAction("CloseBombVisuals")]
        private void CloseBombVisuals()
        {
            if (_bombVisualsModal != null)
                _bombVisualsModal.Hide(true);

            StopBombFontPreview();
        }





        [UIValue("rainbow_cd_seconds")]
        public float RainbowCooldownSeconds
        {
            get => CommandHandler.RainbowCooldownSeconds;
            set
            {
                float clamped = Mathf.Clamp(value, 0f, 300f);
                CommandHandler.RainbowCooldownSeconds = clamped;
                if (Plugin.Settings != null)
                    Plugin.Settings.RainbowCooldownSeconds = clamped;

                NotifyPropertyChanged(nameof(RainbowCooldownSeconds));
            }
        }

        // Text field backing the string-setting
        [UIValue("rainbow_command")]
        public string RainbowCommand
        {
            get => "!rainbow";          // default shown in the text box
            set
            {
                // If you want it editable, validate and store somewhere:
                // e.g. in Plugin.Settings.RainbowCommand
                //if (string.IsNullOrWhiteSpace(value))
                //    return;

                // Example: keep it without leading '!' and force lowercase
                //string cleaned = value.Trim();

                // Store if you have a setting:
                // Plugin.Settings.RainbowCommand = cleaned;

                NotifyPropertyChanged(nameof(RainbowCommand));
            }
        }

        // Button click handler


        [UIValue("ghost_cd_seconds")]
        public float GhostCooldownSeconds
        {
            get => CommandHandler.GhostCooldownSeconds;
            set
            {
                float clamped = Mathf.Clamp(value, 0f, 300f);
                CommandHandler.GhostCooldownSeconds = clamped;
                if (Plugin.Settings != null)
                    Plugin.Settings.GhostCooldownSeconds = clamped;

                NotifyPropertyChanged(nameof(GhostCooldownSeconds));
            }
        }

        [UIValue("disappear_cd_seconds")]
        public float DisappearCooldownSeconds
        {
            get => CommandHandler.DisappearCooldownSeconds;
            set
            {
                float clamped = Mathf.Clamp(value, 0f, 300f);
                CommandHandler.DisappearCooldownSeconds = clamped;
                if (Plugin.Settings != null)
                    Plugin.Settings.DisappearCooldownSeconds = clamped;

                NotifyPropertyChanged(nameof(DisappearCooldownSeconds));
            }
        }

        

        [UIValue("superfast_cd_seconds")]
        public float SuperFastCooldownSeconds
        {
            get => CommandHandler.SuperFastCooldownSeconds;
            set
            {
                float clamped = Mathf.Clamp(value, 0f, 300f);
                CommandHandler.SuperFastCooldownSeconds = clamped;
                if (Plugin.Settings != null)
                    Plugin.Settings.SuperFastCooldownSeconds = clamped;

                NotifyPropertyChanged(nameof(SuperFastCooldownSeconds));
            }
        }

        [UIValue("slower_cd_seconds")]
        public float SlowerCooldownSeconds
        {
            get => CommandHandler.SlowerCooldownSeconds;
            set
            {
                float clamped = Mathf.Clamp(value, 0f, 300f);
                CommandHandler.SlowerCooldownSeconds = clamped;
                if (Plugin.Settings != null)
                    Plugin.Settings.SlowerCooldownSeconds = clamped;

                NotifyPropertyChanged(nameof(SlowerCooldownSeconds));
            }
        }

        [UIValue("speed_exclusive_enabled")]
        public bool SpeedExclusiveEnabled
        {
            get => CommandHandler.SpeedExclusiveEnabled;
            set
            {
                CommandHandler.SpeedExclusiveEnabled = value;
                if (Plugin.Settings != null)
                    Plugin.Settings.SpeedExclusiveEnabled = value;

                NotifyPropertyChanged(nameof(SpeedExclusiveEnabled));
            }
        }

        [UIValue("flashbang_cd_seconds")]
        public int FlashbangCooldownSeconds
        {
            get => (int)CommandHandler.FlashbangCooldownSeconds;
            set
            {
                int clamped = Mathf.Clamp(value, 0, 300);
                CommandHandler.FlashbangCooldownSeconds = clamped;
                if (Plugin.Settings != null)
                    Plugin.Settings.FlashbangCooldownSeconds = clamped;

                NotifyPropertyChanged(nameof(FlashbangCooldownSeconds));
            }
        }



    }
}
