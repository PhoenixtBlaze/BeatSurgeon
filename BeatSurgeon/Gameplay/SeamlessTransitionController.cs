using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BeatSurgeon.Utils;
using HarmonyLib;
using UnityEngine;
using Zenject;

namespace BeatSurgeon.Gameplay
{
    /// <summary>
    /// Endless-mode-only seamless transition controller.
    ///
    /// Instead of reloading scenes (which forces a black "EmptyTransition" screen and stops the
    /// notes + audio), this controller splices the next map's notes and audio into the live,
    /// still-running GameCore. The environment is intentionally kept the same for the whole endless
    /// session, which is the only way to keep cubes and audio uninterrupted.
    ///
    /// Lifecycle: bound into the GameCore Zenject container by <c>SeamlessInstallerPatch</c> ONLY when
    /// an endless session is active. On a normal (non-endless) map this component is never created,
    /// so normal gameplay uses the stock GameplayCore and is provably unaffected.
    ///
    /// Continuity guarantees:
    /// - Notes ride <c>AudioTimeSyncController.songTime</c>, which is wall-clock driven and monotonic.
    /// - Next-map notes are appended into the live <c>BeatmapData</c> re-timestamped onto that same
    ///   monotonic timeline (forward-only, so the callbacks controller picks them up without replay).
    /// - At hand-off we set the ATS <c>_songTimeOffset</c> so the new clip's audio.time tracks
    ///   (songTime - delta), keeping audio and notes in sync by construction.
    /// - If anything cannot be completed in time, we simply do nothing and let the stock end-of-song
    ///   chain (EndlessHarmonyPatch.ReplaceScenes) run — i.e. the existing behavior is the fallback.
    /// </summary>
    public class SeamlessTransitionController : MonoBehaviour
    {
        private static SeamlessTransitionController _instance;

        private static readonly LogUtil _log = LogUtil.GetLogger("SeamlessTransition");

        // How far before the current song's end to begin loading the next map.
        private const float PreloadLeadSeconds = 25f;
        // Minimum lead the first spliced note must have so it jumps in cleanly (covers note half-jump).
        private const float MinNoteLeadSeconds = 3f;
        // Keep the hand-off comfortably before GameSongController's songDidFinish threshold (songEndTime - 0.2).
        private const float HandoffMarginSeconds = 0.5f;

        [Inject] private readonly AudioTimeSyncController _ats;
        [Inject] private readonly GameplayCoreSceneSetupData _sceneSetupData;
        [Inject] private readonly IReadonlyBeatmapData _liveBeatmapDataRO;

        private GameplayManager _gameplayManager;

        private enum State { Idle, Preloading, Armed, Crossfading, Disabled }
        private State _state = State.Idle;

        public static SeamlessTransitionController Instance => _instance;

        public bool ShouldSuppressSongFinish => _state != State.Disabled && _gameplayManager != null && _gameplayManager.IsPlaying();

        private float _minimumReservedSongDuration;

        // Preload results (written off-thread, consumed on the main thread).
        private volatile bool _loadDone;
        private volatile bool _loadFailed;
        private AudioClip _nextClip;
        private IReadonlyBeatmapData _nextData;

        private BeatmapLevel _reservedNextLevel;
        private BeatmapKey _reservedNextKey;
        private GameplayModifiers _reservedModifiers;
        private PlayerSpecificSettings _reservedPlayerSettings;
        private ColorScheme _reservedColor;
        private EnvironmentsListModel _reservedEnvs;

        // Armed splice parameters (songTime space).
        private float _startOffset;     // O: where in the next map we start ("1 min in" => 60)
        private float _delta;           // origTime + delta => live songTime
        private float _crossStart;      // songTime at which the crossfade begins
        private float _crossEnd;        // songTime at which the hand-off happens
        private float _fadeFromVolume;  // ATS source volume captured at crossfade start
        private AudioSource _incomingSource;

