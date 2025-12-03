using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.ViewControllers;
using SaberSurgeon.Chat;
using UnityEngine;
using UnityEngine.UI;
using System.Reflection;
using TMPro;
using System.IO;


namespace SaberSurgeon.UI.Controllers
{
    [ViewDefinition("SaberSurgeon.UI.Views.SaberSurgeonSettings.bsml")]
    [HotReload(RelativePathToLayout = @"..\Views\SaberSurgeonSettings.bsml")]
    public class SaberSurgeonViewController : BSMLAutomaticViewController
    {

        private static Sprite LoadEmbeddedSprite(string resourcePath)
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using (var stream = asm.GetManifestResourceStream(resourcePath))
                {
                    if (stream == null)
                    {
                        Plugin.Log.Error($"[SaberSurgeon] Failed to find embedded resource '{resourcePath}'");
                        return null;
                    }

                    var bytes = new byte[stream.Length];
                    _ = stream.Read(bytes, 0, bytes.Length);

                    var tex = new Texture2D(2, 2, TextureFormat.ARGB32, false);
                    if (!tex.LoadImage(bytes, markNonReadable: false))
                    {
                        Plugin.Log.Error($"[SaberSurgeon] Failed to decode texture from '{resourcePath}'");
                        return null;
                    }

                    tex.wrapMode = TextureWrapMode.Clamp;
                    tex.filterMode = FilterMode.Bilinear;

                    var sprite = Sprite.Create(
                        tex,
                        new Rect(0, 0, tex.width, tex.height),
                        new Vector2(0.5f, 0.5f),
                        100f
                    );
                    sprite.name = "SaberSurgeonRainbowIcon";

                    return sprite;
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.Error($"[SaberSurgeon] Exception loading embedded sprite '{resourcePath}': {ex}");
                return null;
            }
        }


        // Loaded once from embedded resource
        private static readonly Sprite RainbowOffSprite =
            LoadEmbeddedSprite("SaberSurgeon.Assets.Rainbow.png");

        private static readonly Sprite RainbowOnSprite =
            LoadEmbeddedSprite("SaberSurgeon.Assets.RainbowGB.png");

        // DA icons (off = DA, on = DAGB)
        private static readonly Sprite DAOffSprite =
            LoadEmbeddedSprite("SaberSurgeon.Assets.DA.png");

        private static readonly Sprite DAOnSprite =
            LoadEmbeddedSprite("SaberSurgeon.Assets.DAGB.png");

        // Ghost icons (off = GhostNotes, on = GhostNotesGB)
        private static readonly Sprite GhostOffSprite =
            LoadEmbeddedSprite("SaberSurgeon.Assets.GhostNotes.png");

        private static readonly Sprite GhostOnSprite =
            LoadEmbeddedSprite("SaberSurgeon.Assets.GhostNotesGB.png");

        //Button Icons ((off = Bomb, on = BombGB)
        private static readonly Sprite BombOffSprite =
            LoadEmbeddedSprite("SaberSurgeon.Assets.Bomb.png");

        private static readonly Sprite BombOnSprite =
            LoadEmbeddedSprite("SaberSurgeon.Assets.BombGB.png");



        // === Play time slider ===

        [UIValue("playTime")]
        public float PlayTime
        {
            get => _playTime;
            set
            {
                _playTime = value;
                NotifyPropertyChanged(nameof(PlayTime));
                Plugin.Log.Info($"Slider changed → PlayTime = {_playTime} minutes ");
            }
        }

        private float _playTime = 60f;

        // === Rainbow enabled flag (backed by CommandHandler) ===

        [UIValue("rainbowenabled")]
        public bool RainbowEnabled
        {
            get => CommandHandler.RainbowEnabled;
            set
            {
                CommandHandler.RainbowEnabled = value;
                NotifyPropertyChanged(nameof(RainbowEnabled));
                UpdateRainbowButtonVisual();
            }
        }


        // Menu toggle bound to CommandHandler.DisappearingEnabled
        [UIValue("da_enabled")]
        public bool DisappearingEnabled
        {
            get => CommandHandler.DisappearEnabled;
            set
            {
                CommandHandler.DisappearEnabled = value;
                NotifyPropertyChanged(nameof(DisappearingEnabled));
                UpdateDAButtonVisual();
            }
        }

        [UIComponent("dabutton")]
        private Button daButton;

        [UIComponent("daicon")]
        private Image daIcon;

        private Image daButtonImage;





        [UIValue("ghost_enabled")]
        public bool GhostEnabled
        {
            get => CommandHandler.GhostEnabled;
            set
            {
                CommandHandler.GhostEnabled = value;
                NotifyPropertyChanged(nameof(GhostEnabled));
                UpdateGhostButtonVisual();
            }
        }

