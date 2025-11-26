using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.ViewControllers;
using SaberSurgeon.Chat;
using UnityEngine;

namespace SaberSurgeon.UI.Controllers
{
    [ViewDefinition("SaberSurgeon.UI.Views.SaberSurgeonSettings.bsml")]
    [HotReload(RelativePathToLayout = @"..\Views\SaberSurgeonSettings.bsml")]
    public class SaberSurgeonViewController : BSMLAutomaticViewController
    {

        [UIValue("playTime")]
        public float PlayTime
        {
            get => _playTime;
            set
            {
                _playTime = value;
                NotifyPropertyChanged(nameof(PlayTime));
                Plugin.Log.Info($"Slider changed → PlayTime = {_playTime} minutes");
            }
        }

        private float _playTime = 60f; // default mid-range


        [UIAction("OnStartPlayPressed")]
        private void OnStartPlayPressed()
        {
            Plugin.Log.Info("SaberSurgeon: Start/Play button pressed!");
            Plugin.Log.Info($"Timer set to: {PlayTime} minutes");

            // Start the endless mode gameplay
            var gameplayManager = SaberSurgeon.Gameplay.GameplayManager.GetInstance();

            if (gameplayManager.IsPlaying())
            {
                // Stop if already playing
                gameplayManager.StopEndlessMode();
                Plugin.Log.Info("SaberSurgeon: Stopped endless mode");
                ChatManager.GetInstance().SendChatMessage("Saber Surgeon session ended!");
            }
            else
            {
                // Start new session
                gameplayManager.StartEndlessMode(PlayTime);
                Plugin.Log.Info($"SaberSurgeon: Started endless mode for {PlayTime} minutes");
                ChatManager.GetInstance().SendChatMessage($"Saber Surgeon started! Playing for {PlayTime} minutes. Request songs with !bsr <code>");
            }
        }
    }
}
