using System.Globalization;

namespace Beam.Core.Input;

/// <summary>
/// B7: modifier flags for a global hotkey. Values match the Win32 <c>MOD_*</c>
/// constants so the tray can pass them straight to <c>RegisterHotKey</c>.
/// </summary>
[Flags]
public enum HotkeyModifiers : uint
{
    None = 0x0000,
    Alt = 0x0001,       // MOD_ALT
    Control = 0x0002,   // MOD_CONTROL
    Shift = 0x0004,     // MOD_SHIFT
    Win = 0x0008,       // MOD_WIN
}

/// <summary>
/// B7: one parsed global hotkey combo, e.g. Win+Alt+P. Pure data — the tray turns it
/// into a <c>RegisterHotKey</c> call; Core never touches the platform API.
/// </summary>
/// <param name="Modifiers">At least one modifier is required (a bare key would hijack globally).</param>
/// <param name="VirtualKey">Win32 virtual-key code (e.g. 0x50 for P).</param>
public sealed record HotkeyCombo(HotkeyModifiers Modifiers, uint VirtualKey)
{
    /// <summary>Canonical display string, e.g. <c>Win+Alt+P</c> (round-trips through the parser).</summary>
    public string DisplayString
    {
        get
        {
            var parts = new List<string>(5);
            if (Modifiers.HasFlag(HotkeyModifiers.Win))
            {
                parts.Add("Win");
            }

            if (Modifiers.HasFlag(HotkeyModifiers.Control))
            {
                parts.Add("Ctrl");
            }

            if (Modifiers.HasFlag(HotkeyModifiers.Alt))
            {
                parts.Add("Alt");
            }

            if (Modifiers.HasFlag(HotkeyModifiers.Shift))
            {
                parts.Add("Shift");
            }

            parts.Add(HotkeyParser.KeyName(VirtualKey));
            return string.Join("+", parts);
        }
    }
}

/// <summary>
/// B7: parses hotkey combo strings like <c>"Win+Alt+P"</c> into <see cref="HotkeyCombo"/>.
/// Case-insensitive, order-independent, <c>+</c>-separated; the last token is the key,
/// all others are modifiers. A combo must have at least one modifier — a bare key would
/// be registered globally and is rejected.
/// </summary>
public static class HotkeyParser
{
    /// <summary>Parses a combo string. Returns false (and null) for anything invalid.</summary>
    public static bool TryParse(string? text, out HotkeyCombo? combo)
    {
        combo = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string[] tokens = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length < 2)
        {
            return false; // need at least one modifier plus a key
        }

        var modifiers = HotkeyModifiers.None;
        foreach (string token in tokens[..^1])
        {
            switch (token.ToLowerInvariant())
            {
                case "win":
                case "windows":
                case "super":
                case "logo":
                    modifiers |= HotkeyModifiers.Win;
                    break;
                case "ctrl":
                case "control":
                    modifiers |= HotkeyModifiers.Control;
                    break;
                case "alt":
                case "option":
                    modifiers |= HotkeyModifiers.Alt;
                    break;
                case "shift":
                    modifiers |= HotkeyModifiers.Shift;
                    break;
                default:
                    return false; // unknown modifier token
            }
        }

        if (modifiers == HotkeyModifiers.None)
        {
            return false; // bare key — would hijack globally
        }

        if (!TryParseKey(tokens[^1], out uint vk))
        {
            return false;
        }

        combo = new HotkeyCombo(modifiers, vk);
        if (IsReservedByWindows(combo))
        {
            // Win+P is Windows' own projector shortcut (the Win+P flyout) — it stays
            // with Windows. Beam must never register it; use an alternative such as
            // the default Win+Alt+P instead.
            combo = null;
            return false;
        }

        return true;
    }

    /// <summary>Virtual-key code of P — the key of Windows' own projector shortcut.</summary>
    public const uint VkP = 0x50;

    /// <summary>
    /// True when the combo is one Windows owns itself: <c>Win+P</c> opens the projector
    /// flyout (connect to a wireless display), so Beam rejects it at parse time and
    /// never registers it. <c>Win+Alt+P</c> (the default) is Beam's alternative.
    /// </summary>
    public static bool IsReservedByWindows(HotkeyCombo combo) =>
        combo.Modifiers == HotkeyModifiers.Win && combo.VirtualKey == VkP;

    /// <summary>Parses a single key token (letter, digit, F-key, or named key) to a virtual-key code.</summary>
    public static bool TryParseKey(string token, out uint virtualKey)
    {
        virtualKey = 0;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        string t = token.Trim();

        // Single letter A–Z → 0x41–0x5A; single digit 0–9 → 0x30–0x39.
        if (t.Length == 1)
        {
            char c = char.ToUpperInvariant(t[0]);
            if (c is >= 'A' and <= 'Z')
            {
                virtualKey = c;
                return true;
            }

            if (c is >= '0' and <= '9')
            {
                virtualKey = c;
                return true;
            }
        }

        // F1–F24 → 0x70–0x87.
        if (t.StartsWith("F", StringComparison.OrdinalIgnoreCase) && t.Length > 1
            && int.TryParse(t[1..], NumberStyles.None, CultureInfo.InvariantCulture, out int f)
            && f is >= 1 and <= 24)
        {
            virtualKey = 0x70u + (uint)(f - 1);
            return true;
        }

        virtualKey = t.ToLowerInvariant() switch
        {
            "space" or "spacebar" => 0x20,
            "enter" or "return" => 0x0D,
            "tab" => 0x09,
            "esc" or "escape" => 0x1B,
            "backspace" or "back" => 0x08,
            "delete" or "del" => 0x2E,
            "insert" or "ins" => 0x2D,
            "home" => 0x24,
            "end" => 0x23,
            "pageup" or "pgup" => 0x21,
            "pagedown" or "pgdn" => 0x22,
            "up" => 0x26,
            "down" => 0x28,
            "left" => 0x25,
            "right" => 0x27,
            "plus" or "add" => 0xBB,
            "minus" or "subtract" => 0xBD,
            "printscreen" or "prtsc" => 0x2C,
            _ => 0,
        };
        return virtualKey != 0;
    }

    /// <summary>The canonical key name for a virtual-key code (used by <see cref="HotkeyCombo.DisplayString"/>).</summary>
    public static string KeyName(uint virtualKey) => virtualKey switch
    {
        >= 0x41 and <= 0x5A => ((char)virtualKey).ToString(),       // A–Z
        >= 0x30 and <= 0x39 => ((char)virtualKey).ToString(),       // 0–9
        >= 0x70 and <= 0x87 => $"F{virtualKey - 0x70 + 1}",        // F1–F24
        0x20 => "Space",
        0x0D => "Enter",
        0x09 => "Tab",
        0x1B => "Esc",
        0x08 => "Backspace",
        0x2E => "Delete",
        0x2D => "Insert",
        0x24 => "Home",
        0x23 => "End",
        0x21 => "PageUp",
        0x22 => "PageDown",
        0x26 => "Up",
        0x28 => "Down",
        0x25 => "Left",
        0x27 => "Right",
        0xBB => "Plus",
        0xBD => "Minus",
        0x2C => "PrintScreen",
        _ => $"VK_0x{virtualKey:X2}",
    };
}