        [UIComponent("ghostbutton")]
        private Button ghostButton;

        [UIComponent("ghosticon")]
        private Image ghostIcon;

        private Image ghostButtonImage;



        [UIValue("bomb_enabled")]
        public bool BombEnabled
        {
            get => CommandHandler.BombEnabled;
            set
            {
                CommandHandler.BombEnabled = value;
                NotifyPropertyChanged(nameof(BombEnabled));
                UpdateBombButtonVisual();
            }
        }

        [UIValue("bomb_cd_enabled")]
        public bool BombCooldownEnabled
        {
            get => CommandHandler.BombCooldownEnabled;
            set
            {
                CommandHandler.BombCooldownEnabled = value;
                NotifyPropertyChanged(nameof(BombCooldownEnabled));
            }
        }


        [UIComponent("bombbutton")]
        private Button bombButton;

        [UIComponent("bombicon")]
        private Image bombIcon;
        private Image bombButtonImage;


        // === UI components from BSML ===
        [UIComponent("rainbowbutton")]
        private Button rainbowButton;

        [UIComponent("rainbowicon")]
        private Image rainbowIcon;

        private Image rainbowButtonImage;

        // Colors for OFF/ON states to mimic modifier highlight
        private readonly Color offColor = new Color(0.2f, 0.2f, 0.2f, 1f);
        private readonly Color onColor = new Color(0.18f, 0.7f, 1f, 1f);

        // === Cooldown bindings ===

        [UIValue("global_cd_enabled")]
        public bool GlobalCooldownEnabled
        {
            get => CommandHandler.GlobalCooldownEnabled;
            set
            {
                CommandHandler.GlobalCooldownEnabled = value;
                NotifyPropertyChanged(nameof(GlobalCooldownEnabled));
            }
        }

        [UIValue("global_cd_seconds")]
        public float GlobalCooldownSeconds
        {
            get => CommandHandler.GlobalCooldownSeconds;
            set
            {
                CommandHandler.GlobalCooldownSeconds = Mathf.Clamp(value, 0f, 300f);
                NotifyPropertyChanged(nameof(GlobalCooldownSeconds));
            }
        }

        [UIValue("rainbow_cd_enabled")]
        public bool RainbowCooldownEnabled
        {
            get => CommandHandler.RainbowCooldownEnabled;
            set
            {
                CommandHandler.RainbowCooldownEnabled = value;
                NotifyPropertyChanged(nameof(RainbowCooldownEnabled));
            }
        }

        [UIValue("rainbow_cd_seconds")]
        public float RainbowCooldownSeconds
        {
            get => CommandHandler.RainbowCooldownSeconds;
            set
            {
                CommandHandler.RainbowCooldownSeconds = Mathf.Clamp(value, 0f, 300f);
                NotifyPropertyChanged(nameof(RainbowCooldownSeconds));
            }
        }

        [UIValue("bomb_cd_seconds")]
        public float BombCooldownSeconds
        {
            get => CommandHandler.BombCooldownSeconds;
            set
            {
                CommandHandler.BombCooldownSeconds = Mathf.Clamp(value, 0f, 300f);
                NotifyPropertyChanged(nameof(BombCooldownSeconds));
            }
        }

        // === Lifecycle / visual updates ===


        protected override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
        {
            base.DidActivate(firstActivation, addedToHierarchy, screenSystemEnabling);

            if (firstActivation)
            {
                if (rainbowButton != null)
                {
                    // Clear built-in label – icon-only button
                    BeatSaberUI.SetButtonText(rainbowButton, string.Empty);
                    rainbowButtonImage = rainbowButton.GetComponent<Image>();
                }

                if (rainbowIcon != null)
                {
                    // Make the image a child of the button so it sits centered on it
                    //rainbowIcon.transform.SetParent(rainbowButton.transform, worldPositionStays: false);

                    var rt = rainbowIcon.rectTransform;
                    rt.anchorMin = new Vector2(0.5f, 0.5f);
                    rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = Vector2.zero;
                    rt.sizeDelta = new Vector2(12f, 12f); // tweak size to taste
                }
                if (daButton != null)
                {
                    BeatSaberUI.SetButtonText(daButton, string.Empty);
                    daButtonImage = daButton.GetComponent<Image>();
                }

                if (daIcon != null)
                {
                    //daIcon.transform.SetParent(daButton.transform, worldPositionStays: false);

                    var rt = daIcon.rectTransform;
                    rt.anchorMin = new Vector2(0.5f, 0.5f);
                    rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = Vector2.zero;
                    rt.sizeDelta = new Vector2(12f, 12f);
                }

                if (ghostButton != null)
                {
                    BeatSaberUI.SetButtonText(ghostButton, string.Empty);
                    ghostButtonImage = ghostButton.GetComponent<Image>();
                }

                if (ghostIcon != null)
                {
                    ghostIcon.transform.SetParent(ghostButton.transform, worldPositionStays: false);
                    var rt = ghostIcon.rectTransform;
                    rt.anchorMin = new Vector2(0.5f, 0.5f);
                    rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = Vector2.zero;
                    rt.sizeDelta = new Vector2(12f, 12f);
                }
                if (bombButton != null)
                {
                    BeatSaberUI.SetButtonText(bombButton, string.Empty);
                    bombButtonImage = bombButton.GetComponent<Image>();
                }

                if (bombIcon != null)
                {
                    var rt = bombIcon.rectTransform;
                    rt.anchorMin = new Vector2(0.5f, 0.5f);
                    rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = Vector2.zero;
                    rt.sizeDelta = new Vector2(12f, 12f);
                }

                
            }

            // Apply correct sprite & color for current state
            UpdateRainbowButtonVisual();
            UpdateGhostButtonVisual();
            UpdateBombButtonVisual();
            UpdateDAButtonVisual();
        }



