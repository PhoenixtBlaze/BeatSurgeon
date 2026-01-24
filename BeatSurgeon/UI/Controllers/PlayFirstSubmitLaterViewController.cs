using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.ViewControllers;
using BeatSurgeon.Gameplay;
using UnityEngine;

namespace BeatSurgeon.UI.Controllers
{
    /// <summary>
    /// Standalone ViewController for PlayFirst SubmitLater settings.
    /// Completely independent from other Saber Surgeon UI systems.
    /// </summary>
    [ViewDefinition("BeatSurgeon.UI.Views.PlayFirstSubmitLaterSettings.bsml")]
    public class PlayFirstSubmitLaterViewController : BSMLAutomaticViewController
    {
        [UIValue("playFirstSubmitLaterEnabled")]
        public bool PlayFirstSubmitLaterEnabled
        {
            get => Plugin.Settings?.PlayFirstSubmitLaterEnabled ?? true;
            set
            {
                if (Plugin.Settings != null)
                {
                    Plugin.Settings.PlayFirstSubmitLaterEnabled = value;
                    Plugin.Log.Info($"PlayFirstSubmitLater: Feature {(value ? "ENABLED" : "DISABLED")}");
                }
                NotifyPropertyChanged(nameof(PlayFirstSubmitLaterEnabled));
                NotifyPropertyChanged(nameof(SubmissionStatus));
            }
        }

        [UIValue("scoreSubmissionEnabled")]
        public bool ScoreSubmissionEnabled
        {
            get => Plugin.Settings?.ScoreSubmissionEnabled ?? true;
            set
            {
                if (Plugin.Settings != null)
                {
                    Plugin.Settings.ScoreSubmissionEnabled = value;

                    // Apply to manager
                    if (value)
                        PlayFirstSubmitLaterManager.EnableSubmission();
                    else
                        PlayFirstSubmitLaterManager.DisableSubmission();
                }
                NotifyPropertyChanged(nameof(ScoreSubmissionEnabled));
                NotifyPropertyChanged(nameof(SubmissionStatus));
            }
        }

        [UIValue("autoPauseOnMapEnd")]
        public bool AutoPauseOnMapEnd
        {
            get => Plugin.Settings?.AutoPauseOnMapEnd ?? true;
            set
            {
                if (Plugin.Settings != null)
                    Plugin.Settings.AutoPauseOnMapEnd = value;
                NotifyPropertyChanged(nameof(AutoPauseOnMapEnd));
            }
        }

        

        [UIValue("submissionStatus")]
        public string SubmissionStatus
        {
            get
            {
                if (!PlayFirstSubmitLaterManager.IsFeatureEnabled)
                    return "<color=gray>Feature disabled</color>";

                if (ScoreSubmissionEnabled)
                    return "<color=green>Scores WILL be submitted to leaderboards</color>";
                else
                    return "<color=orange>Scores WILL NOT be submitted</color>";
            }
        }
    }
}
