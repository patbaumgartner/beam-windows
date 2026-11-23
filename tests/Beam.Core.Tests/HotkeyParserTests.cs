using Beam.Core.Input;

namespace Beam.Core.Tests;

/// <summary>B7: the global-hotkey combo parser — pure Core logic, no platform API.</summary>
public class HotkeyParserTests
{
    [Theory]
    [InlineData("Win+Alt+P", HotkeyModifiers.Win | HotkeyModifiers.Alt, 0x50u)]
    [InlineData("win+alt+p", HotkeyModifiers.Win | HotkeyModifiers.Alt, 0x50u)]       // case-insensitive
    [InlineData("Alt+Win+P", HotkeyModifiers.Win | HotkeyModifiers.Alt, 0x50u)]       // order-independent
    [InlineData("Ctrl+Shift+F5", HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x74u)]
    [InlineData("Ctrl+Shift+f5", HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x74u)]
    [InlineData("Super+Space", HotkeyModifiers.Win, 0x20u)]
    [InlineData("Ctrl+Enter", HotkeyModifiers.Control, 0x0Du)]
    [InlineData("Alt+1", HotkeyModifiers.Alt, 0x31u)]
    [InlineData(" Shift + A ", HotkeyModifiers.Shift, 0x41u)]                          // whitespace tolerated
    public void TryParse_ValidCombos(string text, HotkeyModifiers expectedMods, uint expectedVk)
    {
        bool ok = HotkeyParser.TryParse(text, out HotkeyCombo? combo);

        Assert.True(ok);
        Assert.NotNull(combo);
        Assert.Equal(expectedMods, combo!.Modifiers);
        Assert.Equal(expectedVk, combo.VirtualKey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("P")]            // bare key — would hijack globally
    [InlineData("Alt")]          // modifier only, no key
    [InlineData("Win+Alt+")]     // empty key token
    [InlineData("Win+Banana")]   // unknown key
    [InlineData("Foo+P")]        // unknown modifier
    [InlineData("Win+Alt+F99")]  // out-of-range F-key
    [InlineData("Win+P")]        // reserved by Windows (projector flyout) — never registered by Beam
    [InlineData("win+p")]        // reserved, case-insensitive
    [InlineData("Super+P")]      // reserved alias
    [InlineData("Windows+P")]    // reserved alias
    [InlineData("Logo+P")]       // reserved alias
    public void TryParse_InvalidCombos_ReturnFalse(string? text)
    {
        bool ok = HotkeyParser.TryParse(text, out HotkeyCombo? combo);

        Assert.False(ok);
        Assert.Null(combo);
    }

    [Theory]
    [InlineData("Win+Alt+P")]
    [InlineData("Ctrl+Shift+F5")]
    [InlineData("Alt+Space")]
    public void DisplayString_RoundTrips(string text)
    {
        Assert.True(HotkeyParser.TryParse(text, out HotkeyCombo? combo));

        string display = combo!.DisplayString;

        Assert.True(HotkeyParser.TryParse(display, out HotkeyCombo? again));
        Assert.Equal(combo, again);
    }

    [Fact]
    public void DisplayString_DefaultCombo_IsWinAltP()
    {
        Assert.True(HotkeyParser.TryParse(Beam.Core.Settings.BeamSettings.Defaults.Hotkey, out HotkeyCombo? combo));

        Assert.Equal("Win+Alt+P", combo!.DisplayString);
    }

    [Fact]
    public void IsReservedByWindows_OnlyWinAloneWithP()
    {
        Assert.True(HotkeyParser.IsReservedByWindows(new HotkeyCombo(HotkeyModifiers.Win, HotkeyParser.VkP)));
        Assert.False(HotkeyParser.IsReservedByWindows(new HotkeyCombo(HotkeyModifiers.Win | HotkeyModifiers.Alt, HotkeyParser.VkP)));
        Assert.False(HotkeyParser.IsReservedByWindows(new HotkeyCombo(HotkeyModifiers.Win | HotkeyModifiers.Shift, HotkeyParser.VkP)));
        Assert.False(HotkeyParser.IsReservedByWindows(new HotkeyCombo(HotkeyModifiers.Win, 0x51u))); // Win+Q is fine
    }

    [Theory]
    [InlineData("Win+P")]
    [InlineData("Win+Alt+P")] // Beam's default alternative must stay valid
    [InlineData("Ctrl+Win+P")]
    public void TryParse_ReservedCheck_MatchesExpected(string text)
    {
        bool ok = HotkeyParser.TryParse(text, out HotkeyCombo? combo);
        bool reserved = combo is not null && HotkeyParser.IsReservedByWindows(combo);

        Assert.Equal(text == "Win+P", !ok); // exactly Win+P is rejected
        Assert.Equal(text == "Win+P", reserved || !ok);
    }

    [Theory]
    [InlineData(0x50u, "P")]
    [InlineData(0x74u, "F5")]
    [InlineData(0x20u, "Space")]
    [InlineData(0x0Du, "Enter")]
    [InlineData(0x1Bu, "Esc")]
    public void KeyName_MapsKnownVirtualKeys(uint vk, string expected)
    {
        Assert.Equal(expected, HotkeyParser.KeyName(vk));
    }
}
