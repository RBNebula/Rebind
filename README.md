# Rebind

Rebind injects a `MODS` section into the game's keybind menu and lets mods register their own keybind rows.

## What Rebind handles

- UI row injection in the game's keybind menu.
- Reset-to-default behavior.
- Conflict highlighting and conflict prompt.
- Key capture for:
  - `KeyboardShortcut` keybinds.
  - `InputAction` bindings.
- External `Input.GetKey*` suppression while Rebind is actively capturing or showing conflict prompt.

## Integration methods

Use one of these depending on how much control you want.

1. `RegisterManagedKeybind` (recommended for most mods)
2. `RegisterKeybind` (you own persistence)
3. `RegisterInputActionBinding` (Unity Input System actions)
4. Reflection-based registration (no compile-time reference)

---

## 1) Managed Packet Method (Recommended)

`RegisterManagedKeybind` is the simplest integration path.  
You provide a packet with metadata + default + apply callback, and Rebind persists values for you.

### What you supply

- `PluginGuid` (required): your mod GUID, for persistence scope.
- `BindingId` (recommended): stable ID for this bind inside your plugin scope.
- `SectionTitle`: group title shown under `MODS`.
- `KeybindTitle`: row title.
- `DefaultKeybind`: default value.
- `Apply` (required): receives effective keybind and applies it to your runtime field.
- `OnChanged` (optional): callback after save/apply on user changes.

### Example

```csharp
using System;
using BepInEx;
using UnityEngine;

[BepInPlugin("com.example.packetdemo", "Packet Demo", "1.0.0")]
[BepInDependency("com.rebind", BepInDependency.DependencyFlags.SoftDependency)]
public sealed class PacketDemoPlugin : BaseUnityPlugin
{
    private IDisposable? _rebindHandle;
    private KeyboardShortcut _toggleBind = new(KeyCode.G);

    private void Awake()
    {
        _rebindHandle = RebindApi.RegisterManagedKeybind(new ManagedKeybindPacket
        {
            PluginGuid = "com.example.packetdemo",
            BindingId = "toggle_advanced_mode",
            SectionTitle = "Packet Demo",
            KeybindTitle = "Toggle Advanced Mode",
            DefaultKeybind = new KeyboardShortcut(KeyCode.G),
            Apply = bind => _toggleBind = bind,
            OnChanged = bind => Logger.LogInfo($"Packet Demo bind changed: {bind.MainKey}")
        });
    }

    private void Update()
    {
        if (_toggleBind.IsDown())
        {
            Logger.LogInfo("Toggled.");
        }
    }

    private void OnDestroy()
    {
        _rebindHandle?.Dispose();
    }
}
```

### How Rebind saves for you

Rebind stores managed packet keybinds in:

- Config file: `BepInEx/config/com.rebind.managed.cfg`
- Section format: `Managed.<PluginGuid>`
- Key format: `<BindingId>`
- Value format: `MainKey+Modifier1+Modifier2` (example: `G+LeftControl`)

Behavior:

1. On registration, Rebind loads saved value by `PluginGuid + BindingId`.
2. If no valid saved value exists, Rebind uses `DefaultKeybind`.
3. Rebind calls `Apply(effectiveValue)` immediately.
4. When user changes bind in menu, Rebind saves it, applies it, then calls `OnChanged` (if provided).

If `BindingId` is empty, Rebind builds one from `SectionTitle.KeybindTitle`.

---

## 2) Direct `RegisterKeybind` (You Persist)

Use this if you want your mod to own its own config format and saving rules.

### What you supply

- `sectionTitle`
- `keybindTitle`
- `getter` (returns current `KeyboardShortcut`)
- `setter` (applies new `KeyboardShortcut`)
- `defaultKeybind`
- `onChanged` (optional, usually where you save your config)

### Example

```csharp
using System;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

[BepInPlugin("com.example.directdemo", "Direct Demo", "1.0.0")]
[BepInDependency("com.rebind", BepInDependency.DependencyFlags.SoftDependency)]
public sealed class DirectDemoPlugin : BaseUnityPlugin
{
    private IDisposable? _rebindHandle;
    private ConfigEntry<KeyboardShortcut> _toggleBind = null!;

    private void Awake()
    {
        _toggleBind = Config.Bind("Input", "ToggleBind", new KeyboardShortcut(KeyCode.G), "Toggle keybind.");

        _rebindHandle = RebindApi.RegisterKeybind(
            sectionTitle: "Direct Demo",
            keybindTitle: "Toggle Feature",
            getter: () => _toggleBind.Value,
            setter: bind => _toggleBind.Value = bind,
            defaultKeybind: new KeyboardShortcut(KeyCode.G),
            onChanged: _ =>
            {
                try { Config.Save(); }
                catch { }
            });
    }

    private void Update()
    {
        if (_toggleBind.Value.IsDown())
        {
            Logger.LogInfo("Toggled.");
        }
    }

    private void OnDestroy()
    {
        _rebindHandle?.Dispose();
    }
}
```

