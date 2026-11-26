using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace PvPTopPlates;

/// <summary>Colours and shapes of one settings-window theme.</summary>
internal readonly record struct ThemePalette(
    Vector4 WindowBg,
    Vector4 SidebarBg,
    Vector4 CardBg,
    Vector4 CardBorder,
    Vector4 PreviewBg,
    Vector4 Frame,
    Vector4 FrameHover,
    Vector4 FrameActive,
    Vector4 Button,
    Vector4 ButtonHover,
    Vector4 ButtonActive,
    Vector4 Accent,
    Vector4 AccentHover,
    Vector4 AccentText,
    Vector4 Text,
    Vector4 TextDim,
    Vector4 Border,
    Vector4 NavSelected,
    Vector4 NavHover,
    Vector4 Title,
    Vector4 TitleActive,
    Vector4 Good,
    Vector4 Warn,
    Vector4 Bad,
    float Rounding,
    float BorderSize,
    Vector4 GradientTop = default,          // W > 0: vertical window gradient (Crystal), else the plain WindowBg
    Vector4 GradientBottom = default);

/// <summary>
/// Settings window themes: Midnight blue, Black, Light and Crystal (the classic blue Final Fantasy menu with a
/// light frame and gold accents). Pushed around this plugin's window only.
/// </summary>
internal static class Theme
{
    public static readonly string[] NameKeys = { "theme_midnight", "theme_black", "theme_light", "theme_crystal" };

    private static int pushedColors;
    private static int pushedVars;

    public static ThemePalette Current { get; private set; } = Midnight;

    public static ThemePalette Get(int index)
    {
        return index switch
        {
            1 => Black,
            2 => Light,
            3 => Crystal,
            _ => Midnight,
        };
    }

    public static uint U32(Vector4 color) => ImGui.ColorConvertFloat4ToU32(color);

    public static void Push(int index, float scale)
    {
        var p = Current = Get(index);
        pushedColors = 0;
        pushedVars = 0;

        Color(ImGuiCol.Text, p.Text);
        Color(ImGuiCol.TextDisabled, p.TextDim);
        Color(ImGuiCol.WindowBg, p.WindowBg);
        Color(ImGuiCol.ChildBg, new Vector4(0f, 0f, 0f, 0f));
        Color(ImGuiCol.PopupBg, p.CardBg with { W = 0.98f });
        Color(ImGuiCol.Border, p.Border);
        Color(ImGuiCol.BorderShadow, new Vector4(0f, 0f, 0f, 0f));
        Color(ImGuiCol.FrameBg, p.Frame);
        Color(ImGuiCol.FrameBgHovered, p.FrameHover);
        Color(ImGuiCol.FrameBgActive, p.FrameActive);
        Color(ImGuiCol.TitleBg, p.Title);
        Color(ImGuiCol.TitleBgActive, p.TitleActive);
        Color(ImGuiCol.TitleBgCollapsed, p.Title);
        Color(ImGuiCol.ScrollbarBg, new Vector4(0f, 0f, 0f, 0f));
        Color(ImGuiCol.ScrollbarGrab, p.Button);
        Color(ImGuiCol.ScrollbarGrabHovered, p.ButtonHover);
        Color(ImGuiCol.ScrollbarGrabActive, p.ButtonActive);
        Color(ImGuiCol.CheckMark, p.Accent);
        Color(ImGuiCol.SliderGrab, p.Accent with { W = 0.85f });
        Color(ImGuiCol.SliderGrabActive, p.AccentHover);
        Color(ImGuiCol.Button, p.Button);
        Color(ImGuiCol.ButtonHovered, p.ButtonHover);
        Color(ImGuiCol.ButtonActive, p.ButtonActive);
        Color(ImGuiCol.Header, p.NavSelected);
        Color(ImGuiCol.HeaderHovered, p.NavHover);
        Color(ImGuiCol.HeaderActive, p.ButtonActive);
        Color(ImGuiCol.Separator, p.Border);
        Color(ImGuiCol.SeparatorHovered, p.Accent);
        Color(ImGuiCol.SeparatorActive, p.Accent);
        Color(ImGuiCol.ResizeGrip, p.Button);
        Color(ImGuiCol.ResizeGripHovered, p.Accent);
        Color(ImGuiCol.ResizeGripActive, p.AccentHover);
        Color(ImGuiCol.TextSelectedBg, p.Accent with { W = 0.35f });

        Var(ImGuiStyleVar.WindowRounding, p.Rounding * scale);
        Var(ImGuiStyleVar.ChildRounding, p.Rounding * scale);
        Var(ImGuiStyleVar.FrameRounding, (p.Rounding - 1f) * scale);
        Var(ImGuiStyleVar.PopupRounding, p.Rounding * scale);
        Var(ImGuiStyleVar.GrabRounding, (p.Rounding - 1f) * scale);
        Var(ImGuiStyleVar.ScrollbarRounding, p.Rounding * scale);
        Var(ImGuiStyleVar.WindowBorderSize, p.BorderSize);
        Var(ImGuiStyleVar.FrameBorderSize, 0f);
        Var(ImGuiStyleVar.WindowPadding, new Vector2(12f, 10f) * scale);

        // Title bar: ImGui puts the close X FramePadding.X from the right edge, Dalamud puts its window-options button
        // (the bars icon) FontSize + ItemInnerSpacing.X from that edge; the gap between them is ItemInnerSpacing.X -
        // FramePadding.X. Buttons 12 px apart, 8 px before the X. ConfigWindow.Draw pushes the normal values for the content.
        Var(ImGuiStyleVar.FramePadding, new Vector2(4f, 5f) * scale);
        Var(ImGuiStyleVar.ItemInnerSpacing, new Vector2(12f, 4f) * scale);
        Var(ImGuiStyleVar.ItemSpacing, new Vector2(8f, 6f) * scale);
        Var(ImGuiStyleVar.ScrollbarSize, 10f * scale);
    }