        private void UpdateBombButtonVisual()
        {
            if (bombIcon != null)
            {
                var sprite = BombEnabled ? BombOnSprite : BombOffSprite;
                if (sprite != null)
                    bombIcon.sprite = sprite;
            }

            if (bombButtonImage != null)
                bombButtonImage.color = BombEnabled ? onColor : offColor;
        }

        [UIAction("OnBombButtonClicked")]
        private void OnBombButtonClicked()
        {
            BombEnabled = !BombEnabled;
            Plugin.Log.Info($"SaberSurgeon: Bomb command enabled = {BombEnabled}");
        }



        private void UpdateDAButtonVisual()
        {
            if (daIcon != null)
            {
                var sprite = DisappearingEnabled ? DAOnSprite : DAOffSprite;
                if (sprite != null)
                    daIcon.sprite = sprite;
            }

            if (daButtonImage != null)
                daButtonImage.color = DisappearingEnabled ? onColor : offColor;
        }

        [UIAction("OnDAButtonClicked")]
        private void OnDAButtonClicked()
        {
            DisappearingEnabled = !DisappearingEnabled;
            Plugin.Log.Info($"SaberSurgeon: Disappearing command enabled = {DisappearingEnabled}");
        }




        private void UpdateRainbowButtonVisual()
        {
            // Swap icon sprite based on enabled state
            if (rainbowIcon != null)
            {
                var sprite = RainbowEnabled ? RainbowOnSprite : RainbowOffSprite;
                if (sprite != null)
                    rainbowIcon.sprite = sprite;
            }

            // Optional: still change background color for extra feedback
            if (rainbowButtonImage != null)
                rainbowButtonImage.color = RainbowEnabled ? onColor : offColor;
        }



        private void UpdateGhostButtonVisual()
        {
            if (ghostIcon != null)
            {
                var sprite = GhostEnabled ? GhostOnSprite : GhostOffSprite;
                if (sprite != null)
                    ghostIcon.sprite = sprite;
            }

            if (ghostButtonImage != null)
                ghostButtonImage.color = GhostEnabled ? onColor : offColor;
        }

        [UIAction("OnGhostButtonClicked")]
        private void OnGhostButtonClicked()
        {
            GhostEnabled = !GhostEnabled;
            Plugin.Log.Info($"SaberSurgeon: Ghost command enabled = {GhostEnabled}");
        }



        // === Actions ===

        [UIAction("OnRainbowButtonClicked")]
        private void OnRainbowButtonClicked()
        {
            RainbowEnabled = !RainbowEnabled;
            Plugin.Log.Info($"SaberSurgeon: Rainbow command enabled = {RainbowEnabled}");
        }

        [UIAction("OnStartPlayPressed")]
        private void OnStartPlayPressed()
        {
            Plugin.Log.Info("SaberSurgeon: Start/Play button pressed!");
            Plugin.Log.Info($"Timer set to: {PlayTime} minutes");

            var gameplayManager = SaberSurgeon.Gameplay.GameplayManager.GetInstance();

            if (gameplayManager.IsPlaying())
            {
                gameplayManager.StopEndlessMode();
                Plugin.Log.Info("SaberSurgeon: Stopped endless mode");
                ChatManager.GetInstance().SendChatMessage("Saber Surgeon session ended!");
            }
            else
            {
                gameplayManager.StartEndlessMode(PlayTime);
                Plugin.Log.Info($"SaberSurgeon: Started endless mode for {PlayTime} minutes");
                ChatManager.GetInstance().SendChatMessage(
                    $"Saber Surgeon started! Playing for {PlayTime} minutes. Request songs with !bsr <code>");
            }
        }
    }
}
