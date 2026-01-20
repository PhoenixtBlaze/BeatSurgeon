using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace BeatSurgeon.Gameplay
{
    /// <summary>
    /// QueueProcessor-equivalent "tick" that runs during gameplay.
    /// It does NOT switch beatmaps yet; it only decides when a switch should happen.
    /// BeatmapSwitcher + seek will subscribe to SwitchRequested later.
    /// </summary>
    internal class InLevelQueueProcessor : MonoBehaviour
    {
        // AudioTimeSyncController holds a private AudioSource we can use for clip length checks.
        private static readonly FieldInfo AudioSourceField =
            AccessTools.Field(typeof(AudioTimeSyncController), "_audioSource");

        private GameplayManager _gameplayManager;
        private AudioTimeSyncController _audioTimeSync;
        private AudioSource _audioSource;

        private bool _active;
        private bool _isExecutingSwitch;
        private bool _preloadTriggered;

        // If set, we arm "switchAt" once audio becomes available in the gameplay scene.
        private float? _pendingSegmentLengthSeconds;

        // Absolute songTime at which we should switch.
        private float _switchAtSongTime = float.PositiveInfinity;

        // Avoid resolving every frame.
        private float _nextResolveTime;

        /// <summary>
        /// Raised when the timer says it's time to switch to a new request.
        /// (Later BeatmapSwitcher+seek will handle the actual switch.)
        /// </summary>
        public event Action<SongRequest> SwitchRequested;
        public event Action<SongRequest> PreloadRequested;

        public void Initialize(GameplayManager gameplayManager)
        {
            _gameplayManager = gameplayManager;
        }

        public void StartProcessing()
        {
            _active = true;
            _isExecutingSwitch = false;
            _preloadTriggered = false;
            _pendingSegmentLengthSeconds = null;
            _switchAtSongTime = float.PositiveInfinity;
            _audioTimeSync = null;
            _audioSource = null;
            _nextResolveTime = 0f;
        }

        public void StopProcessing()
        {
            _active = false;
            _isExecutingSwitch = false;
            _pendingSegmentLengthSeconds = null;
            _switchAtSongTime = float.PositiveInfinity;
        }

        /// <summary>
        /// Call this when a song (or inserted segment) begins, to schedule the next switch.
        /// If null or <= 0, switching will be disabled for this segment.
        /// </summary>
        public void ArmForCurrentSegment(float? segmentLengthSeconds)
        {
            // 1. Store the length we want to switch after.
            _pendingSegmentLengthSeconds = (segmentLengthSeconds.HasValue && segmentLengthSeconds.Value > 0f)
                ? segmentLengthSeconds.Value
                : (float?)null;

            // 2. Reset the target time to Infinity. 
            // Do NOT calculate it here; Update() will do it when _audioTimeSync is ready.
            _switchAtSongTime = float.PositiveInfinity;

            // 3. Reset triggers
            _preloadTriggered = false;
            _isExecutingSwitch = false;
        }


        private void Update()
        {
            if (!_active || _gameplayManager == null || !_gameplayManager.IsPlaying())
                return;

            // Resolve AudioTimeSyncController occasionally until found.
            if ((_audioTimeSync == null || _audioSource == null) && Time.unscaledTime >= _nextResolveTime)
            {
                _nextResolveTime = Time.unscaledTime + 1.0f;
                ResolveAudio();
            }

            if (_audioTimeSync == null)
                return;

            // Arm switch time once audio exists and we have a segment length for this song.
            if (_pendingSegmentLengthSeconds.HasValue && float.IsPositiveInfinity(_switchAtSongTime))
            {
                _switchAtSongTime = _audioTimeSync.songTime + _pendingSegmentLengthSeconds.Value;
                _pendingSegmentLengthSeconds = null;
            }

            if (_isExecutingSwitch || float.IsPositiveInfinity(_switchAtSongTime))
                return;

            if (_audioTimeSync.songTime < _switchAtSongTime)
                return;

            // Optional safety: don't switch in the last 5 seconds of the current clip (like Shaffuru).
            if (_audioSource != null && _audioSource.clip != null)
            {
                float remaining = _audioSource.clip.length - _audioSource.time;
                if (remaining <= 5f)
                    return;
            }

            _isExecutingSwitch = true;

            // We only switch when there is an actual queued request to switch into.
            if (_gameplayManager.TryDequeueQueuedRequest(out var dequeueReq)) 
            {
                SwitchRequested?.Invoke(dequeueReq);
            }

            // If nothing queued, re-arm for "never", and allow future re-arming.
            _switchAtSongTime = float.PositiveInfinity;
            _isExecutingSwitch = false;
            // Trigger preload 5 seconds before the switch
            if (!_preloadTriggered && !float.IsPositiveInfinity(_switchAtSongTime) && _audioTimeSync != null)
            {
                float timeUntilSwitch = _switchAtSongTime - _audioTimeSync.songTime;
                if (timeUntilSwitch <= 5.0f && timeUntilSwitch > 0f)
                {
                    _preloadTriggered = true;
                   
                    if (_gameplayManager.PeekNextRequest(out var preloadReq)) // CHANGED NAME to be safe
                    {
                        PreloadRequested?.Invoke(preloadReq);
                    }
                }
            }
        }

        private void ResolveAudio()
        {
            // In gameplay, there is usually exactly one active controller.
            _audioTimeSync = FindObjectOfType<AudioTimeSyncController>();
            if (_audioTimeSync == null)
                return;

            try
            {
                _audioSource = AudioSourceField?.GetValue(_audioTimeSync) as AudioSource;
            }
            catch
            {
                _audioSource = null;
            }
        }
    }
}