        // ---- cached reflection ----
        private static readonly FieldInfo F_audioSource = AccessTools.Field(typeof(AudioTimeSyncController), "_audioSource");
        private static readonly FieldInfo F_initData = AccessTools.Field(typeof(AudioTimeSyncController), "_initData");
        private static readonly FieldInfo F_audioLatency = AccessTools.Field(typeof(AudioTimeSyncController), "_audioLatency");
        private static readonly FieldInfo F_audioStartOffset = AccessTools.Field(typeof(AudioTimeSyncController), "_audioStartTimeOffsetSinceStart");
        private static readonly FieldInfo F_playbackLoopIndex = AccessTools.Field(typeof(AudioTimeSyncController), "_playbackLoopIndex");
        private static readonly FieldInfo F_prevAudioSamplePos = AccessTools.Field(typeof(AudioTimeSyncController), "_prevAudioSamplePos");
        private static readonly FieldInfo F_songTimeOffset = AccessTools.Field(typeof(AudioTimeSyncController), "_songTimeOffset");
        private static readonly FieldInfo F_timeScale = AccessTools.Field(typeof(AudioTimeSyncController), "_timeScale");
        private static readonly FieldInfo F_initAudioClip = AccessTools.Field(typeof(AudioTimeSyncController.InitData), "audioClip");

        private static readonly MethodInfo M_setItemTime = AccessTools.PropertySetter(typeof(BeatmapDataItem), "time");
        private static readonly MethodInfo M_setSliderTailTime = AccessTools.PropertySetter(typeof(SliderData), "tailTime");

        private static readonly FieldInfo F_setup_settingsManager = AccessTools.Field(typeof(GameplayCoreSceneSetupData), "_settingsManager");
        private static readonly FieldInfo F_setup_audioLoader = AccessTools.Field(typeof(GameplayCoreSceneSetupData), "_audioClipAsyncLoader");
        private static readonly FieldInfo F_setup_dataLoader = AccessTools.Field(typeof(GameplayCoreSceneSetupData), "_beatmapDataLoader");
        private static readonly FieldInfo F_setup_entitlement = AccessTools.Field(typeof(GameplayCoreSceneSetupData), "_beatmapLevelsEntitlementModel");
        private static readonly FieldInfo F_setup_levelsModel = AccessTools.Field(typeof(GameplayCoreSceneSetupData), "_beatmapLevelsModel");

        private bool _loggedAppendError;

        private void Start()
        {
            if (_ats == null || _sceneSetupData == null || _liveBeatmapDataRO == null)
            {
                _log.Warn("Missing GameCore dependencies; seamless transition disabled for this map.");
                _state = State.Disabled;
                return;
            }

            _gameplayManager = GameplayManager.GetInstance();
            if (_gameplayManager == null || !_gameplayManager.IsPlaying())
            {
                _log.Warn("GameplayManager is not active; seamless transition disabled for this map.");
                _state = State.Disabled;
                return;
            }

            var cfg = PluginConfig.Instance;
            if (cfg == null || !cfg.SeamlessTransitionEnabled)
            {
                _state = State.Disabled;
                return;
            }

            _minimumReservedSongDuration = cfg.SeamlessStartOffsetSeconds + cfg.SeamlessCrossfadeSeconds + HandoffMarginSeconds + 1f;
            if (!TryReserveAndStartPreload(cfg))
            {
                _state = State.Disabled;
                return;
            }

            _instance = this;

            _log.Info("Seamless transition controller active for this endless map.");
        }

        private void LateUpdate()
        {
            if (_state == State.Disabled) return;

            try
            {
                var cfg = PluginConfig.Instance;
                if (cfg == null || !cfg.SeamlessTransitionEnabled) return;

                var gm = _gameplayManager ?? GameplayManager.GetInstance();
                if (gm == null || !gm.IsPlaying()) return; // endless ended -> let the song finish normally

                float songTime = _ats.songTime;
                float songEnd = _ats.songEndTime;
                if (songEnd <= 0f) return;

                switch (_state)
                {
                    case State.Idle:
                        break;

                    case State.Preloading:
                        if (_loadFailed)
                        {
                            _log.Warn("Preload failed; falling back to stock end-of-song chain.");
                            _state = State.Idle;
                            ClearPreload();
                            // Re-arm only after this song actually ends (handled by stock patch).
                            _state = State.Disabled;
                        }
                        else if (_loadDone)
                        {
                            TryArmSplice(songTime, songEnd, cfg);
                        }
                        break;

                    case State.Armed:
                        if (songTime >= _crossStart) BeginCrossfade(songTime);
                        break;

                    case State.Crossfading:
                        UpdateCrossfade(songTime);
                        if (songTime >= _crossEnd) CompleteHandoff(songTime);
                        break;
                }
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "LateUpdate; disabling seamless for this map (stock chain will handle the next switch).");
                AbortToStock();
            }
        }

