namespace Rebind;

internal static class RebindInputSuppression
{
    private const string RebindAssemblyName = "Rebind";
    private static readonly object Sync = new();
    private static HashSet<string>? _pluginAssemblyNames;

    internal static void TryPatch(Harmony harmony, ManualLogSource logger)
    {
        try
        {
            var prefix = AccessTools.Method(typeof(RebindInputSuppression), nameof(OnInputKeyQueryPrefix));
            if (prefix == null) return;

            PatchInputMethod(harmony, nameof(Input.GetKey), prefix);
            PatchInputMethod(harmony, nameof(Input.GetKeyDown), prefix);
            PatchInputMethod(harmony, nameof(Input.GetKeyUp), prefix);
            logger.LogInfo("Patched input suppression bridge.");
        }
        catch (Exception ex)
        {
            logger.LogWarning($"Failed to patch input suppression bridge: {ex.Message}");
        }
    }

    private static void PatchInputMethod(Harmony harmony, string methodName, MethodInfo prefix)
    {
        var target = AccessTools.Method(typeof(Input), methodName, new[] { typeof(KeyCode) });
        if (target == null) return;
        harmony.Patch(target, prefix: new HarmonyMethod(prefix));
    }

    private static bool OnInputKeyQueryPrefix(ref bool __result)
    {
        if (!RebindMenuInjector.ShouldSuppressExternalHotkeys()) return true;
        if (!ShouldSuppressInputForCurrentCaller()) return true;

        __result = false;
        return false;
    }

    private static bool ShouldSuppressInputForCurrentCaller()
    {
        try
        {
            var trace = new StackTrace(skipFrames: 2, fNeedFileInfo: false);
            for (var i = 0; i < trace.FrameCount; i++)
            {
                var method = trace.GetFrame(i)?.GetMethod();
                if (method?.DeclaringType?.Assembly?.GetName().Name is not string assemblyName || assemblyName.Length == 0)
                {
                    continue;
                }

                if (IsFrameworkAssembly(assemblyName))
                {
                    continue;
                }

                if (string.Equals(assemblyName, RebindAssemblyName, StringComparison.Ordinal))
                {
                    return false;
                }

                return IsPluginAssembly(assemblyName);
            }
        }
        catch
        {
            // Fail open to avoid breaking gameplay input.
            return false;
        }

        return false;
    }

    private static bool IsPluginAssembly(string assemblyName)
    {
        lock (Sync)
        {
            _pluginAssemblyNames ??= BuildPluginAssemblyNameSet();
            return _pluginAssemblyNames.Contains(assemblyName);
        }
    }

    private static HashSet<string> BuildPluginAssemblyNameSet()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var pluginInfos = BepInEx.Bootstrap.Chainloader.PluginInfos;
        foreach (var pluginInfo in pluginInfos.Values)
        {
            if (pluginInfo.Instance?.GetType().Assembly.GetName().Name is not string name || name.Length == 0)
            {
                continue;
            }

            names.Add(name);
        }

        names.Add(RebindAssemblyName);
        return names;
    }

    private static bool IsFrameworkAssembly(string assemblyName)
    {
        if (assemblyName.StartsWith("System", StringComparison.Ordinal)) return true;
        if (assemblyName.StartsWith("UnityEngine", StringComparison.Ordinal)) return true;
        if (assemblyName.StartsWith("Unity.", StringComparison.Ordinal)) return true;
        if (assemblyName.StartsWith("Harmony", StringComparison.Ordinal)) return true;
        return assemblyName is "mscorlib" or "netstandard" or "BepInEx" or "BepInEx.Core" or "0Harmony";
    }
}