    public static void Pop()
    {
        if (pushedVars > 0)
            ImGui.PopStyleVar(pushedVars);
        if (pushedColors > 0)
            ImGui.PopStyleColor(pushedColors);
        pushedVars = 0;
        pushedColors = 0;
    }

    private static void Color(ImGuiCol slot, Vector4 color)
    {
        ImGui.PushStyleColor(slot, color);
        pushedColors++;
    }

    private static void Var(ImGuiStyleVar slot, float value)
    {
        ImGui.PushStyleVar(slot, value);
        pushedVars++;
    }

    private static void Var(ImGuiStyleVar slot, Vector2 value)
    {
        ImGui.PushStyleVar(slot, value);
        pushedVars++;
    }

    private static Vector4 Hex(uint rgb, float alpha = 1f)
    {
        return new Vector4(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, alpha);
    }

    private static readonly ThemePalette Midnight = new(
        WindowBg: Hex(0x131B2B, 0.98f),
        SidebarBg: Hex(0x0E1522),
        CardBg: Hex(0x1A2438),
        CardBorder: Hex(0x27354F),
        PreviewBg: Hex(0x0C1320),
        Frame: Hex(0x0E1726),
        FrameHover: Hex(0x1C2A44),
        FrameActive: Hex(0x24365A),
        Button: Hex(0x22314D),
        ButtonHover: Hex(0x2C3F63),
        ButtonActive: Hex(0x36507D),
        Accent: Hex(0xE5AE48),
        AccentHover: Hex(0xF1C065),
        AccentText: Hex(0x1C1405),
        Text: Hex(0xDCE3EF),
        TextDim: Hex(0x8391AB),
        Border: Hex(0x2A3A58),
        NavSelected: Hex(0x22314D),
        NavHover: Hex(0x18243A),
        Title: Hex(0x0C121D),
        TitleActive: Hex(0x111A2B),
        Good: Hex(0x5FD08A),
        Warn: Hex(0xE5AE48),
        Bad: Hex(0xE0555A),
        Rounding: 6f,
        BorderSize: 1f);

