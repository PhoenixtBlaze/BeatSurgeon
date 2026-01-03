using BeatSaberMarkupLanguage.Attributes;
using SaberSurgeon.Gameplay;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SaberSurgeon.UI.Settings
{
    internal sealed class PlayFirstSubmitLaterSettingsHost : INotifyPropertyChanged
    {
        private static PlayFirstSubmitLaterSettingsHost _instance;
        public static PlayFirstSubmitLaterSettingsHost Instance
        {
            get
            {
                if (_instance == null)
                    _instance = new PlayFirstSubmitLaterSettingsHost();
                return _instance;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void NotifyPropertyChanged([CallerMemberName] string propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private PlayFirstSubmitLaterSettingsHost()
        {
            SceneHelper.MpPlusInRoomChanged += _ =>
            {
                NotifyPropertyChanged(nameof(IsAutoPauseAvailable));
            };
        }



        [UIValue("enabled")]
        public bool Enabled
        {
            get => Plugin.Settings?.PlayFirstSubmitLaterEnabled ?? true;
            set
            {
                if (Plugin.Settings != null) Plugin.Settings.PlayFirstSubmitLaterEnabled = value;
                NotifyPropertyChanged();
                NotifyPropertyChanged(nameof(StatusText));
                NotifyPropertyChanged(nameof(IsAutoPauseAvailable)); // Update availability if master toggle changes
            }
        }

        [UIValue("scoreSubmissionEnabled")]
        public bool ScoreSubmissionEnabled
        {
            get => Plugin.Settings?.ScoreSubmissionEnabled ?? true;
            set
            {
                if (Plugin.Settings != null) Plugin.Settings.ScoreSubmissionEnabled = value;

                // Apply immediately, independent of other SaberSurgeon features:
                if (value) PlayFirstSubmitLaterManager.EnableSubmission();
                else PlayFirstSubmitLaterManager.DisableSubmission();

                NotifyPropertyChanged();
                NotifyPropertyChanged(nameof(StatusText));
            }
        }

        [UIValue("autoPauseOnMapEnd")]
        public bool AutoPauseOnMapEnd
        {
            get => Plugin.Settings?.AutoPauseOnMapEnd ?? true;
            set
            {
                if (Plugin.Settings != null) Plugin.Settings.AutoPauseOnMapEnd = value;
                NotifyPropertyChanged();
            }
        }

        // FIX: Check if we are in Multiplayer to disable the toggle visual
        [UIValue("isAutoPauseAvailable")]
        public bool IsAutoPauseAvailable
        {
            get
            {
                if (!Enabled) return false;

                // Native Multiplayer check
                if (BS_Utils.Plugin.LevelData.Mode == BS_Utils.Gameplay.Mode.Multiplayer) return false;

                // MP+ room check (NEW)
                if (SceneHelper.MpPlusInRoom) return false;

                return true;
            }
        }

        [UIValue("statusText")]
        public string StatusText
        {
            get
            {
                if (!(Plugin.Settings?.PlayFirstSubmitLaterEnabled ?? true))
                    return "<color=grey>Module disabled</color>";

                return (Plugin.Settings?.ScoreSubmissionEnabled ?? true)
                    ? "<color=green>Scores will submit</color>"
                    : "<color=orange>Scores will NOT submit</color>";
            }
        }
    }
}
