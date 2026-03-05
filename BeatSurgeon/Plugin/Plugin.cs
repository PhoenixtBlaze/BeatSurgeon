using System;
using System.IO;
using System.Linq;
using System.Reflection;
using BeatSurgeon.Installers;
using BeatSurgeon.Utils;
using HarmonyLib;
using IPA;
using IPA.Config;
using IPA.Config.Stores;
using IPA.Loader;
using IPA.Logging;
using SiraUtil.Zenject;

namespace BeatSurgeon
{
    [Plugin(RuntimeOptions.DynamicInit)]
    [NoEnableDisable]
    internal sealed class Plugin
    {
        private static bool _chaosNaClLoaded;

        static Plugin()
        {
            // Embedded-only resolver for Chaos.NaCl.
            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;

            // Preload once so first entitlement check does not race resolution.
            TryLoadChaosNaClFromEmbedded(logSuccess: false);
        }

        private static Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            try
            {
                AssemblyName requested = new AssemblyName(args.Name);
                if (!string.Equals(requested.Name, "Chaos.NaCl", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                return TryLoadChaosNaClFromEmbedded(logSuccess: true);
            }
            catch (Exception ex)
            {
                Log?.Error("Chaos.NaCl resolver failure: " + ex.Message);
                return null;
            }
        }

        private static Assembly TryLoadChaosNaClFromEmbedded(bool logSuccess)
        {
            try
            {
                Assembly alreadyLoaded = AppDomain.CurrentDomain
                    .GetAssemblies()
                    .FirstOrDefault(a =>
                    {
                        try
                        {
                            return string.Equals(a.GetName().Name, "Chaos.NaCl", StringComparison.OrdinalIgnoreCase);
                        }
                        catch
                        {
                            return false;
                        }
                    });

                if (alreadyLoaded != null)
                {
                    _chaosNaClLoaded = true;
                    return alreadyLoaded;
                }

                if (_chaosNaClLoaded)
                {
                    return null;
                }

                Assembly self = Assembly.GetExecutingAssembly();
                string resourceName = self
                    .GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith("Chaos.NaCl.dll", StringComparison.OrdinalIgnoreCase));

                if (string.IsNullOrWhiteSpace(resourceName))
                {
                    Log?.Error("Chaos.NaCl resolver: embedded resource not found.");
                    return null;
                }

                using (Stream stream = self.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        Log?.Error("Chaos.NaCl resolver: resource stream was null for " + resourceName);
                        return null;
                    }

                    using (var ms = new MemoryStream())
                    {
                        stream.CopyTo(ms);
                        Assembly loaded = Assembly.Load(ms.ToArray());
                        _chaosNaClLoaded = loaded != null;
                        if (loaded != null && logSuccess)
                        {
                            Log?.Info("Loaded Chaos.NaCl from embedded resource: " + resourceName);
                        }

                        return loaded;
                    }
                }
            }
            catch (Exception ex)
            {
                Log?.Error("Chaos.NaCl embedded load failure: " + ex.Message);
                return null;
            }
        }

        internal static Plugin Instance { get; private set; }
        internal static Logger Log { get; private set; }
        internal static LogUtil ScopedLog { get; private set; }
        internal static PluginConfig Settings { get; private set; }
        internal static Config Configuration { get; private set; }
        internal static PluginMetadata Metadata { get; private set; }

        private readonly Harmony _harmony;

        [Init]
        public Plugin(Logger logger, PluginMetadata metadata, Config conf, Zenjector zenjector)
        {
            Instance = this;
            Log = logger;
            Metadata = metadata;
            Configuration = conf;

            LogUtil.Initialize(logger);
            ScopedLog = LogUtil.GetLogger(nameof(Plugin));
            ScopedLog.Lifecycle("Init started");

            try
            {
                Settings = conf.Generated<PluginConfig>();
                PluginConfig.Instance = Settings;
                ScopedLog.Info("Config loaded.");
            }
            catch (Exception ex)
            {
                ScopedLog.CriticalException(ex, "Config initialization");
            }

            _harmony = new Harmony("com.phoenixtblaze.beatsurgeon");

            try
            {
                zenjector.UseLogger(logger);
                zenjector.UseMetadataBinder<Plugin>();
                zenjector.Install<BeatSurgeonAppInstaller>(Location.App);
                zenjector.Install<BeatSurgeonMenuInstaller>(Location.Menu);
            }
            catch (Exception ex)
            {
                ScopedLog.CriticalException(ex, "Zenject installer registration");
            }

            ScopedLog.Lifecycle("Init complete");
        }

        [OnEnable]
        public void OnEnable()
        {
            ScopedLog.Lifecycle("OnEnable - applying Harmony patches");
            try
            {
                _harmony.PatchAll(System.Reflection.Assembly.GetExecutingAssembly());
                ScopedLog.Info("Harmony patches applied successfully");
            }
            catch (Exception ex)
            {
                ScopedLog.CriticalException(ex, "Harmony.PatchAll");
            }
        }

        [OnDisable]
        public void OnDisable()
        {
            ScopedLog.Lifecycle("OnDisable - removing Harmony patches");
            try
            {
                _harmony.UnpatchSelf();
            }
            catch (Exception ex)
            {
                ScopedLog.Exception(ex, "Harmony.UnpatchSelf");
            }
        }
    }
}
