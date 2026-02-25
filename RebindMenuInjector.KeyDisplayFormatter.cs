namespace Rebind;

internal static partial class RebindMenuInjector
{
    private static class KeyDisplayFormatter
    {
        public static string ToDisplayText(KeyCode key)
        {
            if (key == KeyCode.None) return "-";
            var path = ToKeyboardPath(key);
            if (!string.IsNullOrEmpty(path))
            {
                var human = InputControlPath.ToHumanReadableString(
                    path,
                    InputControlPath.HumanReadableStringOptions.OmitDevice);
                if (!string.IsNullOrWhiteSpace(human)) return human;
            }

            if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9)
            {
                return ((int)(key - KeyCode.Alpha0)).ToString();
            }

            return key.ToString();
        }

        public static string ToDisplayText(KeyboardShortcut shortcut)
        {
            if (shortcut.MainKey == KeyCode.None) return "-";

            var keys = new List<KeyCode>();
            keys.AddRange(shortcut.Modifiers.Where(k => k != KeyCode.None));
            keys.Add(shortcut.MainKey);

            if (keys.Count == 0) return "-";
            return string.Join(" + ", keys.Select(ToDisplayText));
        }

        private static string ToKeyboardPath(KeyCode key)
        {
            if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9)
            {
                var digit = (int)(key - KeyCode.Alpha0);
                return $"<Keyboard>/{digit}";
            }

            if (key >= KeyCode.A && key <= KeyCode.Z)
            {
                var letter = ((char)('a' + (key - KeyCode.A))).ToString();
                return $"<Keyboard>/{letter}";
            }

            if (key >= KeyCode.Keypad0 && key <= KeyCode.Keypad9)
            {
                var digit = (int)(key - KeyCode.Keypad0);
                return $"<Keyboard>/numpad{digit}";
            }

            if (key >= KeyCode.F1 && key <= KeyCode.F12)
            {
                var fn = (int)(key - KeyCode.F1) + 1;
                return $"<Keyboard>/f{fn}";
            }

            return key switch
            {
                KeyCode.Space => "<Keyboard>/space",
                KeyCode.Return => "<Keyboard>/enter",
                KeyCode.Escape => "<Keyboard>/escape",
                KeyCode.Tab => "<Keyboard>/tab",
                KeyCode.Backspace => "<Keyboard>/backspace",
                KeyCode.LeftShift => "<Keyboard>/leftShift",
                KeyCode.RightShift => "<Keyboard>/rightShift",
                KeyCode.LeftControl => "<Keyboard>/leftCtrl",
                KeyCode.RightControl => "<Keyboard>/rightCtrl",
                KeyCode.LeftAlt => "<Keyboard>/leftAlt",
                KeyCode.RightAlt => "<Keyboard>/rightAlt",
                KeyCode.UpArrow => "<Keyboard>/upArrow",
                KeyCode.DownArrow => "<Keyboard>/downArrow",
                KeyCode.LeftArrow => "<Keyboard>/leftArrow",
                KeyCode.RightArrow => "<Keyboard>/rightArrow",
                KeyCode.Home => "<Keyboard>/home",
                KeyCode.End => "<Keyboard>/end",
                KeyCode.PageUp => "<Keyboard>/pageUp",
                KeyCode.PageDown => "<Keyboard>/pageDown",
                KeyCode.Insert => "<Keyboard>/insert",
                KeyCode.Delete => "<Keyboard>/delete",
                KeyCode.Minus => "<Keyboard>/minus",
                KeyCode.Equals => "<Keyboard>/equals",
                KeyCode.LeftBracket => "<Keyboard>/leftBracket",
                KeyCode.RightBracket => "<Keyboard>/rightBracket",
                KeyCode.Semicolon => "<Keyboard>/semicolon",
                KeyCode.Quote => "<Keyboard>/quote",
                KeyCode.Backslash => "<Keyboard>/backslash",
                KeyCode.Comma => "<Keyboard>/comma",
                KeyCode.Period => "<Keyboard>/period",
                KeyCode.Slash => "<Keyboard>/slash",
                KeyCode.BackQuote => "<Keyboard>/backquote",
                _ => string.Empty
            };
        }

        public static string ToNormalizedToken(KeyCode key)
        {
            return Normalize(ToDisplayText(key));
        }

        public static string ToConflictToken(KeyCode key)
        {
            if (key == KeyCode.None) return string.Empty;
            var path = ToKeyboardPath(key);
            if (!string.IsNullOrWhiteSpace(path))
            {
                return ToConflictTokenFromBindingPath(path);
            }

            return "disp:" + Normalize(ToDisplayText(key));
        }

        public static string ToConflictToken(KeyboardShortcut shortcut)
        {
            if (shortcut.MainKey == KeyCode.None) return string.Empty;
            if (!shortcut.Modifiers.Any())
            {
                return ToConflictToken(shortcut.MainKey);
            }

            var keyTokens = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var key in shortcut.Modifiers.Where(k => k != KeyCode.None).Append(shortcut.MainKey))
            {
                var token = ToKeyTokenPart(key);
                if (string.IsNullOrEmpty(token)) continue;
                if (!seen.Add(token)) continue;
                keyTokens.Add(token);
            }

            if (keyTokens.Count == 0) return string.Empty;
            keyTokens.Sort(StringComparer.Ordinal);
            return "combo:" + string.Join("+", keyTokens);
        }

        private static string ToKeyTokenPart(KeyCode key)
        {
            var path = ToKeyboardPath(key);
            if (!string.IsNullOrWhiteSpace(path))
            {
                return "p:" + NormalizeBindingPath(path);
            }

            var display = ToDisplayText(key);
            if (string.IsNullOrWhiteSpace(display) || string.Equals(display, "-", StringComparison.Ordinal))
            {
                return string.Empty;
            }

            return "d:" + Normalize(display);
        }

        public static string ToConflictTokenFromBindingPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            return "path:" + NormalizeBindingPath(path);
        }

        public static string NormalizeBindingPath(string path)
        {
            return (path ?? string.Empty).Trim().ToLowerInvariant();
        }

        public static string Normalize(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            return new string(text
                .Trim()
                .ToLowerInvariant()
                .Where(c => !char.IsWhiteSpace(c) && c != '-' && c != '_' && c != '/')
                .ToArray());
        }
    }
}
