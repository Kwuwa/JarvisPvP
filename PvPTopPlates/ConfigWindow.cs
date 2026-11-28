using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.GameFonts;
using Dalamud;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using static PvPTopPlates.Loc;
using static PvPTopPlates.Theme;

namespace PvPTopPlates;

/// <summary>
/// "Jarvis PvP Settings": header with the logo and the page subtitle, a sidebar with the pages, a preview of you,
/// your target and an ally pinned above the settings, and the settings of each page in titled cards.
/// Every text comes from <see cref="Loc"/>; widget ids stay stable across languages; drop-down lists switch with
/// Ctrl + mouse wheel; the window is drawn in the chosen <see cref="Theme"/>.
/// </summary>
internal sealed class ConfigWindow : Window, IDisposable
{
    private const string BoostyUrl = "https://boosty.to/aloyb";
    private const string HipolinkUrl = "https://hipolink.net/kwuwa";
    private const string GitHubUrl = "https://github.com/Kwuwa/JarvisPvP";
    private const string NewIssueUrl = GitHubUrl + "/issues/new?title=%5BBug%5D%20";
    private const ImGuiWindowFlags BaseFlags =
        ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;

    private static readonly (string Key, FontAwesomeIcon Icon)[] Pages =
    {
        ("tab_preview", FontAwesomeIcon.Eye),
        ("tab_general", FontAwesomeIcon.SlidersH),
        ("tab_bars", FontAwesomeIcon.Heartbeat),
        ("tab_text", FontAwesomeIcon.Font),
        ("tab_icons", FontAwesomeIcon.ShieldAlt),
        ("tab_debuffs", FontAwesomeIcon.Skull),
        ("tab_targeting", FontAwesomeIcon.Crosshairs),
        ("tab_appearance", FontAwesomeIcon.Palette),
        ("tab_changelog", FontAwesomeIcon.History),
        ("tab_diagnostics", FontAwesomeIcon.Bug),
        ("tab_about", FontAwesomeIcon.InfoCircle),
    };

    private readonly Configuration configuration;
    private readonly OverlayRenderer renderer;
    private readonly IFontHandle titleFont;
    private readonly IFontHandle valueFont;
    private readonly IFontHandle windowFont;
    private readonly IFontHandle russianTitleFont;
    private readonly IFontHandle russianValueFont;
    private IDisposable? pushedWindowFont;
    private Configuration? beforeImport;
    private readonly HashSet<long> selectedLines = new();
    private ImFontPtr iconFont;
    private bool hasIconFont;
    private int page;
    private bool changed;
    private bool savePending;
    private bool scrollToTop;
    private Vector2 cardStart;
    private float cardWidth;
    private int logFilter;
    private string logSearch = string.Empty;
    private bool autoScroll = true;
    private long lastLogId;
    private string toast = string.Empty;
    private string toastPlace = string.Empty;
    private bool toastBad;
    private long toastUntil;
    private long resetArmedUntil;
    private float pairColumn;
    private float pairColumnNext;
    private readonly TitleBarButton collapseButton;
    private int lastContentFrame = -10;
    private int collapseRequestFrame = -10;
    private ImGuiDir savedMenuButton;

    public ConfigWindow(Configuration configuration, OverlayRenderer renderer)
        : base("Jarvis PvP Settings###JarvisPvPSettings", BaseFlags)
    {
        this.configuration = configuration;
        this.renderer = renderer;
        Size = new Vector2(960f, 740f);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(760f, 520f),
            MaximumSize = new Vector2(1_800f, 1_400f),
        };

        titleFont = Plugin.PluginInterface.UiBuilder.FontAtlas.NewGameFontHandle(
            new GameFontStyle(GameFontFamilyAndSize.TrumpGothic23));
        valueFont = Plugin.PluginInterface.UiBuilder.FontAtlas.NewGameFontHandle(
            new GameFontStyle(GameFontFamilyAndSize.Axis18));

        // The game fonts have no Cyrillic: the window uses Dalamud's default font with Cyrillic merged in from the
        // bundled Noto Sans CJK, and the Russian header / tile values use Noto at the game fonts' sizes.
        windowFont = Plugin.PluginInterface.UiBuilder.FontAtlas.NewDelegateFontHandle(e => e.OnPreBuild(tk =>
        {
            var font = tk.AddDalamudDefaultFont(-1, null);
            var size = Plugin.PluginInterface.UiBuilder.DefaultFontSpec.SizePx;
            tk.AddDalamudAssetFont(DalamudAsset.NotoSansCjkMedium, new SafeFontConfig
            {
                SizePx = size,
                GlyphRanges = CyrillicRanges,
                MergeFont = font,
            });
            tk.AttachExtraGlyphsForDalamudLanguage(new SafeFontConfig { SizePx = size, MergeFont = font });
        }));
        russianTitleFont = NotoFont(22f);
        russianValueFont = NotoFont(17f);