        private bool TryReserveAndStartPreload(PluginConfig cfg)
        {
            if (_gameplayManager == null)
            {
                return false;
            }

            if (IsEndlessEnding(_gameplayManager, cfg))
            {
                _log.Warn("Endless mode is nearing its timer limit; skipping seamless reservation for this song.");
                return false;
            }

            if (!_gameplayManager.TryReserveSeamlessNextChain(_minimumReservedSongDuration,
                    out _reservedNextLevel, out _reservedNextKey, out _reservedModifiers,
                    out _reservedPlayerSettings, out _reservedColor, out _reservedEnvs)
                || _reservedNextLevel == null)
            {
                _log.Warn("No seamless-compatible next level available; stock end-of-song chaining will remain in control.");
                return false;
            }

            _state = State.Preloading;
            _loadDone = false;
            _loadFailed = false;
            _ = PreloadNextAsync();
            return true;
        }

        private static bool IsEndlessEnding(GameplayManager gm, PluginConfig cfg)
        {
            float remaining = gm.GetRemainingTime();
            // GetRemainingTime() may be 0 when endless has no countdown; only treat a positive,
            // nearly-elapsed timer as "ending" so we stop starting new seamless splices.
            return remaining > 0f && remaining <= (cfg.SeamlessCrossfadeSeconds + PreloadLeadSeconds + 1f);
        }

