# Rebind

Injects a `MODS` section into the game's keybind menu and lets mods register keybind rows with one call.

## Features

- Uses the game's existing keybind menu styling.
- Supports grouped mod sections under `MODS`.
- Conflict detection:
  - conflicting rows are highlighted red
  - conflict popup appears with `Continue` / `Reset`
- Supports `KeyboardShortcut` and `InputAction` registration.
- Reset button visibility tracks whether a binding is currently at its default.

## API

### `RegisterKeybind` (single key or shortcut)

This is the primary keybind registration call:

```csharp
private KeyboardShortcut _toggleBind = new(KeyCode.G);
private static readonly KeyboardShortcut DefaultToggleBind = new(KeyCode.G);

var handle = RebindApi.RegisterKeybind(
    sectionTitle: "My Mod",
    keybindTitle: "Toggle Advanced Mode",
    getter: () => _toggleBind,
    setter: bind => _toggleBind = bind,
    defaultKeybind: DefaultToggleBind,
    onChanged: _ => SaveSettings());
```

- Capture supports both:
  - single key (example: `G`)
  - modifier shortcuts (example: `Ctrl + G`, `Ctrl + A + B`)
- Runtime usage:

```csharp
if (_toggleBind.IsDown())
{
    // ...
}
```

- Hold usage:

```csharp
if (_toggleBind.IsPressed())
{
    // runs every frame while held
}
```

- `sectionTitle`: Group title shown under `MODS`.
- `keybindTitle`: Row title.
- `getter`: Returns the current shortcut.
- `setter`: Applies the new shortcut.
- `defaultKeybind`: Used by row reset button.
- `onChanged`: Optional callback for side effects (save, toast, etc).

### `InputAction` registration (explicit default recommended)

```csharp
var handle = RebindApi.RegisterInputActionBinding(
    sectionTitle: "My Mod",
    keybindTitle: "Toggle Scanner",
    actionRef: myActionReference,
    bindingIndex: 0,
    defaultBindingPath: "<Keyboard>/r",
    onChanged: () => SaveSettings());
```

- `defaultBindingPath` is used by reset and default-state detection.
- Use Input System path syntax (example: `<Keyboard>/r`, `<Keyboard>/f8`).

### `InputAction` registration (overload without explicit default)

```csharp
var handle = RebindApi.RegisterInputActionBinding(
    sectionTitle: "My Mod",
    keybindTitle: "Toggle Scanner",
    actionRef: myActionReference,
    bindingIndex: 0,
    onChanged: () => SaveSettings());
```

- `actionRef`: Unity Input System action reference.
- `bindingIndex`: Which binding slot of that action to rebind/reset.
- `onChanged`: Optional callback after apply/reset.
- Without `defaultBindingPath`, reset falls back to the action's base binding / no override.

## Integration Notes

- Add soft dependency if your mod can use Rebind when present:

```csharp
[BepInDependency("com.rebind", BepInDependency.DependencyFlags.SoftDependency)]
```

- If load order may vary, retry registration until Rebind is available.
- Keep returned handles and dispose on unload.

### Runtime Input Gating

Rebind handles keybind registration and keybind menu UI.  
Your mod still controls runtime input polling in `Update()`.

If your mod uses its own hotkey gating system, keep applying it before your input checks.

## Cleanup

Dispose returned handle(s) when your mod unloads to remove rows:

```csharp
handle.Dispose();
```

## Complete Demo: Single Key + Shortcut Keybinds

This example keeps it minimal:
- registers two keybinds in `MODS`
  - one single key (`T`)
  - one shortcut (`LeftControl + Y`)
- toggles two independent features
- logs state changes

```csharp
using System;
using System.Collections.Generic;
using BepInEx;
using UnityEngine;

[BepInPlugin("com.example.toggledemo", "Toggle Demo", "1.0.0")]
[BepInDependency("com.rebind", BepInDependency.DependencyFlags.SoftDependency)]
public sealed class ToggleDemo : BaseUnityPlugin
{
    private readonly List<IDisposable> _rebindHandles = new();
    private KeyboardShortcut _singleKeyBind = new(KeyCode.T);
    private static readonly KeyboardShortcut DefaultSingleKeyBind = new(KeyCode.T);
    private KeyboardShortcut _shortcutBind = new(KeyCode.Y, KeyCode.LeftControl);
    private static readonly KeyboardShortcut DefaultShortcutBind = new(KeyCode.Y, KeyCode.LeftControl);

    private bool _singleKeyFeatureEnabled;
    private bool _shortcutFeatureEnabled;

    private void Awake()
    {
        _rebindHandles.Add(RebindApi.RegisterKeybind(
            sectionTitle: "Toggle Demo",
            keybindTitle: "Toggle Feature (Single Key)",
            getter: () => _singleKeyBind,
            setter: bind => _singleKeyBind = bind,
            defaultKeybind: DefaultSingleKeyBind));

        _rebindHandles.Add(RebindApi.RegisterKeybind(
            sectionTitle: "Toggle Demo",
            keybindTitle: "Toggle Feature (Shortcut)",
            getter: () => _shortcutBind,
            setter: bind => _shortcutBind = bind,
            defaultKeybind: DefaultShortcutBind));
    }

    private void Update()
    {
        if (_singleKeyBind.IsDown())
        {
            _singleKeyFeatureEnabled = !_singleKeyFeatureEnabled;
            Logger.LogInfo($"Single-key feature enabled: {_singleKeyFeatureEnabled}");
        }

        if (_shortcutBind.IsDown())
        {
            _shortcutFeatureEnabled = !_shortcutFeatureEnabled;
            Logger.LogInfo($"Shortcut feature enabled: {_shortcutFeatureEnabled}");
        }
    }

    private void OnDestroy()
    {
        for (var i = 0; i < _rebindHandles.Count; i++)
        {
            try { _rebindHandles[i].Dispose(); }
            catch { }
        }
        _rebindHandles.Clear();
    }
}
```

Note:
- If you do not reference `Rebind.dll` at compile time, use reflection-based registration instead.

## Attributions

### External Libraries
- **BepInEx** - plugin framework used by this mod.
- **Harmony (HarmonyLib)** - runtime patching library used for method patching/injection.
- **Unity APIs** (`UnityEngine`, `Unity Input System`, `TMPro`) - used through the game's runtime/API surface.

## License Notice (MIT)

This project is distributed under the MIT License.

MIT requires that distributed copies/substantial portions include:
- the copyright notice
- the permission notice (license text)

Keep the `LICENSE` file with redistributed copies.