---

## 3) `RegisterInputActionBinding` (Input System)

Use this when your mod already uses Unity Input System `InputActionReference`.

### Overloads

- With explicit default path:
  - `RegisterInputActionBinding(sectionTitle, keybindTitle, actionRef, bindingIndex, defaultBindingPath, onChanged)`
- Without explicit default path:
  - `RegisterInputActionBinding(sectionTitle, keybindTitle, actionRef, bindingIndex, onChanged)`

### Example

```csharp
_rebindHandle = RebindApi.RegisterInputActionBinding(
    sectionTitle: "My Mod",
    keybindTitle: "Toggle Scanner",
    actionRef: scannerActionReference,
    bindingIndex: 0,
    defaultBindingPath: "<Keyboard>/r",
    onChanged: () =>
    {
        try { SaveInputConfig(); }
        catch { }
    });
```

Notes:

- `defaultBindingPath` is recommended for predictable reset/default-state behavior.
- Path format uses Input System path syntax (example: `<Keyboard>/r`, `<Keyboard>/f8`).

---

## 4) Reflection-Based Integration (No Rebind.dll Reference)

Use this if you want optional runtime integration without compile-time dependency.

### Example (managed packet method via reflection)

```csharp
using System;
using System.Linq;
using System.Reflection;
using BepInEx;
using UnityEngine;

private IDisposable? _rebindHandle;
private KeyboardShortcut _bind = new(KeyCode.G);

private bool TryRegisterWithRebind()
{
    var apiType = AppDomain.CurrentDomain
        .GetAssemblies()
        .Select(a => a.GetType("Rebind.RebindApi", throwOnError: false))
        .FirstOrDefault(t => t != null);
    if (apiType == null) return false;

    var packetType = AppDomain.CurrentDomain
        .GetAssemblies()
        .Select(a => a.GetType("Rebind.ManagedKeybindPacket", throwOnError: false))
        .FirstOrDefault(t => t != null);
    if (packetType == null) return false;

    var packet = Activator.CreateInstance(packetType);
    if (packet == null) return false;

    packetType.GetProperty("PluginGuid")?.SetValue(packet, "com.example.reflectdemo");
    packetType.GetProperty("BindingId")?.SetValue(packet, "toggle_feature");
    packetType.GetProperty("SectionTitle")?.SetValue(packet, "Reflect Demo");
    packetType.GetProperty("KeybindTitle")?.SetValue(packet, "Toggle Feature");
    packetType.GetProperty("DefaultKeybind")?.SetValue(packet, new KeyboardShortcut(KeyCode.G));
    packetType.GetProperty("Apply")?.SetValue(packet, new Action<KeyboardShortcut>(bind => _bind = bind));

    var register = apiType.GetMethod(
        "RegisterManagedKeybind",
        BindingFlags.Public | BindingFlags.Static,
        binder: null,
        types: new[] { packetType },
        modifiers: null);
    if (register == null) return false;

    var handle = register.Invoke(null, new object[] { packet });
    if (handle is IDisposable disposable)
    {
        _rebindHandle = disposable;
        return true;
    }

    return false;
}
```

---

## Runtime input behavior

Rebind suppresses external `Input.GetKey`, `Input.GetKeyDown`, and `Input.GetKeyUp` calls while:

- key capture is active, or
- conflict prompt is visible.

This prevents integrated mods from firing hotkeys while rebinding.

---

## Cleanup

Always dispose returned handles on unload:

```csharp
_rebindHandle?.Dispose();
_rebindHandle = null;
```

---

## Troubleshooting

- Rebind not present:
  - use soft dependency and null-safe integration.
- Nothing appears in menu:
  - verify registration runs after plugin load.
- Managed packet not saving:
  - check `BepInEx/config/com.rebind.managed.cfg`.
  - verify stable `PluginGuid` + `BindingId`.

---

## Attributions

- BepInEx
- Harmony (HarmonyLib)
- Unity APIs (`UnityEngine`, Input System, TMPro)

## License

This project is MIT licensed. Keep `LICENSE` with redistributed copies.