        private async Task PreloadNextAsync()
        {
            try
            {
                var settingsMgr = (SettingsManager)F_setup_settingsManager.GetValue(_sceneSetupData);
                var audioLoader = (AudioClipAsyncLoader)F_setup_audioLoader.GetValue(_sceneSetupData);
                var dataLoader = (BeatmapDataLoader)F_setup_dataLoader.GetValue(_sceneSetupData);
                var entitlement = (BeatmapLevelsEntitlementModel)F_setup_entitlement.GetValue(_sceneSetupData);
                var levelsModel = (BeatmapLevelsModel)F_setup_levelsModel.GetValue(_sceneSetupData);

                // Reuse the CURRENT session's environment so the transformed data + lighting match the
                // live, still-running environment exactly (single env for the whole endless session).
                // Construct via reflection so we are robust to constructor-signature drift between the
                // referenced game assembly and the decompiled reference set.
                var setup = BuildNextSetupData(_reservedNextKey, _reservedNextLevel, _reservedModifiers, _reservedPlayerSettings, _reservedColor, _reservedEnvs,
                    settingsMgr, audioLoader, dataLoader, entitlement, levelsModel);
                if (setup == null)
                {
                    _loadFailed = true;
                    return;
                }

                await setup.LoadTransformedBeatmapDataAsync().ConfigureAwait(false);

                if (this == null) return; // component destroyed during the await
                if (setup.songAudioClip == null || setup.transformedBeatmapData == null)
                {
                    _log.Warn("Next map loaded but audio clip or beatmap data was null.");
                    _loadFailed = true;
                    return;
                }

                _nextClip = setup.songAudioClip;
                _nextData = setup.transformedBeatmapData;
                _loadDone = true;
                _log.Info($"Seamless preload ready: {_reservedNextLevel.songName}");
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "PreloadNextAsync");
                _loadFailed = true;
            }
        }

        private GameplayCoreSceneSetupData BuildNextSetupData(
            BeatmapKey nextKey, BeatmapLevel nextLevel, GameplayModifiers modifiers,
            PlayerSpecificSettings playerSettings, ColorScheme color, EnvironmentsListModel envs,
            SettingsManager settingsMgr, AudioClipAsyncLoader audioLoader, BeatmapDataLoader dataLoader,
            BeatmapLevelsEntitlementModel entitlement, BeatmapLevelsModel levelsModel)
        {
            try
            {
                var ctor = typeof(GameplayCoreSceneSetupData).GetConstructors()
                    .OrderByDescending(c => c.GetParameters().Length)
                    .FirstOrDefault();
                if (ctor == null)
                {
                    _log.Warn("No GameplayCoreSceneSetupData constructor found.");
                    return null;
                }

                var pars = ctor.GetParameters();
                var args = new object[pars.Length];
                for (int i = 0; i < pars.Length; i++)
                {
                    string n = pars[i].Name?.ToLowerInvariant() ?? string.Empty;
                    switch (n)
                    {
                        case "beatmapkey": args[i] = nextKey; break;
                        case "beatmaplevel": args[i] = nextLevel; break;
                        case "gameplaymodifiers": args[i] = modifiers; break;
                        case "playerspecificsettings": args[i] = playerSettings; break;
                        case "practicesettings": args[i] = null; break;
                        case "targetenvironmentinfo": args[i] = _sceneSetupData.targetEnvironmentInfo; break;
                        case "originalenvironmentinfo": args[i] = _sceneSetupData.originalEnvironmentInfo; break;
                        case "colorscheme": args[i] = color ?? _sceneSetupData.colorScheme; break;
                        case "settingsmanager": args[i] = settingsMgr; break;
                        case "audioclipasyncloader": args[i] = audioLoader; break;
                        case "beatmapdataloader": args[i] = dataLoader; break;
                        case "beatmaplevelsentitlementmodel": args[i] = entitlement; break;
                        case "enablebeatmapdatacaching": args[i] = false; break;
                        case "environmentslistmodel": args[i] = envs ?? _sceneSetupData.environmentsListModel; break;
                        case "allownullbeatmapleveldata": args[i] = false; break;
                        case "beatmaplevelsmodel": args[i] = levelsModel; break;
                        case "beatmapleveldata": args[i] = null; break;
                        case "recordingtooldata": args[i] = null; break;
                        default:
                            if (pars[i].HasDefaultValue) args[i] = pars[i].DefaultValue;
                            else if (pars[i].ParameterType.IsValueType) args[i] = Activator.CreateInstance(pars[i].ParameterType);
                            else args[i] = null;
                            break;
                    }
                }
                return (GameplayCoreSceneSetupData)ctor.Invoke(args);
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "BuildNextSetupData");
                return null;
            }
        }

        private void TryArmSplice(float songTime, float songEnd, PluginConfig cfg)
        {
            float crossfade = Mathf.Max(0.25f, cfg.SeamlessCrossfadeSeconds);
            float offset = Mathf.Max(0f, cfg.SeamlessStartOffsetSeconds);

            if (_nextClip == null || _nextData == null)
            {
                _log.Warn("Seamless preload never produced a valid clip/data pair; disabling seamless for this map.");
                ClearPreload();
                _state = State.Disabled;
                return;
            }

            float requiredSongLength = offset + crossfade + HandoffMarginSeconds + 1f;
            if (_nextClip.length < requiredSongLength)
            {
                _log.Warn($"Seamless candidate is too short for a {offset:0.0}s splice point: clip={_nextClip.length:0.0}s required={requiredSongLength:0.0}s. Falling back to stock chaining.");
                ClearPreload();
                _state = State.Disabled;
                return;
            }

            // Latest possible crossfade window that still hands off before songDidFinish.
            float crossEnd = songEnd - HandoffMarginSeconds;
            float crossStart = crossEnd - crossfade;

            // Ensure the first spliced note has enough jump-in lead from "now".
            float minCrossStart = songTime + MinNoteLeadSeconds;
            if (crossStart < minCrossStart)
            {
                // Not enough time to seamlessly splice this song; let the stock chain handle it.
                _log.Warn("Insufficient time to arm seamless splice; falling back to stock chain for this song.");
                ClearPreload();
                _state = State.Disabled;
                return;
            }

            // Make sure the incoming clip's audio data is ready before we will play it.
            if (_nextClip.loadState == AudioDataLoadState.Unloaded)
            {
                _nextClip.LoadAudioData();
            }

            _startOffset = Mathf.Min(offset, Mathf.Max(0f, _nextClip.length - crossfade - 1f));
            _crossStart = crossStart;
            _crossEnd = crossEnd;
            _delta = crossStart - _startOffset;

            int appended = AppendNextMap(_nextData, _delta, _startOffset);
            _log.Info($"Armed seamless splice: offset={_startOffset:0.0}s crossStart={_crossStart:0.0} crossEnd={_crossEnd:0.0} delta={_delta:0.0} appendedItems={appended}");
            _state = State.Armed;
        }

        private int AppendNextMap(IReadonlyBeatmapData src, float delta, float minOrigTime)
        {
            if (!(_liveBeatmapDataRO is BeatmapData live)) return 0;

            int count = 0;
            foreach (var item in src.allBeatmapDataItems)
            {
                try
                {
                    if (item.time < minOrigTime) continue;

                    if (item is BeatmapObjectData obj)
                    {
                        var shifted = ShiftObject(obj, delta);
                        if (shifted != null)
                        {
                            live.AddBeatmapObjectDataInOrder(shifted);
                            count++;
                        }
                    }
                    else if (item is BeatmapEventData ev)
                    {
                        // Re-time the next map's lighting/events so the (same) environment keeps animating.
                        var copy = (BeatmapEventData)ev.GetCopy();
                        M_setItemTime.Invoke(copy, new object[] { ev.time + delta });
                        live.InsertBeatmapEventDataInOrder(copy);
                        count++;
                    }
                }
                catch (Exception ex)
                {
                    if (!_loggedAppendError)
                    {
                        _loggedAppendError = true;
                        _log.Warn("Skipped a beatmap item during seamless append: " + ex.Message);
                    }
                }
            }
            return count;
        }

        private static BeatmapObjectData ShiftObject(BeatmapObjectData o, float delta)
        {
            // NoteData (notes, bombs, burst-slider elements) has a clean CopyWith that preserves the
            // relative parity durations (timeToPrev/NextColorNote) while moving the absolute time.
            if (o is NoteData nd)
            {
                return nd.CopyWith(time: nd.time + delta);
            }

            var copy = (BeatmapObjectData)o.GetCopy();
            M_setItemTime.Invoke(copy, new object[] { o.time + delta });

            // Sliders/arcs carry an absolute tail time that must shift by the same delta.
            if (copy is SliderData sd)
            {
                M_setSliderTailTime.Invoke(sd, new object[] { sd.tailTime + delta });
            }
            return copy;
        }

        private void BeginCrossfade(float songTime)
        {
            try
            {
                var atsSource = (AudioSource)F_audioSource.GetValue(_ats);
                float timeScale = (float)F_timeScale.GetValue(_ats);
                _fadeFromVolume = atsSource != null ? atsSource.volume : 1f;

                _incomingSource = _ats.gameObject.AddComponent<AudioSource>();
                _incomingSource.clip = _nextClip;
                _incomingSource.playOnAwake = false;
                _incomingSource.loop = false;
                _incomingSource.spatialBlend = 0f;
                _incomingSource.pitch = timeScale;
                _incomingSource.volume = 0f;
                if (atsSource != null) _incomingSource.outputAudioMixerGroup = atsSource.outputAudioMixerGroup;
                _incomingSource.time = Mathf.Clamp(_startOffset, 0f, Mathf.Max(0f, _nextClip.length - 0.05f));
                _incomingSource.Play();

                _state = State.Crossfading;
                _log.Info("Seamless crossfade started.");
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "BeginCrossfade");
                AbortToStock();
            }
        }

        private void UpdateCrossfade(float songTime)
        {
            float p = Mathf.Clamp01((songTime - _crossStart) / Mathf.Max(0.0001f, _crossEnd - _crossStart));
            var atsSource = (AudioSource)F_audioSource.GetValue(_ats);
            if (atsSource != null) atsSource.volume = _fadeFromVolume * (1f - p);
            if (_incomingSource != null) _incomingSource.volume = _fadeFromVolume * p;
        }

        private void CompleteHandoff(float songTime)
        {
            try
            {
                var atsSource = (AudioSource)F_audioSource.GetValue(_ats);
                var initData = (AudioTimeSyncController.InitData)F_initData.GetValue(_ats);
                float audioLatency = (float)F_audioLatency.GetValue(_ats);
                float timeScale = (float)F_timeScale.GetValue(_ats);

                // Position in the new clip that corresponds to the current monotonic songTime.
                float newClipPos = _startOffset + (songTime - _crossStart);
                newClipPos = Mathf.Clamp(newClipPos, 0f, Mathf.Max(0f, _nextClip.length - 0.05f));

                if (atsSource != null)
                {
                    atsSource.clip = _nextClip;
                    atsSource.pitch = timeScale;
                    atsSource.timeSamples = (int)(newClipPos * _nextClip.frequency);
                    atsSource.volume = _fadeFromVolume;
                    if (!atsSource.isPlaying) atsSource.Play();
                }

                // Reconfigure the master clock so songTime stays continuous and audio.time = songTime - delta.
                // Derivation (verified against ATS.Update): songTime = num2 - (_songTimeOffset + _audioLatency),
                // with num2 = timeSinceStart - _audioStartTimeOffsetSinceStart. We want audio.time = songTime - delta,
                // so _songTimeOffset = -delta - _audioLatency and _audioStartTimeOffsetSinceStart = timeSinceStart - (songTime - delta).
                float timeSinceStart = Time.timeSinceLevelLoad * timeScale;
                F_songTimeOffset.SetValue(_ats, -_delta - audioLatency);
                F_audioStartOffset.SetValue(_ats, timeSinceStart - (songTime - _delta));
                F_playbackLoopIndex.SetValue(_ats, 0);
                if (atsSource != null) F_prevAudioSamplePos.SetValue(_ats, atsSource.timeSamples);

                // Keep songEndTime correct (it reads InitData.audioClip.length) so the level ends with the new song.
                if (initData != null) F_initAudioClip.SetValue(initData, _nextClip);

                if (_gameplayManager != null)
                {
                    _gameplayManager.CommitReservedSeamlessNextChain();
                }

                if (_incomingSource != null)
                {
                    Destroy(_incomingSource);
                    _incomingSource = null;
                }

                _log.Info($"Seamless hand-off complete at songTime={songTime:0.0}, newClipPos={newClipPos:0.0}.");
                ClearPreload();
                if (_gameplayManager != null && PluginConfig.Instance != null && PluginConfig.Instance.SeamlessTransitionEnabled)
                {
                    if (!TryReserveAndStartPreload(PluginConfig.Instance))
                    {
                        _state = State.Disabled;
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "CompleteHandoff");
                AbortToStock();
            }
        }

        private void ClearPreload()
        {
            _loadDone = false;
            _loadFailed = false;
            _nextClip = null;
            _nextData = null;
        }

        private void AbortToStock()
        {
            // On any unexpected failure, restore audible audio and stop driving seamless. The stock
            // end-of-song chain (ReplaceScenes) will take over for the next switch.
            try
            {
                var atsSource = (AudioSource)F_audioSource.GetValue(_ats);
                if (atsSource != null && _fadeFromVolume > 0f) atsSource.volume = _fadeFromVolume;
            }
            catch { /* best-effort */ }

            if (_incomingSource != null)
            {
                Destroy(_incomingSource);
                _incomingSource = null;
            }
            ClearPreload();
            _gameplayManager?.ClearReservedSeamlessNextChain();
            if (_instance == this)
            {
                _instance = null;
            }
            _state = State.Disabled;
        }

        private void OnDestroy()
        {
            if (_incomingSource != null)
            {
                Destroy(_incomingSource);
                _incomingSource = null;
            }
            if (_instance == this)
            {
                _instance = null;
            }
            _gameplayManager?.ClearReservedSeamlessNextChain();
        }
    }
}