        // Collapse / expand as a title bar button next to the window-options button and the close X, so all three
        // sit together in the top right corner. ImGui's own arrow is hidden (WindowMenuButtonPosition None while this
        // window begins); NoCollapse cannot be used, it keeps ImGui windows expanded.
        collapseButton = new TitleBarButton
        {
            Icon = FontAwesomeIcon.ChevronUp,
            IconOffset = new Vector2(1.5f, 1f),
            Priority = 0,
            AvailableClickthrough = true,
            Click = _ =>
            {
                var collapsed = lastContentFrame < ImGui.GetFrameCount() - 1;
                Collapsed = !collapsed;
                CollapsedCondition = ImGuiCond.Always;
                collapseRequestFrame = ImGui.GetFrameCount();
            },
        };
        TitleBarButtons.Add(collapseButton);
    }

    private static readonly ushort[] CyrillicRanges = { 0x0400, 0x052F, 0x2010, 0x205E, 0 };
    private static readonly ushort[] LatinCyrillicRanges = { 0x0020, 0x00FF, 0x0400, 0x052F, 0x2010, 0x205E, 0 };

    private static IFontHandle NotoFont(float sizePx)
    {
        return Plugin.PluginInterface.UiBuilder.FontAtlas.NewDelegateFontHandle(e => e.OnPreBuild(tk =>
            tk.AddDalamudAssetFont(DalamudAsset.NotoSansCjkMedium, new SafeFontConfig { SizePx = sizePx, GlyphRanges = LatinCyrillicRanges })));
    }

    private static bool RussianUi => Loc.Index == 4;

    public void Dispose()
    {
        SaveIfPending();
        titleFont.Dispose();
        valueFont.Dispose();
        windowFont.Dispose();
        russianTitleFont.Dispose();
        russianValueFont.Dispose();
    }

    public override void PreDraw()
    {
        WindowName = $"{T("window_title")}###JarvisPvPSettings";
        collapseButton.Icon = lastContentFrame < ImGui.GetFrameCount() - 1 ? FontAwesomeIcon.ChevronDown : FontAwesomeIcon.ChevronUp;
        Push(configuration.Theme, ImGuiHelpers.GlobalScale);
        pushedWindowFont = windowFont.Available ? windowFont.Push() : null;
        var style = ImGui.GetStyle();
        savedMenuButton = style.WindowMenuButtonPosition;
        style.WindowMenuButtonPosition = ImGuiDir.None;
    }

    public override void PostDraw()
    {
        // the collapse request is applied once (next frame), then the window is free again
        if (Collapsed.HasValue && ImGui.GetFrameCount() > collapseRequestFrame)
            Collapsed = null;
        ImGui.GetStyle().WindowMenuButtonPosition = savedMenuButton;
        pushedWindowFont?.Dispose();
        pushedWindowFont = null;
        Pop();
    }

    public override void OnClose()
    {
        OverlayRenderer.TestInWorld = false;
        SaveIfPending();
    }

    private void SaveIfPending()
    {
        if (!savePending)
            return;
        savePending = false;
        configuration.Save();
    }

    public override void Draw()
    {
        changed = false;
        lastContentFrame = ImGui.GetFrameCount();
        var s = ImGuiHelpers.GlobalScale;
        pairColumn = pairColumnNext;
        pairColumnNext = 0f;

        // the theme's frame values space out the title bar buttons; the content uses its own (popped at the end)
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(8f, 4f) * s);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemInnerSpacing, new Vector2(4f, 4f) * s);
        try
        {
            DrawContent(s);
        }
        finally
        {
            ImGui.PopStyleVar(2);
        }

        // the plates read the settings live; the file is written once a slider or colour picker is let go
        if (changed)
            savePending = true;
        if (savePending && !ImGui.IsAnyItemActive())
            SaveIfPending();
    }

    private void DrawContent(float s)
    {
        FetchIconFont();
        DrawWindowGradient(s);
        DrawTopBar(s);

        var available = ImGui.GetContentRegionAvail();
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Current.SidebarBg);
        ImGui.BeginChild("##sidebar", new Vector2(SidebarWidth(s), available.Y), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        ImGui.PopStyleColor();
        DrawSidebar(s);
        ImGui.EndChild();

        ImGui.SameLine(0f, 10f * s);
        ImGui.BeginGroup();
        // up to 360 px tall (about 45 % of the window height), never under 200 px
        if (page == 0 || (page is >= 1 and <= 6 && configuration.PinPreview))
            DrawPreviewBox(s, MathF.Round(Math.Clamp(ImGui.GetContentRegionAvail().Y * 0.45f, 200f * s, 360f * s)));

        // while Ctrl is held the mouse wheel belongs to the drop-down under the cursor
        var contentFlags = ImGui.GetIO().KeyCtrl ? ImGuiWindowFlags.NoScrollWithMouse : ImGuiWindowFlags.None;
        ImGui.BeginChild("##content", new Vector2(0f, ImGui.GetContentRegionAvail().Y), false, contentFlags);
        if (scrollToTop)
        {
            ImGui.SetScrollY(0f);
            scrollToTop = false;
        }

        DrawPage(s);
        ImGui.Dummy(new Vector2(0f, 6f * s));
        ImGui.EndChild();
        ImGui.EndGroup();
    }

    // ------------------------------------------------------------------ frame

    private void FetchIconFont()
    {
        hasIconFont = false;
        var handle = Plugin.PluginInterface.UiBuilder.IconFontHandle;
        if (handle is { Available: true })
        {
            using (handle.Push())
                iconFont = ImGui.GetFont();
            hasIconFont = true;
        }
    }

    /// <summary>Themes with a gradient (Crystal) paint it under everything, from below the title bar.</summary>
    private static void DrawWindowGradient(float s)
    {
        var palette = Current;
        if (palette.GradientTop.W <= 0f)
            return;

        var windowPosition = ImGui.GetWindowPos();
        var windowSize = ImGui.GetWindowSize();
        var top = ImGui.GetCursorScreenPos().Y - ImGui.GetStyle().WindowPadding.Y;
        var bottom = windowPosition.Y + windowSize.Y - (palette.Rounding * s);     // the rounded bottom keeps WindowBg
        var upper = U32(palette.GradientTop);
        var lower = U32(palette.GradientBottom);
        ImGui.GetWindowDrawList().AddRectFilledMultiColor(
            new Vector2(windowPosition.X + palette.BorderSize, top),
            new Vector2(windowPosition.X + windowSize.X - palette.BorderSize, bottom),
            upper,
            upper,
            lower,
            lower);
    }

    private void Icon(ImDrawListPtr drawList, Vector2 position, float size, uint color, FontAwesomeIcon icon)
    {
        if (hasIconFont)
            drawList.AddText(iconFont, size, position, color, icon.ToIconString());
    }

    private void DrawTopBar(float s)
    {
        var drawList = ImGui.GetWindowDrawList();
        var palette = Current;
        var start = ImGui.GetCursorScreenPos();
        var logo = 42f * s;
        var right = start.X + ImGui.GetContentRegionAvail().X;

        drawList.AddRectFilled(start, start + new Vector2(logo), U32(palette.CardBg), palette.Rounding * s);
        drawList.AddRect(start, start + new Vector2(logo), U32(palette.CardBorder), palette.Rounding * s, ImDrawFlags.None, palette.BorderSize);
        Glyphs.CrossedSwords(drawList, start + new Vector2(logo * 0.5f), logo * 0.56f, U32(palette.Accent), U32(new Vector4(0f, 0f, 0f, 0.55f)), s);

        // the swords logo with the "Jarvis PvP Settings" inscription
        var textX = start.X + logo + (12f * s);
        var title = T("window_title");
        ImGui.SetCursorScreenPos(new Vector2(textX, start.Y));
        var headerFont = RussianUi ? russianTitleFont : titleFont;
        if (headerFont.Available)
        {
            using (headerFont.Push())
                ImGui.TextColored(palette.Text, title);
        }
        else
        {
            ImGui.TextColored(palette.Text, title);
        }

        // version badge after the name
        var titleMin = ImGui.GetItemRectMin();
        var titleMax = ImGui.GetItemRectMax();
        var version = "v" + VersionText();
        var badgePadding = new Vector2(6f * s, 1f * s);
        var badgeSize = ImGui.CalcTextSize(version) + (badgePadding * 2f);
        var badgeMin = new Vector2(
            MathF.Round(titleMax.X + (8f * s)),
            MathF.Round(titleMin.Y + ((titleMax.Y - titleMin.Y - badgeSize.Y) * 0.5f)));
        drawList.AddRectFilled(badgeMin, badgeMin + badgeSize, U32(palette.Accent with { W = 0.16f }), badgeSize.Y * 0.5f);
        drawList.AddText(badgeMin + badgePadding, U32(palette.Accent), version);

        ImGui.SetCursorScreenPos(new Vector2(textX, start.Y + logo - ImGui.GetTextLineHeight()));
        ImGui.TextColored(palette.TextDim, T(Pages[page].Key.Replace("tab_", "sub_")));

        var label = T("test_in_world");
        var buttonWidth = ImGui.CalcTextSize(label).X + (28f * s);
        ImGui.SetCursorScreenPos(new Vector2(right - buttonWidth, start.Y + ((logo - ImGui.GetFrameHeight()) * 0.5f)));
        if (ToggleButton($"{label}###test_in_world", OverlayRenderer.TestInWorld, new Vector2(buttonWidth, 0f)))
            OverlayRenderer.TestInWorld = !OverlayRenderer.TestInWorld;
        HoverHelp("test_in_world_help");

        ImGui.SetCursorScreenPos(new Vector2(start.X, start.Y + logo + (8f * s)));
        var lineY = ImGui.GetCursorScreenPos().Y;
        drawList.AddLine(new Vector2(start.X, lineY), new Vector2(right, lineY), U32(palette.Border), 1f);
        ImGui.Dummy(new Vector2(0f, 4f * s));
    }

    private void DrawSidebar(float s)
    {
        var drawList = ImGui.GetWindowDrawList();
        var palette = Current;
        ImGui.Dummy(new Vector2(0f, 2f * s));
        for (var i = 0; i < Pages.Length; i++)
        {
            if (i == 7)
            {
                ImGui.Dummy(new Vector2(0f, 3f * s));
                var position = ImGui.GetCursorScreenPos();
                drawList.AddLine(
                    position + new Vector2(8f * s, 0f),
                    position + new Vector2(ImGui.GetContentRegionAvail().X - (8f * s), 0f),
                    U32(palette.Border),
                    1f);
                ImGui.Dummy(new Vector2(0f, 4f * s));
            }

            NavItem(drawList, i, s);
        }

        // status pill at the bottom
        var (text, color) = StatusText();
        var pillHeight = 28f * s;
        var minimum = new Vector2(
            ImGui.GetWindowPos().X + (8f * s),
            ImGui.GetWindowPos().Y + ImGui.GetWindowHeight() - pillHeight - (10f * s));
        var maximum = minimum + new Vector2(ImGui.GetWindowWidth() - (16f * s), pillHeight);
        drawList.AddRectFilled(minimum, maximum, U32(color with { W = 0.14f }), pillHeight * 0.5f);
        drawList.AddRect(minimum, maximum, U32(color with { W = 0.45f }), pillHeight * 0.5f, ImDrawFlags.None, 1f);
        drawList.AddCircleFilled(minimum + new Vector2(15f * s, pillHeight * 0.5f), 4f * s, U32(color));
        drawList.AddText(minimum + new Vector2(27f * s, (pillHeight - ImGui.GetFontSize()) * 0.5f), U32(color), text);
        if (ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(minimum, maximum))
            Tooltip(StatusHelp());
    }

    /// <summary>Wide enough for the longest page name and the status of the chosen language.</summary>
    private float SidebarWidth(float s)
    {
        var width = ImGui.CalcTextSize(StatusText().Text).X + (55f * s);
        foreach (var (key, _) in Pages)
            width = Math.Max(width, ImGui.CalcTextSize(T(key)).X + (52f * s));
        return MathF.Round(Math.Max(170f * s, width));
    }

    private string StatusHelp()
    {
        if (!configuration.Enabled)
            return T("status_paused");
        if (Plugin.ClientState.IsPvP)
            return T("status_pvp");
        return T(configuration.DrawOutsidePvPForPositioning ? "status_positioning" : "status_hidden");
    }

    private void NavItem(ImDrawListPtr drawList, int index, float s)
    {
        var palette = Current;
        var start = ImGui.GetCursorScreenPos();
        var size = new Vector2(ImGui.GetContentRegionAvail().X, 30f * s);
        if (ImGui.InvisibleButton($"##page{index}", size) && page != index)
        {
            page = index;
            scrollToTop = true;
        }

        var hovered = ImGui.IsItemHovered();
        var selected = page == index;
        if (selected || hovered)
            drawList.AddRectFilled(start, start + size, U32(selected ? palette.NavSelected : palette.NavHover), 5f * s);
        if (selected)
            drawList.AddRectFilled(start + new Vector2(0f, 6f * s), start + new Vector2(3f * s, size.Y - (6f * s)), U32(palette.Accent), 2f * s);

        var fontSize = ImGui.GetFontSize();
        var y = MathF.Round((size.Y - fontSize) * 0.5f);
        drawList.PushClipRect(start, start + size, true);
        Icon(drawList, start + new Vector2(12f * s, y), fontSize, U32(selected ? palette.Accent : palette.TextDim), Pages[index].Icon);
        drawList.AddText(start + new Vector2(36f * s, y), U32(selected ? palette.Text : palette.TextDim), T(Pages[index].Key));
        drawList.PopClipRect();
    }

    private (string Text, Vector4 Color) StatusText()
    {
        var palette = Current;
        if (!configuration.Enabled)
            return (T("st_paused"), palette.Bad);
        if (!Plugin.ClientState.IsPvP && !configuration.DrawOutsidePvPForPositioning)
            return (T("st_waiting"), palette.Warn);
        return (T("st_active"), palette.Good);
    }

    private void DrawPreviewBox(float s, float height)
    {
        var palette = Current;
        var drawList = ImGui.GetWindowDrawList();
        var minimum = ImGui.GetCursorScreenPos();
        var size = new Vector2(ImGui.GetContentRegionAvail().X, height);
        ImGui.Dummy(size);
        var maximum = minimum + size;
        drawList.AddRectFilled(minimum, maximum, U32(palette.PreviewBg), palette.Rounding * s);

        // the Preview page shows everything; each settings page shows only what it sets
        renderer.DrawPreviewScene(drawList, minimum, maximum, s, (OverlayRenderer.PreviewFocus)Math.Clamp(page, 0, 6));
        drawList.AddRect(minimum, maximum, U32(palette.CardBorder), palette.Rounding * s, ImDrawFlags.None, palette.BorderSize);
        ImGui.Dummy(new Vector2(0f, 2f * s));
    }

    // ------------------------------------------------------------------ pages

    private void DrawPage(float s)
    {
        switch (page)
        {
            case 0:
                DrawPreviewPage(s);
                break;
            case 1:
                DrawGeneralPage();
                break;
            case 2:
                DrawBarsPage();
                break;
            case 3:
                DrawTextPage();
                break;
            case 4:
                DrawIconsPage();
                break;
            case 5:
                DrawDebuffsPage();
                break;
            case 6:
                DrawTargetingPage();
                break;
            case 7:
                DrawAppearancePage(s);
                break;
            case 8:
                DrawChangelogPage();
                break;
            case 9:
                DrawDiagnosticsPage(s);
                break;
            default:
                DrawAboutPage(s);
                break;
        }
    }

    private void DrawPreviewPage(float s)
    {
        BeginCard("quick", FontAwesomeIcon.Bolt);
        Check("enabled", configuration.Enabled, v => configuration.Enabled = v, "quick.enabled");
        Pair("hp_bar", configuration.ShowHpBar, v => configuration.ShowHpBar = v,
            "mp_bar", configuration.ShowMpBar, v => configuration.ShowMpBar = v, "quick");
        Pair("lb_bar", configuration.ShowLimitBreakBar, v => configuration.ShowLimitBreakBar = v,
            "show_job", configuration.ShowJobIcon, v => configuration.ShowJobIcon = v, "quick");
        Pair("show_guard", configuration.ShowGuardStateSymbol, v => configuration.ShowGuardStateSymbol = v,
            "show_debuffs", configuration.ShowDebuffs, v => configuration.ShowDebuffs = v, "quick");
        Pair("signs_above", configuration.MoveSignMarkers, v => configuration.MoveSignMarkers = v,
            "dot", configuration.TargetingMeDot, v => configuration.TargetingMeDot = v, "quick");
        Pair("name", configuration.ShowNames, v => configuration.ShowNames = v,
            "hp_text", configuration.ShowHpPercent, v => configuration.ShowHpPercent = v, "quick");
        ImGui.Spacing();
        ImGui.TextDisabled(T("preview_hint"));
        ImGui.TextDisabled(T("wheel_help"));
        EndCard();
    }

    private void DrawGeneralPage()
    {
        BeginCard("sec_general", FontAwesomeIcon.SlidersH);
        Check("enabled", configuration.Enabled, v => configuration.Enabled = v);
        Check("hide_ui", configuration.HideWhenGameUiIsHidden, v => configuration.HideWhenGameUiIsHidden = v);
        Check("behind_ui", configuration.HideBehindPluginWindows, v => configuration.HideBehindPluginWindows = v);
        Help("behind_ui_help");
        Check("outside_pvp", configuration.DrawOutsidePvPForPositioning, v => configuration.DrawOutsidePvPForPositioning = v);
        Help("outside_pvp_help");
        Check("native_np", configuration.RequireNativeNameplatePresence, v => configuration.RequireNativeNameplatePresence = v);
        Help("native_np_help");
        ImGui.Spacing();
        ImGui.TextDisabled(T("show_for"));
        Pair("enemies", configuration.ShowEnemies, v => configuration.ShowEnemies = v,
            "party_members", configuration.ShowPartyMembers, v => configuration.ShowPartyMembers = v, "players");
        Pair("alliance_members", configuration.ShowAllianceMembers, v => configuration.ShowAllianceMembers = v,
            "myself", configuration.ShowLocalPlayer, v => configuration.ShowLocalPlayer = v, "players");
        Check("other_friendly", configuration.ShowOtherFriendlies, v => configuration.ShowOtherFriendlies = v);
        ImGui.Spacing();
        Slider("max_distance", configuration.MaximumDistance, 10f, 100f, Yalms("%.0f"), v => configuration.MaximumDistance = v, fallback: Defaults.MaximumDistance);
        Check("highlight_target", configuration.HighlightCurrentTarget, v => configuration.HighlightCurrentTarget = v);
        if (configuration.HighlightCurrentTarget)
        {
            ImGui.SameLine();
            Swatch("target.outline", configuration.TargetOutlineColor, v => configuration.TargetOutlineColor = v, Defaults.TargetOutlineColor);
        }

        Reset("general",
            nameof(Configuration.Enabled), nameof(Configuration.HideWhenGameUiIsHidden), nameof(Configuration.HideBehindPluginWindows),
            nameof(Configuration.DrawOutsidePvPForPositioning), nameof(Configuration.RequireNativeNameplatePresence),
            nameof(Configuration.ShowEnemies), nameof(Configuration.ShowPartyMembers), nameof(Configuration.ShowAllianceMembers),
            nameof(Configuration.ShowLocalPlayer), nameof(Configuration.ShowOtherFriendlies), nameof(Configuration.MaximumDistance),
            nameof(Configuration.HighlightCurrentTarget), nameof(Configuration.TargetOutlineColor));
        EndCard();

        BeginCard("sec_position", FontAwesomeIcon.ArrowsAlt);
        Combo("plate_position", configuration.PlateUnderFeet ? 1 : 0, new[] { T("above_head"), T("under_feet") },
            v => configuration.PlateUnderFeet = v == 1);
        if (configuration.PlateUnderFeet)
        {
            Slider("height_under", configuration.UnderFeetHeight, -1.5f, 1.5f, Yalms("%.2f"), v => configuration.UnderFeetHeight = v, fallback: Defaults.UnderFeetHeight);
            Help("height_under_help");
            Slider("gap_under", configuration.UnderFeetGap, 0f, 60f, "%.0f px", v => configuration.UnderFeetGap = v, fallback: Defaults.UnderFeetGap);
        }
        else
        {
            Slider("height_above", configuration.WorldHeight, 0.5f, 4.5f, Yalms("%.2f"), v => configuration.WorldHeight = v, fallback: Defaults.WorldHeight);
            Help("height_above_help");
        }

        Slider("screen_y", configuration.ScreenOffsetY, -100f, 100f, "%.0f px", v => configuration.ScreenOffsetY = v, fallback: Defaults.ScreenOffsetY);
        Check("stabilize", configuration.StabilizePositions, v => configuration.StabilizePositions = v);
        Help("stabilize_help");
        if (configuration.StabilizePositions)
        {
            Indented(() =>
            {
                Slider("follow_side", configuration.FollowHorizontal, 2f, 60f, "%.0f", v => configuration.FollowHorizontal = v, fallback: Defaults.FollowHorizontal);
                Help("follow_side_help");
                Slider("follow_vertical", configuration.FollowVertical, 0.5f, 60f, "%.1f", v => configuration.FollowVertical = v, fallback: Defaults.FollowVertical);
                Help("follow_vertical_help");
                Slider("snap", configuration.SnapYalms, 0.5f, 15f, Yalms("%.1f"), v => configuration.SnapYalms = v, fallback: Defaults.SnapYalms);
                Help("snap_help");
            });
        }

        Check("pixel_snap", configuration.PixelSnap, v => configuration.PixelSnap = v);
        Help("pixel_snap_help");
        Reset("position",
            nameof(Configuration.PlateUnderFeet), nameof(Configuration.UnderFeetHeight), nameof(Configuration.UnderFeetGap),
            nameof(Configuration.WorldHeight), nameof(Configuration.ScreenOffsetY), nameof(Configuration.StabilizePositions),
            nameof(Configuration.FollowHorizontal), nameof(Configuration.FollowVertical), nameof(Configuration.SnapYalms),
            nameof(Configuration.PixelSnap));
        EndCard();

        DrawShareCard();

        BeginCard("sec_reset", FontAwesomeIcon.Undo);
        ImGui.TextDisabled(T("reset_all_help"));
        var armed = Environment.TickCount64 < resetArmedUntil;     // a second click within 4 s confirms
        if (armed)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, Current.Bad with { W = 0.75f });
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Current.Bad);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, Current.Bad);
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 1f, 1f, 1f));
        }

        var clicked = ImGui.Button(L(armed ? "reset_confirm" : "reset_all", "reset.everything"));
        if (armed)
            ImGui.PopStyleColor(4);
        if (clicked && !armed)
        {
            resetArmedUntil = Environment.TickCount64 + 4_000;
        }
        else if (clicked)
        {
            resetArmedUntil = 0;
            var language = configuration.UiLanguage;
            var theme = configuration.Theme;
            configuration.ResetToDefaults();
            configuration.UiLanguage = language;     // the language and theme of this window stay
            configuration.Theme = theme;
            changed = true;
        }

        EndCard();
    }

    private void DrawBarsPage()
    {
        BeginCard("bars_shape", FontAwesomeIcon.SlidersH);
        Slider("bar_width", configuration.BarWidth, 40f, 240f, "%.0f px", v => configuration.BarWidth = v, fallback: Defaults.BarWidth);
        Slider("border_thickness", configuration.BorderThickness, 0f, 6f, "%.1f px", v => configuration.BorderThickness = v, fallback: Defaults.BorderThickness);
        Slider("corner_rounding", configuration.CornerRounding, 0f, 12f, "%.1f px", v => configuration.CornerRounding = v, fallback: Defaults.CornerRounding);
        Color("border", "border", configuration.BorderColor, v => configuration.BorderColor = v, Defaults.BorderColor);
        Reset("shape",
            nameof(Configuration.BarWidth), nameof(Configuration.BorderThickness), nameof(Configuration.CornerRounding),
            nameof(Configuration.BorderColor));
        EndCard();

        BeginCard("hp_bar", FontAwesomeIcon.Heart);
        Check("hp_bar", configuration.ShowHpBar, v => configuration.ShowHpBar = v, "hp.show");
        if (configuration.ShowHpBar)
        {
            Slider("hp_bar_height", configuration.BarHeight, 4f, 40f, "%.0f px", v => configuration.BarHeight = v, fallback: Defaults.BarHeight);
            ColorRow(
                ("hp.enemy", "c_enemy", configuration.EnemyColor, v => configuration.EnemyColor = v, Defaults.EnemyColor),
                ("hp.party", "c_party", configuration.PartyColor, v => configuration.PartyColor = v, Defaults.PartyColor),
                ("hp.alliance", "c_alliance", configuration.AllianceColor, v => configuration.AllianceColor = v, Defaults.AllianceColor));
            ColorRow(
                ("hp.friendly", "c_friendly", configuration.FriendlyColor, v => configuration.FriendlyColor = v, Defaults.FriendlyColor),
                ("hp.myself", "c_myself", configuration.LocalPlayerColor, v => configuration.LocalPlayerColor = v, Defaults.LocalPlayerColor),
                ("hp.empty", "c_empty", configuration.EmptyHealthColor, v => configuration.EmptyHealthColor = v, Defaults.EmptyHealthColor));
            Check("barrier", configuration.ShowShields, v => configuration.ShowShields = v);
            if (configuration.ShowShields)
            {
                ImGui.SameLine();
                Swatch("barrier.colour", configuration.ShieldColor, v => configuration.ShieldColor = v, Defaults.ShieldColor);
            }

            Help("barrier_help");
        }

        Reset("hp",
            nameof(Configuration.ShowHpBar), nameof(Configuration.BarHeight), nameof(Configuration.EnemyColor),
            nameof(Configuration.PartyColor), nameof(Configuration.AllianceColor), nameof(Configuration.FriendlyColor),
            nameof(Configuration.LocalPlayerColor), nameof(Configuration.EmptyHealthColor), nameof(Configuration.ShowShields),
            nameof(Configuration.ShieldColor));
        EndCard();

        BeginCard("mp_bar", FontAwesomeIcon.Tint);
        Check("mp_bar", configuration.ShowMpBar, v => configuration.ShowMpBar = v, "mp.show");
        if (configuration.ShowMpBar)
        {
            Slider("mp_bar_height", configuration.MpBarHeight, 2f, 20f, "%.0f px", v => configuration.MpBarHeight = v, fallback: Defaults.MpBarHeight);
            Slider("mp_bar_spacing", configuration.MpBarSpacing, 0f, 12f, "%.0f px", v => configuration.MpBarSpacing = v, fallback: Defaults.MpBarSpacing);
            ColorRow(
                ("mp.fill", "mp_colour", configuration.MpColor, v => configuration.MpColor = v, Defaults.MpColor),
                ("mp.empty", "c_empty", configuration.EmptyMpColor, v => configuration.EmptyMpColor = v, Defaults.EmptyMpColor));
        }

        Reset("mp",
            nameof(Configuration.ShowMpBar), nameof(Configuration.MpBarHeight), nameof(Configuration.MpBarSpacing),
            nameof(Configuration.MpColor), nameof(Configuration.EmptyMpColor));
        EndCard();

        BeginCard("lb_bar", FontAwesomeIcon.Fire);
        Check("lb_bar", configuration.ShowLimitBreakBar, v => configuration.ShowLimitBreakBar = v, "lb.show");
        Help("lb_bar_help");
        if (configuration.ShowLimitBreakBar)
        {
            Slider("lb_bar_height", configuration.LimitBreakBarHeight, 2f, 20f, "%.0f px", v => configuration.LimitBreakBarHeight = v, fallback: Defaults.LimitBreakBarHeight);
            Slider("lb_bar_spacing", configuration.LimitBreakBarSpacing, 0f, 12f, "%.0f px", v => configuration.LimitBreakBarSpacing = v, fallback: Defaults.LimitBreakBarSpacing);
            Slider("lb_blink", configuration.LimitBreakBlinkSpeed, 0.2f, 5f, "%.1f " + Unit("per_second"), v => configuration.LimitBreakBlinkSpeed = v, fallback: Defaults.LimitBreakBlinkSpeed);
            ColorRow(
                ("lb.charge", "c_charge", configuration.LimitBreakColor, v => configuration.LimitBreakColor = v, Defaults.LimitBreakColor),
                ("lb.empty", "c_empty", configuration.LimitBreakEmptyColor, v => configuration.LimitBreakEmptyColor = v, Defaults.LimitBreakEmptyColor));
            ColorRow(
                ("lb.full1", "c_full1", configuration.LimitBreakFullColorA, v => configuration.LimitBreakFullColorA = v, Defaults.LimitBreakFullColorA),
                ("lb.full2", "c_full2", configuration.LimitBreakFullColorB, v => configuration.LimitBreakFullColorB = v, Defaults.LimitBreakFullColorB));
        }

        Reset("lb",
            nameof(Configuration.ShowLimitBreakBar), nameof(Configuration.LimitBreakBarHeight),
            nameof(Configuration.LimitBreakBarSpacing), nameof(Configuration.LimitBreakBlinkSpeed),
            nameof(Configuration.LimitBreakColor), nameof(Configuration.LimitBreakEmptyColor),
            nameof(Configuration.LimitBreakFullColorA), nameof(Configuration.LimitBreakFullColorB));
        EndCard();
    }

    private void DrawTextPage()
    {
        BeginCard("sec_text", FontAwesomeIcon.Font);
        Check("name", configuration.ShowNames, v => configuration.ShowNames = v);
        if (configuration.ShowNames)
        {
            Indented(() =>
            {
                var game = T("font_game");
                Combo(
                    "name_font",
                    configuration.NameFont,
                    new[] { $"Axis ({game})", $"Jupiter ({game})", $"Trump Gothic ({game})", "Noto Sans", "Inconsolata" },
                    v => configuration.NameFont = v);
                Slider("name_size", configuration.NameFontScale, 0.5f, 2.5f, "%.2fx", v => configuration.NameFontScale = v, fallback: Defaults.NameFontScale);
                ImGui.TextDisabled(T("name_colours"));
                ColorRow(
                    ("name.enemy", "c_enemy", configuration.EnemyNameColor, v => configuration.EnemyNameColor = v, Defaults.EnemyNameColor),
                    ("name.party", "c_party", configuration.PartyNameColor, v => configuration.PartyNameColor = v, Defaults.PartyNameColor),
                    ("name.alliance", "c_alliance", configuration.AllianceNameColor, v => configuration.AllianceNameColor = v, Defaults.AllianceNameColor));
                ColorRow(
                    ("name.friendly", "c_friendly", configuration.FriendlyNameColor, v => configuration.FriendlyNameColor = v, Defaults.FriendlyNameColor),
                    ("name.myself", "c_myself", configuration.MyNameColor, v => configuration.MyNameColor = v, Defaults.MyNameColor));
            });
        }

        Check("hp_text", configuration.ShowHpPercent, v => configuration.ShowHpPercent = v);
        Help("hp_text_help");
        if (configuration.ShowHpPercent)
        {
            Indented(() =>
            {
                Combo(
                    "hp_format",
                    configuration.HpTextFormat,
                    new[] { T("fmt_percent"), T("fmt_current"), T("fmt_current_max"), T("fmt_current_short"), T("fmt_current_max_short") },
                    v => configuration.HpTextFormat = v);
                Slider("hp_text_size", configuration.HpTextFontScale, 0.5f, 2.5f, "%.2fx", v => configuration.HpTextFontScale = v, fallback: Defaults.HpTextFontScale);
            });
        }

        Color("text_colour", "text_colour", configuration.TextColor, v => configuration.TextColor = v, Defaults.TextColor);

        Reset("text",
            nameof(Configuration.ShowNames), nameof(Configuration.NameFontScale), nameof(Configuration.ShowHpPercent),
            nameof(Configuration.HpTextFormat), nameof(Configuration.HpTextFontScale), nameof(Configuration.TextColor),
            nameof(Configuration.EnemyNameColor), nameof(Configuration.PartyNameColor), nameof(Configuration.AllianceNameColor),
            nameof(Configuration.FriendlyNameColor), nameof(Configuration.MyNameColor), nameof(Configuration.NameFont));
        EndCard();

        // outline: names and HP text apart, for you, enemies (the target) and allies
        BeginCard("sec_outline", FontAwesomeIcon.Highlighter);
        ImGui.TextDisabled(T("outline_help"));
        foreach (var (group, caption) in new[] { ("My", "pv_you"), ("Enemy", "pv_target"), ("Ally", "pv_ally") })
        {
            ImGui.Spacing();
            ImGui.TextColored(Current.Accent, T(caption));
            Indented(() =>
            {
                OutlineRow(group, "Name", "outline_name");
                OutlineRow(group, "Hp", "outline_hp");
            });
        }

        Reset("outline", nameof(Configuration.MyNameOutline), nameof(Configuration.MyNameOutlineColor), nameof(Configuration.MyNameOutlineThickness), nameof(Configuration.MyHpOutline), nameof(Configuration.MyHpOutlineColor), nameof(Configuration.MyHpOutlineThickness), nameof(Configuration.EnemyNameOutline), nameof(Configuration.EnemyNameOutlineColor), nameof(Configuration.EnemyNameOutlineThickness), nameof(Configuration.EnemyHpOutline), nameof(Configuration.EnemyHpOutlineColor), nameof(Configuration.EnemyHpOutlineThickness), nameof(Configuration.AllyNameOutline), nameof(Configuration.AllyNameOutlineColor), nameof(Configuration.AllyNameOutlineThickness), nameof(Configuration.AllyHpOutline), nameof(Configuration.AllyHpOutlineColor), nameof(Configuration.AllyHpOutlineThickness));
        EndCard();
    }

    /// <summary>One outline: switch + colour on a line, thickness under it while it is on.</summary>
    private void OutlineRow(string group, string kind, string key)
    {
        var prefix = group + kind;
        var type = typeof(Configuration);
        var enabled = type.GetProperty(prefix + "Outline")!;
        var colour = type.GetProperty(prefix + "OutlineColor")!;
        var thickness = type.GetProperty(prefix + "OutlineThickness")!;
        var on = (bool)enabled.GetValue(configuration)!;
        Check(key, on, v => enabled.SetValue(configuration, v), $"outline.{prefix}");
        ImGui.SameLine();
        Swatch($"outline.{prefix}.colour", (Vector4)colour.GetValue(configuration)!, v => colour.SetValue(configuration, v), (Vector4)colour.GetValue(Defaults)!);
        if (on)
        {
            Indented(() => Slider(
                "outline_thickness",
                (float)thickness.GetValue(configuration)!,
                0.5f,
                5f,
                "%.1f px",
                v => thickness.SetValue(configuration, v),
                $"outline.{prefix}.thickness",
                (float)thickness.GetValue(Defaults)!));
        }
    }

    private void DrawIconsPage()
    {
        BeginCard("sec_job", FontAwesomeIcon.Users);
        Check("show_job", configuration.ShowJobIcon, v => configuration.ShowJobIcon = v);
        if (configuration.ShowJobIcon)
        {
            Combo("style", configuration.JobIconStyle, new[] { T("style_party"), T("style_gold"), T("style_light") },
                v => configuration.JobIconStyle = v);
            Check("match_height", configuration.JobIconAutoSize, v => configuration.JobIconAutoSize = v, "job.match");
            Help("match_height_help");
            if (!configuration.JobIconAutoSize)
                Indented(() => Slider("size", configuration.JobIconSize, 12f, 72f, "%.0f px", v => configuration.JobIconSize = v, "job.size", fallback: Defaults.JobIconSize));
            Slider("spacing", configuration.JobIconSpacing, 0f, 20f, "%.0f px", v => configuration.JobIconSpacing = v, "job.spacing", fallback: Defaults.JobIconSpacing);
            Slider("x_offset", configuration.JobIconOffsetX, -80f, 80f, "%.0f px", v => configuration.JobIconOffsetX = v, "job.x", fallback: Defaults.JobIconOffsetX);
            Slider("y_offset", configuration.JobIconOffsetY, -80f, 80f, "%.0f px", v => configuration.JobIconOffsetY = v, "job.y", fallback: Defaults.JobIconOffsetY);
            Check("elixir_fill", configuration.ShowElixirCast, v => configuration.ShowElixirCast = v);
            if (configuration.ShowElixirCast)
            {
                ImGui.SameLine();
                Swatch("elixir.colour", configuration.ElixirFillColor, v => configuration.ElixirFillColor = v, Defaults.ElixirFillColor);
            }

            Help("elixir_fill_help");
        }

        Reset("job",
            nameof(Configuration.ShowJobIcon), nameof(Configuration.JobIconStyle), nameof(Configuration.JobIconAutoSize),
            nameof(Configuration.JobIconSize), nameof(Configuration.JobIconSpacing), nameof(Configuration.JobIconOffsetX),
            nameof(Configuration.JobIconOffsetY), nameof(Configuration.ShowElixirCast), nameof(Configuration.ElixirFillColor));
        EndCard();

        BeginCard("sec_guard", FontAwesomeIcon.ShieldAlt);
        Check("show_guard", configuration.ShowGuardStateSymbol, v => configuration.ShowGuardStateSymbol = v);
        Help("show_guard_help");
        if (configuration.ShowGuardStateSymbol)
        {
            Check("use_action_icon", configuration.UseGuardActionIcon, v => configuration.UseGuardActionIcon = v);
            Help("use_action_icon_help");
            if (configuration.UseGuardActionIcon)
            {
                Indented(() =>
                {
                    Check("glow_active", configuration.GuardActiveGlow, v => configuration.GuardActiveGlow = v);
                    Check("countdown", configuration.ShowGuardCooldownText, v => configuration.ShowGuardCooldownText = v);
                    Slider("border_thickness", configuration.GuardIconBorderThickness, 0f, 6f, "%.1f px", v => configuration.GuardIconBorderThickness = v, "guard.border", fallback: Defaults.GuardIconBorderThickness);
                });
            }

            Check("match_height", configuration.GuardIconAutoSize, v => configuration.GuardIconAutoSize = v, "guard.match");
            Help("match_height_help");
            if (!configuration.GuardIconAutoSize)
                Indented(() => Slider("size", configuration.GuardSymbolSize, 8f, 72f, "%.0f px", v => configuration.GuardSymbolSize = v, "guard.size", fallback: Defaults.GuardSymbolSize));
            Slider("spacing", configuration.GuardSymbolSpacing, 0f, 20f, "%.0f px", v => configuration.GuardSymbolSpacing = v, "guard.spacing", fallback: Defaults.GuardSymbolSpacing);
            Slider("x_offset", configuration.GuardIconOffsetX, -80f, 80f, "%.0f px", v => configuration.GuardIconOffsetX = v, "guard.x", fallback: Defaults.GuardIconOffsetX);
            Slider("y_offset", configuration.GuardIconOffsetY, -80f, 80f, "%.0f px", v => configuration.GuardIconOffsetY = v, "guard.y", fallback: Defaults.GuardIconOffsetY);
            ColorRow(
                ("guard.ready", "c_ready", configuration.GuardReadyColor, v => configuration.GuardReadyColor = v, Defaults.GuardReadyColor),
                ("guard.active", "c_active", configuration.GuardActiveColor, v => configuration.GuardActiveColor = v, Defaults.GuardActiveColor),
                ("guard.cooldown", "c_cooldown", configuration.GuardCooldownColor, v => configuration.GuardCooldownColor = v, Defaults.GuardCooldownColor));
        }

        Reset("guard",
            nameof(Configuration.ShowGuardStateSymbol), nameof(Configuration.UseGuardActionIcon), nameof(Configuration.GuardActiveGlow),
            nameof(Configuration.ShowGuardCooldownText), nameof(Configuration.GuardIconBorderThickness), nameof(Configuration.GuardIconAutoSize),
            nameof(Configuration.GuardSymbolSize), nameof(Configuration.GuardSymbolSpacing), nameof(Configuration.GuardIconOffsetX),
            nameof(Configuration.GuardIconOffsetY), nameof(Configuration.GuardReadyColor), nameof(Configuration.GuardActiveColor),
            nameof(Configuration.GuardCooldownColor));
        EndCard();
    }

    private void DrawDebuffsPage()
    {
        BeginCard("sec_debuffs", FontAwesomeIcon.Skull);
        var positions = new[] { T("pos_auto"), T("pos_under"), T("pos_over"), T("pos_right"), T("pos_left") };
        Check("debuffs_enemies", configuration.ShowDebuffs, v => configuration.ShowDebuffs = v);
        if (configuration.ShowDebuffs)
        {
            Indented(() =>
            {
                Combo("position", configuration.DebuffPosition, positions, v => configuration.DebuffPosition = v, "debuff.position");
                Check("only_mine", configuration.OnlyMyDebuffs, v => configuration.OnlyMyDebuffs = v);
            });
        }

        Check("debuffs_allies", configuration.ShowAllyDebuffs, v => configuration.ShowAllyDebuffs = v);
        if (configuration.ShowAllyDebuffs)
            Indented(() => Combo("position", configuration.AllyDebuffPosition, positions, v => configuration.AllyDebuffPosition = v, "debuff.ally.position"));

        Check("debuffs_self", configuration.ShowOwnDebuffs, v => configuration.ShowOwnDebuffs = v);
        if (configuration.ShowOwnDebuffs)
            Indented(() => Combo("position", configuration.OwnDebuffPosition, positions, v => configuration.OwnDebuffPosition = v, "debuff.own.position"));

        if (configuration.ShowDebuffs || configuration.ShowAllyDebuffs || configuration.ShowOwnDebuffs)
        {
            ImGui.Spacing();
            Check("border_mine", configuration.HighlightMyDebuffs, v => configuration.HighlightMyDebuffs = v);
            if (configuration.HighlightMyDebuffs)
            {
                ImGui.SameLine();
                Swatch("debuff.mine.colour", configuration.MyDebuffBorderColor, v => configuration.MyDebuffBorderColor = v, Defaults.MyDebuffBorderColor);
            }

            Check("timers", configuration.ShowDebuffTimers, v => configuration.ShowDebuffTimers = v);
            Slider("icon_size", configuration.DebuffIconSize, 12f, 64f, "%.0f px", v => configuration.DebuffIconSize = v, "debuff.size", fallback: Defaults.DebuffIconSize);
            Slider("spacing", configuration.DebuffSpacing, 0f, 12f, "%.0f px", v => configuration.DebuffSpacing = v, "debuff.spacing", fallback: Defaults.DebuffSpacing);
            Slider("gap_plate", configuration.DebuffRowGap, 0f, 30f, "%.0f px", v => configuration.DebuffRowGap = v, "debuff.gap", fallback: Defaults.DebuffRowGap);
            Slider("max_shown", configuration.MaxDebuffs, 1f, 20f, "%.0f", v => configuration.MaxDebuffs = v, "debuff.max", fallback: Defaults.MaxDebuffs);
        }

        Reset("debuffs",
            nameof(Configuration.ShowDebuffs), nameof(Configuration.DebuffPosition), nameof(Configuration.OnlyMyDebuffs),
            nameof(Configuration.ShowAllyDebuffs), nameof(Configuration.AllyDebuffPosition),
            nameof(Configuration.ShowOwnDebuffs), nameof(Configuration.OwnDebuffPosition),
            nameof(Configuration.HighlightMyDebuffs), nameof(Configuration.MyDebuffBorderColor), nameof(Configuration.ShowDebuffTimers),
            nameof(Configuration.DebuffIconSize), nameof(Configuration.DebuffSpacing), nameof(Configuration.DebuffRowGap),
            nameof(Configuration.MaxDebuffs));
        EndCard();

        BeginCard("sec_signs", FontAwesomeIcon.Flag);
        Check("signs_above", configuration.MoveSignMarkers, v => configuration.MoveSignMarkers = v);
        Help("signs_above_help");
        if (configuration.MoveSignMarkers)
        {
            Slider("size", configuration.SignMarkerSize, 12f, 200f, "%.0f px", v => configuration.SignMarkerSize = v, "marker.size", fallback: Defaults.SignMarkerSize);
            Slider("gap", configuration.SignMarkerGap, 0f, 30f, "%.0f px", v => configuration.SignMarkerGap = v, "marker.gap", fallback: Defaults.SignMarkerGap);
            Slider("y_offset", configuration.SignMarkerOffsetY, -80f, 80f, "%.0f px", v => configuration.SignMarkerOffsetY = v, "marker.y", fallback: Defaults.SignMarkerOffsetY);
        }

        Reset("signs",
            nameof(Configuration.MoveSignMarkers), nameof(Configuration.SignMarkerSize), nameof(Configuration.SignMarkerGap),
            nameof(Configuration.SignMarkerOffsetY));
        EndCard();
    }

    private void DrawTargetingPage()
    {
        BeginCard("sec_targeting", FontAwesomeIcon.Crosshairs);
        Check("dot", configuration.TargetingMeDot, v => configuration.TargetingMeDot = v);
        Help("dot_help");
        if (configuration.TargetingMeDot)
        {
            Indented(() =>
            {
                Slider("dot_size", configuration.TargetingDotSize, 2f, 16f, "%.0f px", v => configuration.TargetingDotSize = v, fallback: Defaults.TargetingDotSize);
                Slider("dot_offset", configuration.TargetingDotOffsetY, -60f, 60f, "%.0f px", v => configuration.TargetingDotOffsetY = v, fallback: Defaults.TargetingDotOffsetY);
                ColorRow(
                    ("role.tank", "c_tank", configuration.TankColor, v => configuration.TankColor = v, Defaults.TankColor),
                    ("role.healer", "c_healer", configuration.HealerColor, v => configuration.HealerColor = v, Defaults.HealerColor),
                    ("role.dps", "c_dps", configuration.DpsColor, v => configuration.DpsColor = v, Defaults.DpsColor));
            });
        }

        Check("icons_above", configuration.TargetingMeIcons, v => configuration.TargetingMeIcons = v);
        Help("icons_above_help");
        if (configuration.TargetingMeIcons)
        {
            Indented(() =>
            {
                Check("red_glow_icons", configuration.TargetingMeIconGlow, v => configuration.TargetingMeIconGlow = v);
                Slider("icon_size", configuration.TargetingIconSize, 12f, 64f, "%.0f px", v => configuration.TargetingIconSize = v, "tm.size", fallback: Defaults.TargetingIconSize);
                Slider("height_above_me", configuration.TargetingIconHeight, 1f, 4.5f, Yalms("%.2f"), v => configuration.TargetingIconHeight = v, fallback: Defaults.TargetingIconHeight);
                Slider("y_offset", configuration.TargetingIconOffsetY, -100f, 100f, "%.0f px", v => configuration.TargetingIconOffsetY = v, "tm.y", fallback: Defaults.TargetingIconOffsetY);
            });
        }

        Check("job_glow", configuration.TargetingMeJobGlow, v => configuration.TargetingMeJobGlow = v);
        Check("swords", configuration.TargetingMeSwords, v => configuration.TargetingMeSwords = v);
        if (configuration.TargetingMeIcons || configuration.TargetingMeJobGlow || configuration.TargetingMeSwords)
            Color("glow_colour", "glow_colour", configuration.TargetingGlowColor, v => configuration.TargetingGlowColor = v, Defaults.TargetingGlowColor);

        Reset("targeting",
            nameof(Configuration.TargetingMeDot), nameof(Configuration.TargetingDotSize), nameof(Configuration.TargetingDotOffsetY),
            nameof(Configuration.TankColor), nameof(Configuration.HealerColor), nameof(Configuration.DpsColor),
            nameof(Configuration.TargetingMeIcons), nameof(Configuration.TargetingMeIconGlow), nameof(Configuration.TargetingIconSize),
            nameof(Configuration.TargetingIconHeight), nameof(Configuration.TargetingIconOffsetY), nameof(Configuration.TargetingMeJobGlow),
            nameof(Configuration.TargetingMeSwords), nameof(Configuration.TargetingGlowColor));
        EndCard();
    }

    private void DrawAppearancePage(float s)
    {
        BeginCard("sec_theme", FontAwesomeIcon.Palette);
        var drawList = ImGui.GetWindowDrawList();
        var inner = cardWidth - (24f * s);
        var gap = 10f * s;
        var tileWidth = (inner - gap) * 0.5f;
        var tileHeight = 62f * s;
        for (var i = 0; i < NameKeys.Length; i++)
        {
            var tile = Get(i);
            var position = ImGui.GetCursorScreenPos();
            if (ImGui.InvisibleButton($"##theme{i}", new Vector2(tileWidth, tileHeight)))
            {
                configuration.Theme = i;
                changed = true;
            }

            var hovered = ImGui.IsItemHovered();
            var selected = configuration.Theme == i;
            var maximum = position + new Vector2(tileWidth, tileHeight);
            var rounding = tile.Rounding * s;
            drawList.PushClipRect(position, maximum, true);
            if (tile.GradientTop.W > 0f)
            {
                var upper = U32(tile.GradientTop with { W = 1f });
                var lower = U32(tile.GradientBottom with { W = 1f });
                drawList.AddRectFilledMultiColor(position, maximum, upper, upper, lower, lower);
            }
            else
            {
                drawList.AddRectFilled(position, maximum, U32(tile.WindowBg with { W = 1f }), rounding);
            }

            drawList.AddRectFilled(position, position + new Vector2(tileWidth * 0.26f, tileHeight), U32(tile.SidebarBg), rounding, ImDrawFlags.RoundCornersLeft);
            drawList.AddRectFilled(
                position + new Vector2(tileWidth * 0.32f, tileHeight * 0.18f),
                position + new Vector2(tileWidth * 0.94f, tileHeight * 0.54f),
                U32(tile.CardBg),
                3f * s);
            drawList.AddRectFilled(
                position + new Vector2(tileWidth * 0.36f, tileHeight * 0.30f),
                position + new Vector2(tileWidth * 0.62f, tileHeight * 0.36f),
                U32(tile.Accent),
                1f * s);
            drawList.AddRectFilled(
                position + new Vector2(tileWidth * 0.05f, tileHeight * 0.22f),
                position + new Vector2(tileWidth * 0.20f, tileHeight * 0.28f),
                U32(tile.Accent),
                1f * s);
            drawList.AddText(
                position + new Vector2(tileWidth * 0.32f, tileHeight - ImGui.GetFontSize() - (6f * s)),
                U32(tile.Text),
                T(NameKeys[i]));
            drawList.PopClipRect();
            drawList.AddRect(
                position,
                maximum,
                U32(selected ? Current.Accent : hovered ? Current.TextDim : tile.CardBorder),
                rounding,
                ImDrawFlags.None,
                selected ? 2.5f * s : 1f);

            if (i % 2 == 0)
                ImGui.SameLine(0f, gap);
        }

        EndCard();

        BeginCard("language", FontAwesomeIcon.Language);
        var languages = (string[])LanguageNames.Clone();
        languages[0] = T("language_game");
        Combo("language", configuration.UiLanguage, languages, v =>
        {
            configuration.UiLanguage = v;
            Loc.Selected = v;
        });
        EndCard();

        BeginCard("sec_layout", FontAwesomeIcon.LayerGroup);
        Check("pin_preview", configuration.PinPreview, v => configuration.PinPreview = v);
        ImGui.TextDisabled(T("wheel_help"));
        EndCard();
    }

    private void DrawDiagnosticsPage(float s)
    {
        var palette = Current;
        BeginCard("sec_status", FontAwesomeIcon.TachometerAlt);
        var (statusText, statusColor) = StatusText();
        var inner = cardWidth - (24f * s);
        Tiles(
            inner,
            s,
            (T("sec_status"), statusText, statusColor, FontAwesomeIcon.Circle, StatusHelp()),
            (T("d_plates"), $"{renderer.PlatesNow} / {renderer.PlatesPeak}", palette.Accent, FontAwesomeIcon.LayerGroup, T("d_plates_help")),
            (T("d_frame"), renderer.FrameMs.ToString("0.000", CultureInfo.InvariantCulture) + " ms", palette.Accent, FontAwesomeIcon.TachometerAlt, T("d_frame_help")),
            (T("d_guards"), renderer.GuardUsesSeen.ToString(CultureInfo.InvariantCulture), palette.Accent, FontAwesomeIcon.ShieldAlt, T("d_guards_help")),
            (T("d_lb"), renderer.LimitBreakPlayers.ToString(CultureInfo.InvariantCulture), palette.Accent, FontAwesomeIcon.Fire, T("d_lb_help")),
            (T("d_signs"), renderer.SignMarkersPlaced.ToString(CultureInfo.InvariantCulture), palette.Accent, FontAwesomeIcon.Flag, T("d_signs_help")),
            (T("d_targeting"), renderer.TargetingMeNow.ToString(CultureInfo.InvariantCulture), renderer.TargetingMeNow > 0 ? palette.Bad : palette.Accent, FontAwesomeIcon.Crosshairs, T("d_targeting_help")),
            (T("language_game"), GameLanguageName(), palette.Accent, FontAwesomeIcon.Language, null));
        ImGui.Dummy(new Vector2(0f, 4f * s));
        Check("record_events", configuration.RecordEvents, v => configuration.RecordEvents = v);
        Help("record_events_help");
        EndCard();

        BeginCard("sec_feedback", FontAwesomeIcon.CommentDots);
        var drawList = ImGui.GetWindowDrawList();
        var boxStart = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(boxStart + new Vector2(10f * s, 8f * s));
        ImGui.BeginGroup();
        ImGui.TextColored(palette.Warn, T("fb_title"));
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + inner - (20f * s));
        ImGui.TextUnformatted(T("fb_steps"));
        ImGui.PopTextWrapPos();
        ImGui.EndGroup();
        var boxEnd = new Vector2(boxStart.X + inner, ImGui.GetItemRectMax().Y + (8f * s));
        drawList.ChannelsSetCurrent(1);                      // under the text, over the card
        drawList.AddRectFilled(boxStart, boxEnd, U32(palette.Warn with { W = 0.08f }), palette.Rounding * s);
        drawList.AddRect(boxStart, boxEnd, U32(palette.Warn with { W = 0.55f }), palette.Rounding * s, ImDrawFlags.None, 1f);
        drawList.ChannelsSetCurrent(2);
        ImGui.SetCursorScreenPos(new Vector2(boxStart.X, boxEnd.Y + (8f * s)));

        if (ImGui.Button(L("report_problem", "feedback.report")))
            ReportProblem("feedback");
        HoverHelp("report_help");
        ImGui.SameLine();
        if (AccentButton(L("copy_report", "feedback.copy")))
            CopyReport(false, "feedback");
        DrawToast("feedback");
        EndCard();

        BeginCard("sec_log", FontAwesomeIcon.Terminal);
        var filters = new[] { "log_all", "log_info", "log_warn", "log_error" };
        for (var i = 0; i < filters.Length; i++)
        {
            if (i > 0)
                ImGui.SameLine(0f, 4f * s);
            if (ToggleButton(L(filters[i], $"log.filter{i}"), logFilter == i, Vector2.Zero))
                logFilter = i;
        }

        ImGui.SameLine(0f, 10f * s);
        ImGui.SetNextItemWidth(Math.Max(120f * s, ImGui.GetContentRegionAvail().X - (12f * s)));
        ImGui.InputTextWithHint("##logsearch", T("log_filter"), ref logSearch, 128);

        if (AccentButton(L("copy_report", "log.copyreport")))
            CopyReport(false, "log");
        ImGui.SameLine();
        if (ImGui.Button(L("copy_lines", "log.copylines")))
            CopyReport(true, "log");
        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Button, palette.Bad with { W = 0.55f });
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, palette.Bad with { W = 0.75f });
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, palette.Bad);
        if (ImGui.Button(L("clear", "log.clear")))
        {
            UiLog.Clear();
            selectedLines.Clear();
        }

        ImGui.PopStyleColor(3);
        ImGui.SameLine();
        ImGui.Checkbox(L("auto_scroll", "log.autoscroll"), ref autoScroll);
        Help("log_select_help");
        DrawToast("log");

        var lines = FilteredLog();
        ImGui.PushStyleColor(ImGuiCol.ChildBg, palette.Frame);
        ImGui.BeginChild("##log", new Vector2(inner, 230f * s), true);
        ImGui.PopStyleColor();
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8f * s, 1f * s));     // log lines sit close together
        if (lines.Count == 0)
            ImGui.TextDisabled(T("log_empty"));

        // the text is drawn by hand: log lines can contain '#', which ImGui labels would treat as an id
        var logDrawList = ImGui.GetWindowDrawList();
        var lineHeight = ImGui.GetTextLineHeight();
        foreach (var entry in lines)
        {
            var color = entry.Level switch
            {
                UiLog.Level.Error => palette.Bad,
                UiLog.Level.Warning => palette.Warn,
                _ => entry.Text.StartsWith("[event]", StringComparison.Ordinal) ? palette.Accent : palette.Text,
            };
            var text = entry.Format();
            var position = ImGui.GetCursorScreenPos();
            if (ImGui.Selectable($"##line{entry.Id}", selectedLines.Contains(entry.Id), ImGuiSelectableFlags.None, new Vector2(0f, lineHeight)))
            {
                if (!selectedLines.Add(entry.Id))
                    selectedLines.Remove(entry.Id);
            }

            if (ImGui.IsItemHovered() && ImGui.CalcTextSize(text).X > ImGui.GetItemRectSize().X)
                Tooltip(text);
            logDrawList.AddText(position, U32(color), text);
        }

        var newest = UiLog.LastId;
        if (autoScroll && newest != lastLogId)
            ImGui.SetScrollHereY(1f);
        lastLogId = newest;
        ImGui.PopStyleVar();
        ImGui.EndChild();

        ImGui.Spacing();
        ImGui.TextDisabled(T("privacy"));
        EndCard();
    }

    /// <summary>Every version, newest first; 1.0 is the release. Texts are in Loc ("cl_" + version, one line per change).</summary>
    private static readonly string[] ChangelogVersions = { "1.2.1", "1.2", "1.1", "1.0" };

    private void DrawChangelogPage()
    {
        var palette = Current;
        var s = ImGuiHelpers.GlobalScale;
        var installed = VersionText();
        foreach (var version in ChangelogVersions)
        {
            BeginCardText(version, version == "1.0" ? FontAwesomeIcon.Flag : FontAwesomeIcon.Tag);
            if (version == "1.0" || version == installed)
            {
                var badges = new List<string>();
                if (version == "1.0")
                    badges.Add(T("cl_release"));
                if (version == installed)
                    badges.Add(T("cl_installed"));
                ImGui.TextColored(palette.Accent, string.Join(" · ", badges));
            }

            foreach (var line in T("cl_" + version.Replace('.', '_')).Split('\n'))
            {
                ImGui.Bullet();
                ImGui.SameLine(0f, 10f * s);
                ImGui.BeginGroup();
                ImGui.TextUnformatted(line);
                ImGui.EndGroup();
            }

            EndCard();
        }
    }

    private void DrawAboutPage(float s)
    {
        var palette = Current;
        BeginCardText("Jarvis PvP", FontAwesomeIcon.Robot);
        ImGui.TextColored(palette.TextDim, T("version_line").Replace("{VER}", VersionText(), StringComparison.Ordinal));
        ImGui.Spacing();
        ImGui.TextUnformatted(T("about_desc"));
        EndCard();

        BeginCard("sec_support", FontAwesomeIcon.HandHoldingHeart);
        var boosty = new Vector4(0.945f, 0.373f, 0.173f, 1f);            // Boosty orange
        ImGui.PushStyleColor(ImGuiCol.Button, boosty);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, boosty with { X = 1f, Y = 0.47f, Z = 0.26f });
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, boosty with { X = 0.85f, Y = 0.31f, Z = 0.13f });
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 1f, 1f, 1f));
        if (ImGui.Button(L("support_boosty", "about.boosty")))
            Util.OpenLink(BoostyUrl);
        ImGui.PopStyleColor(4);
        HoverText(BoostyUrl);
        ImGui.SameLine();
        var hipolink = new Vector4(0.42f, 0.36f, 0.95f, 1f);
        ImGui.PushStyleColor(ImGuiCol.Button, hipolink);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, hipolink with { X = 0.52f, Y = 0.46f, Z = 1f });
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, hipolink with { X = 0.36f, Y = 0.30f, Z = 0.85f });
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 1f, 1f, 1f));
        if (ImGui.Button(L("support_hipolink", "about.hipolink")))
            Util.OpenLink(HipolinkUrl);
        ImGui.PopStyleColor(4);
        HoverText(HipolinkUrl);
        ImGui.SameLine();
        if (ImGui.Button("GitHub###about.github"))
            Util.OpenLink(GitHubUrl);
        HoverHelp("github_help");
        ImGui.SameLine();
        if (ImGui.Button(L("report_problem", "about.report")))
            ReportProblem("about");
        HoverHelp("report_help");
        DrawToast("about");
        EndCard();

        BeginCard("sec_shortcuts", FontAwesomeIcon.Keyboard);
        CommandRow("/jpvp", T("cmd_open"), s);
        CommandRow("/jpvp on | off | toggle", T("cmd_toggle"), s);
        EndCard();

        BeginCard("sec_credits", FontAwesomeIcon.Star);
        ImGui.TextUnformatted(T("cr_original"));
        ImGui.Spacing();
        ImGui.TextUnformatted(T("cr_ai"));
        ImGui.Spacing();
        ImGui.TextUnformatted(T("cr_thanks"));
        ImGui.Spacing();
        ImGui.TextDisabled(T("cr_assets"));
        EndCard();
    }

    // ------------------------------------------------------------------ diagnostics helpers

    /// <summary>Status tiles: value on top, caption under it, 4 per row (2 in a narrow window); hover = help.</summary>
    private void Tiles(float inner, float s, params (string Caption, string Value, Vector4 Color, FontAwesomeIcon Icon, string? Help)[] tiles)
    {
        var palette = Current;
        var drawList = ImGui.GetWindowDrawList();
        var columns = inner >= 600f * s ? 4 : 2;
        var gap = 8f * s;
        var width = MathF.Floor((inner - (gap * (columns - 1))) / columns);
        var height = 54f * s;
        for (var i = 0; i < tiles.Length; i++)
        {
            if (i % columns != 0)
                ImGui.SameLine(0f, gap);
            var minimum = ImGui.GetCursorScreenPos();
            ImGui.Dummy(new Vector2(width, height));
            if (tiles[i].Help is { Length: > 0 } help && ImGui.IsItemHovered())
                Tooltip(help);

            var maximum = minimum + new Vector2(width, height);
            drawList.AddRectFilled(minimum, maximum, U32(palette.Frame), palette.Rounding * s);
            drawList.AddRectFilled(minimum, minimum + new Vector2(3f * s, height), U32(tiles[i].Color), palette.Rounding * s, ImDrawFlags.RoundCornersLeft);
            drawList.PushClipRect(minimum, maximum, true);
            Icon(drawList, new Vector2(maximum.X - (26f * s), minimum.Y + (10f * s)), ImGui.GetFontSize() * 1.2f, U32(palette.TextDim with { W = 0.35f }), tiles[i].Icon);
            var tileFont = RussianUi ? russianValueFont : valueFont;
            if (tileFont.Available)
            {
                using (tileFont.Push())
                    drawList.AddText(ImGui.GetFont(), ImGui.GetFontSize(), minimum + new Vector2(12f * s, 7f * s), U32(tiles[i].Color), tiles[i].Value);
            }
            else
            {
                drawList.AddText(minimum + new Vector2(12f * s, 8f * s), U32(tiles[i].Color), tiles[i].Value);
            }

            var captionSize = ImGui.GetFontSize() * 0.88f;
            drawList.AddText(
                ImGui.GetFont(),
                captionSize,
                new Vector2(minimum.X + (12f * s), MathF.Round(maximum.Y - captionSize - (8f * s))),
                U32(palette.TextDim),
                tiles[i].Caption);
            drawList.PopClipRect();
        }
    }

    private static string GameLanguageName()
    {
        var language = Plugin.ClientState.ClientLanguage;
        return language switch
        {
            Dalamud.Game.ClientLanguage.French => LanguageNames[2],
            Dalamud.Game.ClientLanguage.German => LanguageNames[3],
            Dalamud.Game.ClientLanguage.Japanese => LanguageNames[4],
            _ => LanguageNames[1],
        };
    }

    private List<UiLog.Entry> FilteredLog()
    {
        var search = logSearch.Trim();
        return UiLog.Snapshot()
            .Where(entry => logFilter switch
            {
                1 => entry.Level == UiLog.Level.Info,
                2 => entry.Level == UiLog.Level.Warning,
                3 => entry.Level == UiLog.Level.Error,
                _ => true,
            })
            .Where(entry => search.Length == 0 || entry.Format().Contains(search, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private void CopyReport(bool linesOnly, string place)
    {
        var lines = FilteredLog();
        if (selectedLines.Count > 0)
            lines = lines.Where(entry => selectedLines.Contains(entry.Id)).ToList();
        var logText = string.Join("\n", lines.Select(entry => entry.Format()));

        string text;
        if (linesOnly)
        {
            text = logText;
        }
        else
        {
            var report = new StringBuilder();
            report.AppendLine("**Jarvis PvP report**");
            report.AppendLine($"- Version: {typeof(Plugin).Assembly.GetName().Version}");
            report.AppendLine($"- Dalamud: {typeof(Dalamud.Plugin.IDalamudPluginInterface).Assembly.GetName().Version}");
            report.AppendLine($"- Game language: {Plugin.ClientState.ClientLanguage}; window language: {LanguageNames[Math.Clamp(configuration.UiLanguage, 0, LanguageNames.Length - 1)]}");
            report.AppendLine($"- In PvP: {Plugin.ClientState.IsPvP}; territory: {Plugin.ClientState.TerritoryType}");
            report.AppendLine(
                $"- Plates now / peak: {renderer.PlatesNow} / {renderer.PlatesPeak}; frame cost: {renderer.FrameMs.ToString("0.000", CultureInfo.InvariantCulture)} ms; " +
                $"LB gauges read: {renderer.LimitBreakPlayers}; signs: {renderer.SignMarkersPlaced}; Guard uses seen: {renderer.GuardUsesSeen}; " +
                $"enemies targeting you: {renderer.TargetingMeNow}");
            report.AppendLine();
            report.AppendLine("Settings:");
            report.AppendLine("```json");
            report.AppendLine(JsonSerializer.Serialize(configuration, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
            report.AppendLine("```");
            report.AppendLine();
            report.AppendLine("Log:");
            report.AppendLine("```");
            report.AppendLine(logText);
            report.AppendLine("```");
            text = report.ToString();
        }

        ImGui.SetClipboardText(text);
        toast = T("copied");
        toastPlace = place;
        toastBad = false;
        toastUntil = Environment.TickCount64 + 2_500;
    }

    /// <summary>Copies the report (version, settings, log) and opens a new GitHub issue to paste it into.</summary>
    private void ReportProblem(string place)
    {
        CopyReport(false, place);
        Util.OpenLink(NewIssueUrl);
    }

    private void DrawToast(string place)
    {
        if (toastPlace != place || Environment.TickCount64 >= toastUntil)
            return;
        ImGui.SameLine();
        ImGui.TextColored(toastBad ? Current.Bad : Current.Good, toast);
    }

    private void ShowToast(string key, string place, bool bad = false)
    {
        toast = T(key);
        toastPlace = place;
        toastBad = bad;
        toastUntil = Environment.TickCount64 + 3_000;
    }

    /// <summary>Settings code export / import, so players can share their look. Undo brings the previous settings back.</summary>
    private void DrawShareCard()
    {
        BeginCard("sec_share", FontAwesomeIcon.ShareAlt);
        ImGui.TextDisabled(T("share_help"));
        if (AccentButton(L("export_code", "share.export")))
        {
            ImGui.SetClipboardText(SettingsCode.Export(configuration));
            ShowToast("copied", "share");
        }

        ImGui.SameLine();
        if (ImGui.Button(L("import_code", "share.import")))
        {
            var backup = configuration.Clone();
            if (SettingsCode.Import(ImGui.GetClipboardText(), configuration) > 0)
            {
                beforeImport = backup;
                changed = true;
                ShowToast("import_done", "share");
            }
            else
            {
                ShowToast("import_failed", "share", true);
            }
        }

        HoverHelp("import_help");
        if (beforeImport != null)
        {
            ImGui.SameLine();
            if (ImGui.Button(L("undo_import", "share.undo")))
            {
                SettingsCode.CopyShared(beforeImport, configuration);
                beforeImport = null;
                changed = true;
                ShowToast("import_undone", "share");
            }
        }

        DrawToast("share");
        EndCard();
    }

    private static string VersionText()
    {
        var version = typeof(Plugin).Assembly.GetName().Version;
        return version is null ? "?" : version.Build > 0 ? $"{version.Major}.{version.Minor}.{version.Build}" : $"{version.Major}.{version.Minor}";
    }

    private void CommandRow(string command, string description, float s)
    {
        var palette = Current;
        var drawList = ImGui.GetWindowDrawList();
        var start = ImGui.GetCursorScreenPos();
        var padding = new Vector2(7f * s, 3f * s);
        var size = ImGui.CalcTextSize(command) + (padding * 2f);
        drawList.AddRectFilled(start, start + size, U32(palette.Frame), 4f * s);
        drawList.AddText(start + padding, U32(palette.Accent), command);
        ImGui.Dummy(size);
        ImGui.SameLine();
        ImGui.SetCursorScreenPos(new Vector2(start.X + Math.Max(size.X + (16f * s), 230f * s), start.Y + padding.Y));
        ImGui.TextUnformatted(description);
    }

    // ------------------------------------------------------------------ cards and widgets

    private void BeginCard(string titleKey, FontAwesomeIcon icon) => BeginCardText(T(titleKey), icon);

    private void BeginCardText(string title, FontAwesomeIcon icon)
    {
        var s = ImGuiHelpers.GlobalScale;
        SectionTitle(title, icon);
        // channel 0 = card background (drawn last, when the height is known), 1 = inner boxes, 2 = content
        var drawList = ImGui.GetWindowDrawList();
        drawList.ChannelsSplit(3);
        drawList.ChannelsSetCurrent(2);
        cardStart = ImGui.GetCursorScreenPos();
        cardWidth = ImGui.GetContentRegionAvail().X;
        ImGui.SetCursorScreenPos(cardStart + new Vector2(12f * s, 10f * s));
        ImGui.BeginGroup();
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + cardWidth - (24f * s));
    }

    private void EndCard()
    {
        var s = ImGuiHelpers.GlobalScale;
        var palette = Current;
        ImGui.PopTextWrapPos();
        ImGui.EndGroup();
        var bottom = ImGui.GetItemRectMax().Y + (10f * s);
        var drawList = ImGui.GetWindowDrawList();
        drawList.ChannelsSetCurrent(0);
        var maximum = new Vector2(cardStart.X + cardWidth, bottom);
        drawList.AddRectFilled(cardStart, maximum, U32(palette.CardBg), palette.Rounding * s);
        drawList.AddRect(cardStart, maximum, U32(palette.CardBorder), palette.Rounding * s, ImDrawFlags.None, palette.BorderSize);
        drawList.ChannelsMerge();
        ImGui.SetCursorScreenPos(new Vector2(cardStart.X, bottom + (8f * s)));
        ImGui.Dummy(new Vector2(0f, 0f));
    }

    private void SectionTitle(string title, FontAwesomeIcon icon)
    {
        var s = ImGuiHelpers.GlobalScale;
        var palette = Current;
        var drawList = ImGui.GetWindowDrawList();
        ImGui.Dummy(new Vector2(0f, 2f * s));
        var position = ImGui.GetCursorScreenPos();
        var lineHeight = ImGui.GetTextLineHeight();
        drawList.AddRectFilled(position + new Vector2(0f, 1f * s), position + new Vector2(3f * s, lineHeight - (1f * s)), U32(palette.Accent), 1.5f * s);
        Icon(drawList, position + new Vector2(10f * s, 0f), ImGui.GetFontSize(), U32(palette.Accent), icon);
        var text = title.ToUpperInvariant();
        var textPosition = position + new Vector2(32f * s, 0f);
        drawList.AddText(textPosition, U32(palette.Accent), text);
        var textEnd = textPosition.X + ImGui.CalcTextSize(text).X + (10f * s);
        var right = position.X + ImGui.GetContentRegionAvail().X;
        if (right > textEnd)
            drawList.AddLine(new Vector2(textEnd, position.Y + (lineHeight * 0.5f)), new Vector2(right, position.Y + (lineHeight * 0.5f)), U32(palette.Border), 1f);
        ImGui.Dummy(new Vector2(0f, lineHeight + (2f * s)));
    }

    private static string Yalms(string number) => number + " " + Unit("yalms");

    private void Check(string key, bool value, Action<bool> apply, string? id = null)
    {
        if (ImGui.Checkbox(L(key, id ?? key), ref value))
        {
            apply(value);
            changed = true;
        }
    }

    /// <summary>Two checkboxes on one line; the second column starts after the widest first one (last frame).</summary>
    private void Pair(string keyA, bool valueA, Action<bool> applyA, string keyB, bool valueB, Action<bool> applyB, string group)
    {
        var s = ImGuiHelpers.GlobalScale;
        var startX = ImGui.GetCursorScreenPos().X;
        Check(keyA, valueA, applyA, $"{group}.{keyA}");
        pairColumnNext = Math.Max(pairColumnNext, ImGui.GetItemRectSize().X);
        ImGui.SameLine();
        var column = Math.Max(260f * s, pairColumn + (28f * s));
        ImGui.SetCursorScreenPos(new Vector2(startX + column, ImGui.GetCursorScreenPos().Y));
        Check(keyB, valueB, applyB, $"{group}.{keyB}");
    }

    /// <summary>
    /// A slider with a number field and - / + buttons (one step of the shown precision: 1, 0.1 or 0.05), like the
    /// sliders of Customize+ / Glamourer, and a reset arrow back to the default. Typed numbers apply when the field
    /// is left or Enter is pressed.
    /// </summary>
    private void Slider(string key, float value, float minimum, float maximum, string format, Action<float> apply, string? id = null, float? fallback = null)
    {
        var s = ImGuiHelpers.GlobalScale;
        var spacing = ImGui.GetStyle().ItemInnerSpacing.X;
        var decimals = SliderDecimals(format);
        var step = decimals switch { 0 => 1f, 1 => 0.1f, _ => 0.05f };
        var number = $"%.{decimals}f";

        void Set(float next)
        {
            next = MathF.Round(next, Math.Max(decimals, 2));
            next = Math.Clamp(next, minimum, maximum);
            if (Math.Abs(next - value) < 0.0001f)
                return;
            apply(next);
            changed = true;
        }

        ImGui.PushID($"slider.{id ?? key}");
        var sliderValue = value;
        ImGui.SetNextItemWidth(170f * s);
        if (ImGui.SliderFloat("##slider", ref sliderValue, minimum, maximum, format))
            Set(sliderValue);

        ImGui.SameLine(0f, spacing);
        var typed = value;
        ImGui.SetNextItemWidth(62f * s);
        ImGui.InputFloat("##value", ref typed, 0f, 0f, number);
        if (ImGui.IsItemDeactivatedAfterEdit())
            Set(typed);

        var button = new Vector2(ImGui.GetFrameHeight());
        ImGui.SameLine(0f, spacing);
        if (ImGui.Button("-", button))
            Set(value - step);
        ImGui.SameLine(0f, spacing);
        if (ImGui.Button("+", button))
            Set(value + step);
        if (fallback is { } original)
        {
            // the turning arrow puts this one setting back to its default
            ImGui.SameLine(0f, spacing);
            var reset = ImGui.Button("##reset", button);
            var buttonMin = ImGui.GetItemRectMin();
            var buttonMax = ImGui.GetItemRectMax();
            using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
            {
                // the icon is drawn centred on the button (as a button label the icon font sat off centre)
                var glyph = FontAwesomeIcon.UndoAlt.ToIconString();
                var glyphSize = ImGui.CalcTextSize(glyph);
                var glyphPosition = ((buttonMin + buttonMax) * 0.5f) - (glyphSize * 0.5f);
                ImGui.GetWindowDrawList().AddText(
                    new Vector2(MathF.Round(glyphPosition.X), MathF.Round(glyphPosition.Y)),
                    ImGui.GetColorU32(ImGuiCol.Text),
                    glyph);
            }

            if (ImGui.IsItemHovered())
                Tooltip(T("reset_value"));
            if (reset && Math.Abs(original - value) > 0.0001f)
            {
                apply(original);
                changed = true;
            }
        }

        ImGui.PopID();

        ImGui.SameLine(0f, spacing);
        ImGui.TextUnformatted(T(key));
    }

    private static int SliderDecimals(string format)
    {
        var match = System.Text.RegularExpressions.Regex.Match(format, @"%\.(\d)f");
        return match.Success ? match.Groups[1].Value[0] - '0' : 0;
    }

    /// <summary>A drop-down list; Ctrl + mouse wheel over it steps through the entries.</summary>
    private void Combo(string key, int value, string[] items, Action<int> apply, string? id = null)
    {
        var current = Math.Clamp(value, 0, items.Length - 1);
        ImGui.SetNextItemWidth(280f * ImGuiHelpers.GlobalScale);

        // the chosen entry stands out in the open list (the themes' header colour is too faint for it)
        var accent = Current.Accent;
        // chosen = accent, the entry under the mouse = a neutral light tint, so the two never look alike
        ImGui.PushStyleColor(ImGuiCol.Header, accent with { W = 0.50f });
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Current.Text with { W = 0.14f });
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, Current.Text with { W = 0.22f });
        PushPopupBorder();
        var picked = ImGui.Combo(L(key, id ?? key), ref current, string.Join("\0", items) + "\0");
        PopPopupBorder();
        ImGui.PopStyleColor(3);
        if (picked)
        {
            apply(current);
            changed = true;
            return;
        }

        var io = ImGui.GetIO();
        if (io.KeyCtrl && io.MouseWheel != 0f && ImGui.IsItemHovered())
        {
            var next = Math.Clamp(current + (io.MouseWheel > 0f ? -1 : 1), 0, items.Length - 1);
            if (next != current)
            {
                apply(next);
                changed = true;
            }
        }
    }

    private static readonly Configuration Defaults = new();

    private const ImGuiColorEditFlags ColorFlags =
        ImGuiColorEditFlags.AlphaBar | ImGuiColorEditFlags.AlphaPreviewHalf | ImGuiColorEditFlags.NoInputs |
        ImGuiColorEditFlags.NoOptions | ImGuiColorEditFlags.NoTooltip;

    /// <summary>A colour picker; right-click puts the colour back to its default.</summary>
    private void Color(string id, string key, Vector4 color, Action<Vector4> apply, Vector4 fallback)
    {
        PushPopupBorder();
        var edited = ImGui.ColorEdit4(L(key, id), ref color, ColorFlags);
        PopPopupBorder();
        if (edited)
        {
            apply(color);
            changed = true;
        }

        ColorReset(fallback, apply);
    }

    private void Swatch(string id, Vector4 color, Action<Vector4> apply, Vector4 fallback)
    {
        PushPopupBorder();
        var edited = ImGui.ColorEdit4($"###{id}", ref color, ColorFlags | ImGuiColorEditFlags.NoLabel);
        PopPopupBorder();
        if (edited)
        {
            apply(color);
            changed = true;
        }

        ColorReset(fallback, apply);
    }

    private void ColorReset(Vector4 fallback, Action<Vector4> apply)
    {
        if (ImGui.IsItemHovered())
            Tooltip(T("colour_reset_help"));
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            apply(fallback);
            changed = true;
        }
    }

    private void ColorRow(params (string Id, string Key, Vector4 Color, Action<Vector4> Apply, Vector4 Fallback)[] colors)
    {
        for (var i = 0; i < colors.Length; i++)
        {
            if (i > 0)
                ImGui.SameLine(0f, 16f * ImGuiHelpers.GlobalScale);
            Color(colors[i].Id, colors[i].Key, colors[i].Color, colors[i].Apply, colors[i].Fallback);
        }
    }

    private static void Indented(Action draw)
    {
        ImGui.Indent();
        draw();
        ImGui.Unindent();
    }

    private void Reset(string id, params string[] properties)
    {
        ImGui.Spacing();
        if (!ImGui.SmallButton(L("reset_section", $"reset.{id}")))
            return;

        var defaults = new Configuration();
        foreach (var name in properties)
        {
            var property = typeof(Configuration).GetProperty(name);
            property?.SetValue(configuration, property.GetValue(defaults));
        }

        changed = true;
    }

    private static bool ToggleButton(string label, bool active, Vector2 size)
    {
        var palette = Current;
        if (active)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, palette.Accent);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, palette.AccentHover);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, palette.AccentHover);
            ImGui.PushStyleColor(ImGuiCol.Text, palette.AccentText);
        }

        var pressed = ImGui.Button(label, size);
        if (active)
            ImGui.PopStyleColor(4);
        return pressed;
    }

    private static bool AccentButton(string label) => ToggleButton(label, true, Vector2.Zero);

    private static void Help(string key)
    {
        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        HoverHelp(key);
    }

    private static void HoverHelp(string key) => HoverText(T(key));

    private static void HoverText(string text)
    {
        if (ImGui.IsItemHovered())
            Tooltip(text);
    }

    /// <summary>
    /// Open lists and colour pickers get the border of the main window (the theme's border colour) and the slightly
    /// lighter frame colour as background, so they stand out from the card under them.
    /// </summary>
    private static void PushPopupBorder()
    {
        var palette = Current;
        ImGui.PushStyleColor(ImGuiCol.Border, palette.Border with { W = 1f });
        ImGui.PushStyleColor(ImGuiCol.PopupBg, Vector4.Lerp(palette.CardBg, palette.Frame, 0.6f) with { W = 1f });
        ImGui.PushStyleVar(ImGuiStyleVar.PopupBorderSize, Math.Max(1f, palette.BorderSize));
    }

    private static void PopPopupBorder()
    {
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(2);
    }

    /// <summary>Tooltips stand out from the window: a background tinted with the accent and the main window border.</summary>
    private static void Tooltip(string text)
    {
        var palette = Current;
        var s = ImGuiHelpers.GlobalScale;
        var background = Vector4.Lerp(palette.CardBg, palette.Accent, 0.22f) with { W = 1f };
        ImGui.PushStyleColor(ImGuiCol.PopupBg, background);
        ImGui.PushStyleColor(ImGuiCol.Border, palette.Border with { W = 1f });
        ImGui.PushStyleVar(ImGuiStyleVar.PopupBorderSize, Math.Max(1f, palette.BorderSize));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(9f, 6f) * s);
        ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(ImGui.GetFontSize() * 28f);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(2);
    }

    private static void DisabledHelp(string key)
    {
        if (!ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            return;
        ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(ImGui.GetFontSize() * 28f);
        ImGui.TextUnformatted(T(key));
        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
    }
}