    private static readonly ThemePalette Black = new(
        WindowBg: Hex(0x0D0D0F, 0.98f),
        SidebarBg: Hex(0x08080A),
        CardBg: Hex(0x161619),
        CardBorder: Hex(0x27272C),
        PreviewBg: Hex(0x070708),
        Frame: Hex(0x1C1C21),
        FrameHover: Hex(0x26262D),
        FrameActive: Hex(0x313139),
        Button: Hex(0x232328),
        ButtonHover: Hex(0x2E2E35),
        ButtonActive: Hex(0x3A3A43),
        Accent: Hex(0xD8DCE4),
        AccentHover: Hex(0xFFFFFF),
        AccentText: Hex(0x0D0D0F),
        Text: Hex(0xE6E6E9),
        TextDim: Hex(0x7E7E86),
        Border: Hex(0x2A2A30),
        NavSelected: Hex(0x232328),
        NavHover: Hex(0x18181C),
        Title: Hex(0x070708),
        TitleActive: Hex(0x0D0D0F),
        Good: Hex(0x6FD08F),
        Warn: Hex(0xE0B456),
        Bad: Hex(0xE05A5F),
        Rounding: 5f,
        BorderSize: 1f);

    private static readonly ThemePalette Light = new(
        WindowBg: Hex(0xF2F4F8, 0.99f),
        SidebarBg: Hex(0xE6EAF1),
        CardBg: Hex(0xFFFFFF),
        CardBorder: Hex(0xD3D9E3),
        PreviewBg: Hex(0x2B3446),
        Frame: Hex(0xE4E8EF),
        FrameHover: Hex(0xD9DFE9),
        FrameActive: Hex(0xCBD3E0),
        Button: Hex(0xDDE3EC),
        ButtonHover: Hex(0xD0D8E4),
        ButtonActive: Hex(0xC0CADA),
        Accent: Hex(0x2F6FD6),
        AccentHover: Hex(0x4583E6),
        AccentText: Hex(0xFFFFFF),
        Text: Hex(0x1E2430),
        TextDim: Hex(0x667086),
        Border: Hex(0xC9D0DC),
        NavSelected: Hex(0xD3DDEE),
        NavHover: Hex(0xDDE3EE),
        Title: Hex(0xDCE1EA),
        TitleActive: Hex(0xCBD3E1),
        Good: Hex(0x2E9E5B),
        Warn: Hex(0xC98A1B),
        Bad: Hex(0xC9363C),
        Rounding: 6f,
        BorderSize: 1f);

    // classic blue menu window: gradient from a lighter top to a deep bottom, panels let it show through
    private static readonly ThemePalette Crystal = new(
        WindowBg: Hex(0x0C1452, 0.97f),
        SidebarBg: Hex(0x0A1150, 0.55f),
        CardBg: Hex(0x1C2B8A, 0.55f),
        CardBorder: Hex(0xC9D3FF, 0.85f),
        PreviewBg: Hex(0x0D1656),
        Frame: Hex(0x0F1A5E),
        FrameHover: Hex(0x2537A6),
        FrameActive: Hex(0x2E45C2),
        Button: Hex(0x24369F),
        ButtonHover: Hex(0x2F45C0),
        ButtonActive: Hex(0x3A55DA),
        Accent: Hex(0xF2D27A),
        AccentHover: Hex(0xFFE39A),
        AccentText: Hex(0x1A1440),
        Text: Hex(0xFFFFFF),
        TextDim: Hex(0xAEB9EC),
        Border: Hex(0xD5DDFF, 0.9f),
        NavSelected: Hex(0x2A3DB3),
        NavHover: Hex(0x1E2E95),
        Title: Hex(0x0E1656),
        TitleActive: Hex(0x14207A),
        Good: Hex(0x7CF0A6),
        Warn: Hex(0xF2D27A),
        Bad: Hex(0xFF7B7B),
        Rounding: 3f,
        BorderSize: 1.5f,
        GradientTop: Hex(0x2A3DB8, 0.97f),
        GradientBottom: Hex(0x0C1452, 0.97f));
}
