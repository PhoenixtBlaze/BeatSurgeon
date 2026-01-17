using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using IPA.Loader;
using SaberSurgeon.Gameplay; // CoroutineHost
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SaberSurgeon
{
    internal static class SceneHelper
    {
        // Public-ish flag for other modules (e.g. PFSL) to query
        internal static bool MpPlusInRoom { get; private set; }
        internal static string MpPlusRoomCode { get; private set; } = string.Empty;
        internal static bool MpPlusIsHost { get; private set; } = false;
        internal static event Action MpPlusRoomInfoChanged;

        internal static event Action<bool> MpPlusInRoomChanged;


        private static bool _initialized;
        private static bool _roomDataDumped;
        private static Coroutine _pollRoutine;
        private static string _lastRoomId;

        private static Assembly _mpAsm;

        // ---- MP+ NetworkManager reflection ----
        private static Type _mpNetworkManagerType;
        private static PropertyInfo _mpRoomDataProp;
        private static FieldInfo _mpRoomDataField;
        private static PropertyInfo _mpStatusProp;
        private static FieldInfo _mpStatusField;
        private static PropertyInfo _mpSelfPlayerProp;


        // ---- MP+ FlowCoordinator reflection (UI “lobby open” detection) ----
        // MP+ class: BeatSaberPlusMultiplayer.UI.MultiplayerPViewFlowCoordinator (derived from HMUIViewFlowCoordinatorMultiplayerPViewFlowCoordinator)
        private static Type _mpFlowCoordinatorType;
        private static Type _mpFlowCoordinatorHostType; // typically HMUIViewFlowCoordinatorMultiplayerPViewFlowCoordinator
        private static PropertyInfo _mpFlowInstanceProp; // static Instance
        private static PropertyInfo _mpFlowIsInMainViewProp;
        private static PropertyInfo _mpFlowIsInRoomViewProp;
        private static PropertyInfo _mpFlowIsInResultsViewProp;
        private static MethodInfo _mpFlowIsInHierarchyMethod; // IsFlowCoordinatorInHierarchy(FlowCoordinator)

        // One-time diagnostics
        private static bool _loggedBindings;
        private static bool _loggedNoMpAsm;

        // Snapshot log (only log when something changes)
        private static string _lastSnapshot;

        private const BindingFlags kStaticAny = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        private const BindingFlags kInstanceAny = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        // ---- MP+ event hook state ----
        private static bool _mpEventsHooked;

        private sealed class MpEventSub
        {
            public readonly EventInfo Event;
            public readonly Delegate Handler;
            public MpEventSub(EventInfo evt, Delegate handler)
            {
                Event = evt;
                Handler = handler;
            }
        }

        private static readonly List<MpEventSub> _mpEventSubs = new List<MpEventSub>();

        internal static void Init()
        {
            if (_initialized) return;
            _initialized = true;

            //DumpIpaPluginsOnce();
            SceneManager.activeSceneChanged += OnActiveSceneChanged;

            LogUtils.Debug(() => $"[SceneHelper] Init. Active scene: {SceneManager.GetActiveScene().name}");
            _pollRoutine = CoroutineHost.Instance.StartCoroutine(PollMpPlusState());
        }

        private static void DumpIpaPluginsOnce()
        {
            foreach (var p in PluginManager.EnabledPlugins)
                LogUtils.Debug(() => $"[SceneHelper] IPA plugin: Id={p.Id} Name={p.Name} Assembly={(p.Assembly?.GetName().Name ?? "null")}");
        }

        internal static void Dispose()
        {
            if (!_initialized) return;
            _initialized = false;

            SceneManager.activeSceneChanged -= OnActiveSceneChanged;

            if (_pollRoutine != null)
            {
                CoroutineHost.Instance.StopCoroutine(_pollRoutine);
                _pollRoutine = null;
            }

            // Unhook MP+ events (avoid duplicate subscriptions on reload)
            try
            {
                for (int i = 0; i < _mpEventSubs.Count; i++)
                {
                    var sub = _mpEventSubs[i];
                    var remove = sub?.Event?.GetRemoveMethod(true);
                    if (remove != null && sub?.Handler != null)
                        remove.Invoke(null, new object[] { sub.Handler });

                }
            }
            catch
            {
                // ignore
            }

            _mpEventSubs.Clear();
            _mpEventsHooked = false;

            MpPlusInRoom = false;

            LogUtils.Debug(() => "[SceneHelper] Disposed.");
        }

        private static void OnActiveSceneChanged(Scene prev, Scene next)
        {
            LogUtils.Debug(() => $"[SceneHelper] Active scene changed: {prev.name} -> {next.name}");
        }


        private static void DumpRoomDataMembersOnce(object roomData)
        {
            if (roomData == null) return;

            var t = roomData.GetType();
            LogUtils.Debug(() => $"[SceneHelper] MP+ RoomData runtime type = {t.FullName}");

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            foreach (var p in t.GetProperties(flags))
            {
                if (p == null || p.GetIndexParameters().Length != 0) continue;
                object v = null;
                try { v = p.GetValue(roomData, null); } catch { /* ignore */ }
                LogUtils.Debug(() => $"[SceneHelper] RoomData.PROP {p.PropertyType.Name} {p.Name} = {v}");
            }

            foreach (var f in t.GetFields(flags))
            {
                if (f == null) continue;
                object v = null;
                try { v = f.GetValue(roomData); } catch { /* ignore */ }
                LogUtils.Debug(() => $"[SceneHelper] RoomData.FIELD {f.FieldType.Name} {f.Name} = {v}");
            }
        }

        private static void DumpRoomDataOncePerRoom(object roomData)
        {
            if (roomData == null)
            {
                _lastRoomId = null;
                _roomDataDumped = false;
                return;
            }

            var roomId = GetMemberString(roomData, "RoomID"); // exists in your dump
            if (!string.Equals(roomId, _lastRoomId, StringComparison.Ordinal))
            {
                _lastRoomId = roomId;
                _roomDataDumped = false;
            }

            if (!_roomDataDumped)
            {
                _roomDataDumped = true;
                DumpRoomDataMembersOnce(roomData);
            }
        }

        private static bool _networkManagerDumped;

        private static void DumpNetworkManagerMembersOnce()
        {
            if (_networkManagerDumped) return;
            if (_mpNetworkManagerType == null) return;

            _networkManagerDumped = true;

            LogUtils.Debug(() => $"[SceneHelper] MP+ NetworkManager type = {_mpNetworkManagerType.FullName}");

            foreach (var p in _mpNetworkManagerType.GetProperties(kStaticAny))
            {
                if (p == null || p.GetIndexParameters().Length != 0) continue;
                object v = null;
                try { v = p.GetValue(null, null); } catch { /* ignore */ }
                LogUtils.Debug(() => $"[SceneHelper] NetMan.PROP {p.PropertyType.Name} {p.Name} = {v}");
            }

            foreach (var f in _mpNetworkManagerType.GetFields(kStaticAny))
            {
                if (f == null) continue;
                object v = null;
                try { v = f.GetValue(null); } catch { /* ignore */ }
                LogUtils.Debug(() => $"[SceneHelper] NetMan.FIELD {f.FieldType.Name} {f.Name} = {v}");
            }
        }


        private static IEnumerator PollMpPlusState()
        {
            while (true)
            {
                try
                {
                    EnsureMpAssembly();
                    EnsureMpNetworkReflection();
                    DumpNetworkManagerMembersOnce();
                    EnsureMpFlowReflection();

                    var sceneName = SceneManager.GetActiveScene().name;

                    // ----- Network-based detection -----
                    object statusObj = GetStaticValue(_mpStatusProp, _mpStatusField);
                    string statusStr = statusObj?.ToString();

                    object roomData = GetStaticValue(_mpRoomDataProp, _mpRoomDataField);
                    bool hasRoomData = roomData != null;

                    string roomCode = GetMemberString(roomData, "RoomCode");
                    string roomState = GetMemberString(roomData, "State");
                    string roomType = GetMemberString(roomData, "RoomType");

                    bool statusSaysInRoom = string.Equals(statusStr, "InRoom", StringComparison.OrdinalIgnoreCase);
                    bool mpInRoom = statusSaysInRoom || hasRoomData;

                    // Keep exported flag in sync even if events fail / no events fire.
                    SetMpPlusInRoom(mpInRoom, "poll", statusStr, hasRoomData);

                    // "In MP+ lobby/room" meaning: MP+ room exists, and we're not currently playing a map.
                    bool mpRoomSelecting = string.Equals(roomState, "SelectingSong", StringComparison.OrdinalIgnoreCase);
                    bool mpRoomWarmingUp = string.Equals(roomState, "WarmingUp", StringComparison.OrdinalIgnoreCase);
                    bool mpRoomPlaying = string.Equals(roomState, "Playing", StringComparison.OrdinalIgnoreCase);
                    bool mpRoomResults = string.Equals(roomState, "Results", StringComparison.OrdinalIgnoreCase);

                    bool mpInLobbyRoomState =
                        mpInRoom && (mpRoomSelecting || mpRoomWarmingUp || mpRoomResults);

                    // "In MP+ map" meaning: in GameCore and room state indicates playing/warmup/results.
                    bool mpInMap =
                        mpInRoom &&
                        string.Equals(sceneName, "GameCore", StringComparison.OrdinalIgnoreCase) &&
                        (mpRoomPlaying || mpRoomWarmingUp || mpRoomResults);



                    object selfPlayer = GetStaticValue(_mpSelfPlayerProp, null);

                    
                    uint hostLuid = GetMemberUInt(roomData, "HostLUID");
                    uint selfLuid = GetMemberUInt(selfPlayer, "LUID");

                    bool isHost = hostLuid != 0 && selfLuid != 0 && hostLuid == selfLuid;

                    SetMpPlusRoomInfo(roomCode, isHost);



                    // ----- UI FlowCoordinator-based detection (MP+ menu/lobby UI open) -----
                    object flow = GetStaticValue(_mpFlowInstanceProp, null);
                    bool flowExists = flow != null;

                    bool flowIsInMain = GetInstanceBool(flow, _mpFlowIsInMainViewProp);
                    bool flowIsInRoom = GetInstanceBool(flow, _mpFlowIsInRoomViewProp);
                    bool flowIsInResults = GetInstanceBool(flow, _mpFlowIsInResultsViewProp);
                    bool flowInHierarchy = InvokeIsInHierarchy(flow);

                    bool mpUiActive =
                        flowExists &&
                        (flowIsInMain || flowIsInRoom || flowIsInResults || flowInHierarchy);

                    // ----- Single snapshot log -----
                    string snapshot =
                        $"Scene={sceneName} | " +
                        $"MPAsm={(_mpAsm != null)} MPNet={(_mpNetworkManagerType != null)} | " +
                        $"Status={statusStr ?? "null"} InRoom={mpInRoom} RoomData={hasRoomData} | " +
                        $"RoomState={roomState ?? "null"} RoomCode={roomCode ?? "null"} RoomType={roomType ?? "null"} | " +
                        $"MP_UI={mpUiActive} (Main={flowIsInMain},Room={flowIsInRoom},Results={flowIsInResults},Hierarchy={flowInHierarchy}) | " +
                        $"MP_Lobby={mpInLobbyRoomState} MP_Map={mpInMap} | " +
                        $"MpPlusInRoomFlag={MpPlusInRoom}";

                    if (!string.Equals(snapshot, _lastSnapshot, StringComparison.Ordinal))
                    {
                        _lastSnapshot = snapshot;
                        LogUtils.Debug(() => $"[SceneHelper] MP+ snapshot -> {snapshot}");
                    }
                }
                catch (Exception ex)
                {
                    LogUtils.Warn($"[SceneHelper] MP+ poll error: {ex.GetType().Name}: {ex.Message}");
                }

                yield return new WaitForSecondsRealtime(0.5f);
            }
        }

        private static void SetMpPlusInRoom(bool value, string reason, string statusStr, bool hasRoomData)
        {
            if (MpPlusInRoom == value) return;


            MpPlusInRoom = value;
            MpPlusInRoomChanged?.Invoke(MpPlusInRoom);
            LogUtils.Debug(() => $"[SceneHelper] MP+ in-room changed -> {MpPlusInRoom} (reason={reason}, status={statusStr ?? "null"}, roomData={hasRoomData})");
        }

        private static void SetMpPlusRoomInfo(string roomCode, bool isHost)
        {
            if (roomCode == null)
                roomCode = string.Empty;


            if (MpPlusRoomCode == roomCode && MpPlusIsHost == isHost)
                return;

            MpPlusRoomCode = roomCode;
            MpPlusIsHost = isHost;
            MpPlusRoomInfoChanged?.Invoke();
        }


        // ----------------------------
        // Assembly / type binding
        // ----------------------------
        private static void EnsureMpAssembly()
        {
            const string probeTypeA = "BeatSaberPlusMultiplayer.Network.NetworkManager";
            const string probeTypeB = "BeatSaberPlus_Multiplayer.Network.NetworkManager";

            if (_mpAsm != null)
            {
                // Validate cached assembly; if wrong, drop it so we can retry each poll.
                if (_mpAsm.GetType(probeTypeA, false) != null || _mpAsm.GetType(probeTypeB, false) != null)
                    return;

                _mpAsm = null;
            }
            // Accept multiple possible BSIPA ids / assembly names for MP+
            // (Different builds sometimes use different ids/names.)
            string[] candidatePluginIds =
            {
                "BeatSaberPlusMultiplayer",
                "BeatSaberPlus_Multiplayer",
            };

            string[] candidateAssemblyNames =
            {
                "BeatSaberPlusMultiplayer",
                "BeatSaberPlus_Multiplayer",
            };

            // 1) Resolve by BSIPA plugin id first
            foreach (var id in candidatePluginIds)
            {
                var mpPlugin = PluginManager.EnabledPlugins
                    .FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

                if (mpPlugin?.Assembly != null)
                {
                    _mpAsm = mpPlugin.Assembly;
                    LogUtils.Debug(() => $"[SceneHelper] MP+ assembly resolved via plugin Id '{id}': {_mpAsm.GetName().Name}");
                    return;
                }
            }

            // 2) Resolve by assembly name in plugin list
            foreach (var asmName in candidateAssemblyNames)
            {
                var mpPlugin = PluginManager.EnabledPlugins
                    .FirstOrDefault(p => string.Equals(p.Assembly?.GetName().Name, asmName, StringComparison.Ordinal));

                if (mpPlugin?.Assembly != null)
                {
                    _mpAsm = mpPlugin.Assembly;
                    LogUtils.Debug(() => $"[SceneHelper] MP+ assembly resolved via plugin assembly name '{asmName}': {_mpAsm.GetName().Name}");
                    return;
                }
            }

            // 3) Resolve by AppDomain assembly name
            foreach (var asmName in candidateAssemblyNames)
            {
                var domainAsm = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => string.Equals(a.GetName().Name, asmName, StringComparison.Ordinal));

                if (domainAsm != null)
                {
                    _mpAsm = domainAsm;
                    LogUtils.Debug(() => $"[SceneHelper] MP+ assembly resolved via AppDomain '{asmName}': {_mpAsm.GetName().Name}");
                    return;
                }
            }

            // 4) Last resort: type probe across all loaded assemblies
            const string probeType = "BeatSaberPlusMultiplayer.Network.NetworkManager";
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (asm.GetType(probeType, throwOnError: false) != null)
                    {
                        _mpAsm = asm;
                        LogUtils.Debug(() => $"[SceneHelper] MP+ assembly resolved via global type probe: {_mpAsm.GetName().Name}");
                        return;
                    }
                }
                catch
                {
                    // ignore
                }
            }

            if (!_loggedNoMpAsm)
            {
                _loggedNoMpAsm = true;
                LogUtils.Warn("[SceneHelper] MP+ assembly still not found (all resolution strategies failed).");
            }
        }

        private static uint GetMemberUInt(object obj, string name)
        {
            if (obj == null) return 0;
            var t = obj.GetType();

            var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (p != null)
            {
                try
                {
                    var v = p.GetValue(obj, null);
                    return v is uint u ? u : 0;
                }
                catch { return 0; }
            }

            var f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null)
            {
                try
                {
                    var v = f.GetValue(obj);
                    return v is uint u ? u : 0;
                }
                catch { return 0; }
            }

            return 0;
        }

        private static void EnsureMpNetworkReflection()
        {
            if (_mpAsm == null) return;
            if (_mpNetworkManagerType != null) return;

            _mpNetworkManagerType =
                _mpAsm.GetType("BeatSaberPlusMultiplayer.Network.NetworkManager", throwOnError: false)
                ?? _mpAsm.GetType("BeatSaberPlus_Multiplayer.Network.NetworkManager", throwOnError: false);
            

            if (_mpNetworkManagerType == null) return;

            _mpSelfPlayerProp = _mpNetworkManagerType.GetProperty("SelfPlayer", kStaticAny);

            // RoomData in 6.4.2.0 is backed by private static RoomData mRoomData; and an internal static RoomData RoomData accessor exists.
            _mpRoomDataProp = _mpNetworkManagerType.GetProperty("RoomData", kStaticAny);
            _mpRoomDataField = _mpNetworkManagerType.GetField("mRoomData", kStaticAny);

            // Status exists as internal static ENetworkStatus Status { get; private set; } in 6.4.2.0.
            _mpStatusProp = _mpNetworkManagerType.GetProperty("Status", kStaticAny);
            _mpStatusField =
                _mpNetworkManagerType.GetField("Status", kStaticAny)
                ?? _mpNetworkManagerType.GetField("<Status>k__BackingField", kStaticAny);


            if (!_loggedBindings)
            {
                _loggedBindings = true;
                LogUtils.Debug(() => 
                    $"[SceneHelper] MP+ bound NetworkManager. " +
                    $"RoomDataProp={_mpRoomDataProp != null}, RoomDataField={_mpRoomDataField != null}, " +
                    $"StatusProp={_mpStatusProp != null}, StatusField={_mpStatusField != null}"
                );
            }

            // New: hook events once we have the NetworkManager type.
            TryHookMpNetworkEvents();

            // Initialize MpPlusInRoom from current state at bind time.
            RefreshMpPlusFlags("initial-bind");
        }

        private static void EnsureMpFlowReflection()
        {
            if (_mpAsm == null) return;

            // Find the flow coordinator type
            if (_mpFlowCoordinatorType == null)
                _mpFlowCoordinatorType = _mpAsm.GetType("BeatSaberPlusMultiplayer.UI.MultiplayerPViewFlowCoordinator", throwOnError: false);

            // Host type containing Instance property
            if (_mpFlowCoordinatorHostType == null)
                _mpFlowCoordinatorHostType = FindTypeByName(_mpAsm, "HMUIViewFlowCoordinatorMultiplayerPViewFlowCoordinator");

            if (_mpFlowInstanceProp == null)
            {
                // Try on host type first, then on the flow type itself.
                if (_mpFlowCoordinatorHostType != null)
                    _mpFlowInstanceProp = _mpFlowCoordinatorHostType.GetProperty("Instance", kStaticAny);

                if (_mpFlowInstanceProp == null && _mpFlowCoordinatorType != null)
                    _mpFlowInstanceProp = _mpFlowCoordinatorType.GetProperty("Instance", kStaticAny);
            }

            // Cache view-state bools
            if (_mpFlowCoordinatorType != null)
            {
                if (_mpFlowIsInMainViewProp == null)
                    _mpFlowIsInMainViewProp = _mpFlowCoordinatorType.GetProperty("IsInMainView", kInstanceAny);

                if (_mpFlowIsInRoomViewProp == null)
                    _mpFlowIsInRoomViewProp = _mpFlowCoordinatorType.GetProperty("IsInRoomView", kInstanceAny);

                if (_mpFlowIsInResultsViewProp == null)
                    _mpFlowIsInResultsViewProp = _mpFlowCoordinatorType.GetProperty("IsInResultsView", kInstanceAny);

                if (_mpFlowIsInHierarchyMethod == null)
                    _mpFlowIsInHierarchyMethod = FindIsFlowCoordinatorInHierarchyMethod(_mpFlowCoordinatorType);
            }
        }

        // ----------------------------
        // MP+ event hook (proper “push” updates)
        // ----------------------------
        private static void TryHookMpNetworkEvents()
        {
            if (_mpEventsHooked || _mpNetworkManagerType == null)
                return;

            var events = _mpNetworkManagerType.GetEvents(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

            int hooked = 0;

            foreach (var evt in events)
            {
                if (evt == null) continue;

                try
                {
                    var del = BuildEventForwarder(evt, evt.Name);
                    if (del == null)
                        continue;

                    var add = evt.GetAddMethod(true);
                    if (add == null)
                        continue;

                    add.Invoke(null, new object[] { del });
                    _mpEventSubs.Add(new MpEventSub(evt, del));
                    hooked++;
                }
                catch (Exception ex)
                {
                    LogUtils.Warn($"[SceneHelper] Failed hooking MP+ event {evt.Name}: {ex.GetType().Name} {ex.Message}");
                }
            }

            _mpEventsHooked = true;
            LogUtils.Debug(() => $"[SceneHelper] MP+ NetworkManager events hooked: {hooked}");
        }

        private static Delegate BuildEventForwarder(EventInfo evt, string eventName)
        {
            var handlerType = evt.EventHandlerType;
            if (handlerType == null)
                return null;

            var invoke = handlerType.GetMethod("Invoke");
            if (invoke == null)
                return null;

            // Event delegates should be void-returning; skip anything else.
            if (invoke.ReturnType != typeof(void))
                return null;

            var ps = invoke.GetParameters();

            // Support Action and Action<T> style delegates; skip complex ones.
            if (ps.Length > 1)
                return null;

            var parameters = ps.Select(p => Expression.Parameter(p.ParameterType, p.Name)).ToArray();

            var method = typeof(SceneHelper).GetMethod(nameof(OnMpNetworkEvent), BindingFlags.NonPublic | BindingFlags.Static);
            if (method == null)
                return null;

            var body = Expression.Call(method, Expression.Constant(eventName));
            return Expression.Lambda(handlerType, body, parameters).Compile();
        }

        private static void OnMpNetworkEvent(string evtName)
        {
            RefreshMpPlusFlags(evtName);
        }

        private static void RefreshMpPlusFlags(string reason)
        {
            object statusObj = GetStaticValue(_mpStatusProp, _mpStatusField);
            string statusStr = statusObj?.ToString();

            object roomData = GetStaticValue(_mpRoomDataProp, _mpRoomDataField);
            bool hasRoomData = roomData != null;
            if (roomData != null && !_roomDataDumped)
            {
                _roomDataDumped = true;
                DumpRoomDataMembersOnce(roomData);
            }
            bool inRoom =
                string.Equals(statusStr, "InRoom", StringComparison.OrdinalIgnoreCase) ||
                hasRoomData;

            SetMpPlusInRoom(inRoom, reason, statusStr, hasRoomData);

            if (roomData != null )//&& !_roomDataDumped)
            {
                //_roomDataDumped = true;
                DumpRoomDataMembersOnce(roomData);
                
            }
        }



        // ----------------------------
        // Reflection helpers
        // ----------------------------
        private static object GetStaticValue(PropertyInfo prop, FieldInfo field)
        {
            if (prop != null) return prop.GetValue(null, null);
            if (field != null) return field.GetValue(null);
            return null;
        }

        private static bool GetInstanceBool(object instance, PropertyInfo prop)
        {
            if (instance == null || prop == null) return false;
            try
            {
                var val = prop.GetValue(instance, null);
                return val is bool b && b;
            }
            catch
            {
                return false;
            }
        }

        private static bool InvokeIsInHierarchy(object flow)
        {
            if (flow == null || _mpFlowIsInHierarchyMethod == null) return false;
            try
            {
                // Method signature is IsFlowCoordinatorInHierarchy(FlowCoordinator) in HMUI, so pass itself.
                var result = _mpFlowIsInHierarchyMethod.Invoke(flow, new object[] { flow });
                return result is bool b && b;
            }
            catch
            {
                return false;
            }
        }

        private static MethodInfo FindIsFlowCoordinatorInHierarchyMethod(Type t)
        {
            // Find a 1-arg bool-returning method called IsFlowCoordinatorInHierarchy.
            try
            {
                var methods = t.GetMethods(kInstanceAny);
                for (int i = 0; i < methods.Length; i++)
                {
                    var m = methods[i];
                    if (m == null) continue;
                    if (m.Name != "IsFlowCoordinatorInHierarchy") continue;
                    if (m.ReturnType != typeof(bool)) continue;

                    var ps = m.GetParameters();
                    if (ps.Length != 1) continue;

                    return m;
                }
            }
            catch
            {
                // ignore
            }

            return null;
        }

        private static Type FindTypeByName(Assembly asm, string typeName)
        {
            try
            {
                var types = asm.GetTypes();
                for (int i = 0; i < types.Length; i++)
                {
                    var t = types[i];
                    if (t == null) continue;
                    if (t.Name == typeName) return t;
                }
            }
            catch (ReflectionTypeLoadException rtle)
            {
                var types = rtle.Types;
                if (types == null) return null;

                for (int i = 0; i < types.Length; i++)
                {
                    var t = types[i];
                    if (t == null) continue;
                    if (t.Name == typeName) return t;
                }
            }
            catch
            {
                // ignore
            }

            return null;
        }

        private static string GetMemberString(object obj, string name)
        {
            if (obj == null) return null;

            var t = obj.GetType();

            // Property first
            var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (p != null)
            {
                try { return p.GetValue(obj, null)?.ToString(); }
                catch { return null; }
            }

            // Then field
            var f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null)
            {
                try { return f.GetValue(obj)?.ToString(); }
                catch { return null; }
            }

            return null;
        }
    }
}
