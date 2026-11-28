using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using LuminaAction = Lumina.Excel.Sheets.Action;

namespace PvPTopPlates;

internal sealed class OverlayRenderer : IDisposable
{
    private const uint GuardActionId = 29054;
    private const uint ElixirActionId = 29055;
    private const uint GuardStatusId = 3054;
    private const uint AlternateGuardStatusId = 3673;
    private const float GuardDurationSeconds = 4f;
    private const float DefaultGuardRecastSeconds = 30f;

    private static readonly Vector2[] OutlineOffsets =
    {
        new(-1f, -1f), new(0f, -1f), new(1f, -1f), new(-1f, 0f),
        new(1f, 0f), new(-1f, 1f), new(0f, 1f), new(1f, 1f),
    };

    private readonly float guardRecastSeconds = DefaultGuardRecastSeconds;
    private readonly uint guardIconId;
    private readonly IFontHandle? countdownFont;
    private const float BaseTextPixels = 17f;
    private ScaledFont nameFont = new();          // swapped with the preview fonts while the settings preview draws
    private ScaledFont hpFont = new();
    private ScaledFont debuffFont = new();
    private readonly List<DebuffIcon> debuffPool = new(256);
    private readonly List<(uint JobId, byte Role)> targetingMe = new(8);
    private HashSet<ulong> targetingMeIds = new();
    private HashSet<ulong> previousTargetingMeIds = new();
    private HashSet<ulong> elixirIds = new();
    private HashSet<ulong> previousElixirIds = new();
    private readonly Dictionary<ulong, string> eventNames = new();
    private readonly Dictionary<ulong, uint> signMarkers = new(17);
    private readonly HashSet<ulong> drawnWithMarker = new(17);
    private readonly uint[] markerIcons = Array.Empty<uint>();
    private readonly Configuration settings;            // the saved settings (never swapped)
    private Configuration configuration;                // what is drawn: the settings, or the preview's copy

    // Plugin windows in front of the plates: their rectangles, and the uncovered pieces of a plate.
    private readonly List<(Vector2 Min, Vector2 Max)> occluders = new(32);
    private readonly Dictionary<ulong, (Vector2 Min, Vector2 Max)> plateExtents = new(64);
    private List<(Vector2 Min, Vector2 Max)> pieces = new(16);
    private List<(Vector2 Min, Vector2 Max)> spare = new(16);

    // What DrawBar drew for the current plate (screen space): measured, then used to place and cut plates.
    private Vector2 drawnMinimum;
    private Vector2 drawnMaximum;
    private readonly NativeNameplateTracker nameplateTracker;
    private readonly GuardActionTracker guardActionTracker;
    private readonly LimitBreakReader limitBreakReader = new();
    private readonly List<BarCandidate> candidates = new(64);
    private readonly Dictionary<ulong, PositionState> positionStates = new(64);
    private readonly Dictionary<ulong, GuardTrackingState> guardTrackingStates = new(64);
    private readonly List<ulong> stalePositionIds = new(64);
    private readonly List<ulong> staleGuardIds = new(64);
    private uint frameNumber;

    public OverlayRenderer(
        Configuration configuration,
        NativeNameplateTracker nameplateTracker,
        GuardActionTracker guardActionTracker)
    {
        settings = configuration;
        this.configuration = configuration;
        this.nameplateTracker = nameplateTracker;
        this.guardActionTracker = guardActionTracker;

        try
        {
            var markerSheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Marker>();
            var icons = new uint[17];
            for (var i = 0; i < icons.Length; i++)
                icons[i] = markerSheet.GetRowOrDefault((uint)i + 1) is { } marker ? (uint)marker.Icon : 0u;
            markerIcons = icons;
        }
        catch (Exception exception)
        {
            UiLog.Warn("Marker sheet unavailable; sign markers stay where the game draws them.", exception);
        }

        // The hotbar icon and the real recast of PvP Guard come from the game data.
        try
        {
            var guard = Plugin.DataManager.GetExcelSheet<LuminaAction>().GetRowOrDefault(GuardActionId);
            if (guard is { } row)
            {
                guardIconId = row.Icon;
                if (row.Recast100ms > 0)
                    guardRecastSeconds = row.Recast100ms / 10f;
            }
        }
        catch (Exception exception)
        {
            UiLog.Warn("Guard action data unavailable; using the shield symbol and a 30 s recast.", exception);
        }

        try
        {
            countdownFont = Plugin.PluginInterface.UiBuilder.FontAtlas.NewGameFontHandle(
                new GameFontStyle(GameFontFamilyAndSize.MiedingerMid36));
        }
        catch (Exception exception)
        {
            UiLog.Warn("Countdown font unavailable; using the default font.", exception);
        }

        UiLog.Info($"Guard: icon {guardIconId}, recast {guardRecastSeconds:0.#} s; sign icons {markerIcons.Length}.");
    }

    public void Dispose()
    {
        countdownFont?.Dispose();
        nameFont.Dispose();
        hpFont.Dispose();
        debuffFont.Dispose();
        previewNameFont.Dispose();
        previewHpFont.Dispose();
        previewDebuffFont.Dispose();
    }

    /// <summary>"Test in world": a sample enemy plate above your own character while the settings window is open.</summary>
    public static bool TestInWorld { get; set; }

    public int PlatesNow { get; private set; }
    public int PlatesPeak { get; private set; }
    public double FrameMs { get; set; }
    public int TargetingMeNow { get; private set; }
    public int LimitBreakPlayers => limitBreakReader.Count;
    public int SignMarkersPlaced => signMarkers.Count;
    public int GuardUsesSeen => guardActionTracker.UsesSeen;

    /// <summary>What the settings preview shows: everything, or only what one settings page sets.</summary>
    public enum PreviewFocus
    {
        Everything,
        General,
        Bars,
        Text,
        Icons,
        DebuffsAndSigns,
        Targeting,
    }

    private const float FigureHeight = 56f;                       // preview figure, px at scale 1
    private const float PixelsPerYalm = FigureHeight / 1.8f;      // the figure stands for a 1.8 yalm character
    private const float DefaultWorldHeight = 2.15f;               // at this height the preview plate clears the head
    private const float DefaultIconHeight = 2.6f;
    private const ulong TestPlateId = ulong.MaxValue;
    private static readonly float[] PreviewDebuffDurations = { 12f, 8f, 23f };
    private static readonly Vector4 PreviewLabelColor = new(0.80f, 0.84f, 0.92f, 0.85f);

    private uint[]? previewDebuffIcons;
    private readonly (Vector2 Min, Vector2 Max)[] previewExtents = new (Vector2 Min, Vector2 Max)[21];   // focus x slot
    private readonly bool[] previewMeasured = new bool[21];
    private readonly float[] previewFit = { 1f, 1f, 1f, 1f, 1f, 1f, 1f };
    private ScaledFont previewNameFont = new();
    private ScaledFont previewHpFont = new();
    private ScaledFont previewDebuffFont = new();
    private float textScale = 1f;                                 // name / HP text size factor (preview fit)

    /// <summary>Guard in the previews: 4 s active, then the recast, then a few seconds ready; shifted by the time.</summary>
    private GuardInfo PreviewGuard(float time)
    {
        var recast = guardRecastSeconds;
        var t = time % (recast + 6f);
        if (t < GuardDurationSeconds)
            return new GuardInfo(GuardState.Active, GuardDurationSeconds - t, recast - t, (recast - t) / recast);
        if (t < recast)
            return new GuardInfo(GuardState.Cooldown, 0f, recast - t, (recast - t) / recast);
        return new GuardInfo(GuardState.Ready, 0f, 0f, 0f);
    }

    /// <summary>Limit Break in the previews: fills in 8 s, then stays full (blinking) for 4 s.</summary>
    private static float PreviewCharge(float time) => Math.Min(1f, (time % 12f) / 8f);

    private static float PreviewTime() => (Environment.TickCount64 % 3_600_000L) / 1000f;

    /// <summary>1 -> 0 -> 1 over the period.</summary>
    private static float Wave(float time, float period) => 0.5f + (0.5f * MathF.Cos(time * MathF.Tau / period));

    /// <summary>The sample enemy: HP and MP moving, Guard cycling, LB filling, debuffs counting down, a sign, targeting you.</summary>
    private BarCandidate SampleEnemy(Vector2 anchor, Vector2 feet, bool hasFeet, bool isCurrentTarget, float time)
    {
        previewDebuffIcons ??= FindPreviewDebuffIcons();
        var debuffStart = debuffPool.Count;
        for (var i = 0; i < previewDebuffIcons.Length; i++)
        {
            var duration = PreviewDebuffDurations[i % PreviewDebuffDurations.Length];
            debuffPool.Add(new DebuffIcon(previewDebuffIcons[i], duration - (time % duration), i == 0));
        }

        return new BarCandidate(
            Loc.T("preview_enemy"),
            (uint)(60_000 * (0.45f + (0.40f * Wave(time, 16f)))),
            60_000,
            (uint)(10_000 * (0.70f + (0.30f * Wave(time + 3f, 11f)))),
            10_000,
            0,
            anchor,
            10f,
            PlayerRelation.Enemy,
            PreviewGuard(time),
            configuration.ShowLimitBreakBar ? PreviewCharge(time) : -1f,
            34,
            -1f,
            debuffStart,
            debuffPool.Count - debuffStart,
            true,
            2,
            markerIcons.Length > 0 ? markerIcons[0] : 0u,
            TestPlateId,
            feet,
            hasFeet,
            isCurrentTarget);
    }

    /// <summary>Sample debuffs for the previews, counting down; returns (start, count) in the debuff pool.</summary>
    private (int Start, int Count) SampleDebuffs(float time, int count, bool firstMine)
    {
        previewDebuffIcons ??= FindPreviewDebuffIcons();
        var start = debuffPool.Count;
        for (var i = 0; i < Math.Min(count, previewDebuffIcons.Length); i++)
        {
            var duration = PreviewDebuffDurations[i % PreviewDebuffDurations.Length];
            debuffPool.Add(new DebuffIcon(previewDebuffIcons[i], duration - (time % duration), firstMine && i == 0));
        }

        return (start, debuffPool.Count - start);
    }

    /// <summary>The preview ally: takes damage behind a barrier, drinks an Elixir (the icon fills), back to full.</summary>
    private BarCandidate SampleAlly(Vector2 anchor, float time)
    {
        var phase = time % 14f;
        var hp = phase < 6f ? 0.95f - (0.55f * (phase / 6f)) : phase < 10.5f ? 0.40f : 1f;
        var mp = phase < 10.5f ? 0.55f : 1f;
        var barrier = phase is >= 1.5f and < 5f ? (byte)30 : (byte)0;
        var elixir = configuration.ShowElixirCast && phase is >= 6f and < 10.5f ? (phase - 6f) / 4.5f : -1f;
        var (debuffStart, debuffCount) = configuration.ShowAllyDebuffs ? SampleDebuffs(time + 4f, 2, false) : (0, 0);
        return new BarCandidate(
            Loc.T("preview_ally"),
            (uint)(52_000 * hp),
            52_000,
            (uint)(10_000 * mp),
            10_000,
            barrier,
            anchor,
            10f,
            PlayerRelation.Party,
            PreviewGuard(time + 13f),
            configuration.ShowLimitBreakBar ? PreviewCharge(time + 5f) : -1f,
            24,
            elixir,
            debuffStart,
            debuffCount,
            false,
            4,
            0u,
            TestPlateId - 2,
            Vector2.Zero,
            false,
            false);
    }

    /// <summary>You in the preview: HP and MP moving, Guard and Limit Break on their own cycles.</summary>
    private BarCandidate SampleYou(Vector2 anchor, float time, string name, uint job, byte role)
    {
        var (debuffStart, debuffCount) = configuration.ShowOwnDebuffs ? SampleDebuffs(time + 9f, 1, false) : (0, 0);
        return new BarCandidate(
            name,
            (uint)(58_500 * (0.78f + (0.22f * Wave(time, 10f)))),
            58_500,
            (uint)(10_000 * (0.50f + (0.50f * Wave(time + 2f, 9f)))),
            10_000,
            0,
            anchor,
            0f,
            PlayerRelation.LocalPlayer,
            PreviewGuard(time + 24f),
            configuration.ShowLimitBreakBar ? PreviewCharge(time + 8.5f) : -1f,
            job,
            -1f,
            debuffStart,
            debuffCount,
            false,
            role,
            0u,
            TestPlateId - 1,
            Vector2.Zero,
            false,
            false);
    }

    private void DrawTestInWorld(ImDrawListPtr drawList, float uiScale)
    {
        var display = ImGui.GetIO().DisplaySize;
        var anchor = new Vector2(display.X * 0.5f, display.Y * 0.22f);
        var feet = Vector2.Zero;
        var hasFeet = false;
        var local = Plugin.ObjectTable.LocalPlayer;
        if (local is not null && local.IsValid())
        {
            var position = GetVisualPosition(local);
            var height = configuration.PlateUnderFeet ? configuration.UnderFeetHeight : configuration.WorldHeight;
            if (Plugin.GameGui.WorldToScreen(position + (Vector3.UnitY * height), out var screen, out var inView) && inView)
            {
                anchor = screen + new Vector2(0f, configuration.ScreenOffsetY * uiScale);
                if (configuration.PlateUnderFeet)
                    anchor.Y += configuration.UnderFeetGap * uiScale;
            }

            if (configuration.TargetingMeDot &&
                Plugin.GameGui.WorldToScreen(position, out var feetScreen, out var feetInView) && feetInView)
            {
                feet = feetScreen + new Vector2(0f, configuration.TargetingDotOffsetY * uiScale);
                hasFeet = true;
            }
        }

        anchor = new Vector2(MathF.Round(anchor.X), MathF.Round(anchor.Y));
        debuffPool.Clear();
        DrawOccluded(drawList, SampleEnemy(anchor, feet, hasFeet, false, PreviewTime()), uiScale);
    }

    /// <summary>
    /// The preview inside the settings window: you, your target (an enemy that has you as its target) and an ally,
    /// animated. <paramref name="focus"/> limits it to what one settings page sets. One scale for the whole scene
    /// makes everything fit (measured from what was drawn), each plate stays inside its third of the box, and the
    /// ground line only moves when that scale has to change.
    /// </summary>
    public void DrawPreviewScene(ImDrawListPtr drawList, Vector2 minimum, Vector2 maximum, float uiScale, PreviewFocus focus)
    {
        var saved = configuration;
        configuration = saved.Clone();
        ApplyFocus(configuration, focus);
        (nameFont, previewNameFont) = (previewNameFont, nameFont);
        (hpFont, previewHpFont) = (previewHpFont, hpFont);
        (debuffFont, previewDebuffFont) = (previewDebuffFont, debuffFont);
        drawList.PushClipRect(minimum, maximum, true);
        try
        {
            DrawScene(drawList, minimum, maximum, uiScale, focus);
        }
        finally
        {
            drawList.PopClipRect();
            textScale = 1f;
            (nameFont, previewNameFont) = (previewNameFont, nameFont);
            (hpFont, previewHpFont) = (previewHpFont, hpFont);
            (debuffFont, previewDebuffFont) = (previewDebuffFont, debuffFont);
            configuration = saved;
        }
    }

    /// <summary>Every page keeps the bars as reference; whatever a page does not set is hidden.</summary>
    private static void ApplyFocus(Configuration view, PreviewFocus focus)
    {
        if (focus == PreviewFocus.Everything)
            return;

        var general = focus == PreviewFocus.General;
        var text = focus == PreviewFocus.Text;
        var icons = focus == PreviewFocus.Icons;
        var debuffs = focus == PreviewFocus.DebuffsAndSigns;
        var targeting = focus == PreviewFocus.Targeting;
        view.ShowNames &= general || text || debuffs;                  // signs sit above the name
        view.ShowHpPercent &= text;
        view.ShowJobIcon &= icons || debuffs || targeting;             // debuff positions and the red glow use it
        view.ShowGuardStateSymbol &= icons || debuffs;
        view.ShowElixirCast &= icons;
        view.ShowDebuffs &= debuffs;
        view.ShowAllyDebuffs &= debuffs;
        view.ShowOwnDebuffs &= debuffs;
        view.MoveSignMarkers &= debuffs;
        view.TargetingMeDot &= targeting;
        view.TargetingMeIcons &= targeting;
        view.TargetingMeJobGlow &= targeting;
        view.TargetingMeSwords &= targeting;
        view.HighlightCurrentTarget &= general;
    }

    private void DrawScene(ImDrawListPtr drawList, Vector2 minimum, Vector2 maximum, float baseScale, PreviewFocus focus)
    {
        var time = PreviewTime();
        var focusIndex = (int)focus;
        var general = focus == PreviewFocus.General;
        var margin = 10f * baseScale;
        var captionTop = minimum.Y + (6f * baseScale);
        var areaTop = captionTop + ImGui.GetTextLineHeight() + (6f * baseScale);
        var areaBottom = maximum.Y - (8f * baseScale);
        var slotWidth = (maximum.X - minimum.X) / 3f;

        // slots: 0 you, 1 your target, 2 an ally; on General the "Show plates for" switches apply
        var shown = new[]
        {
            !general || configuration.ShowLocalPlayer,
            !general || configuration.ShowEnemies,
            !general || configuration.ShowPartyMembers,
        };

        // where the plate sits relative to the feet (px at scale 1, y down); the height settings move it as in the world
        var underFeet = configuration.PlateUnderFeet;
        var headTop = -1.015f * FigureHeight;
        var anchorY = underFeet
            ? (-configuration.UnderFeetHeight * PixelsPerYalm) + configuration.UnderFeetGap + configuration.ScreenOffsetY
            : headTop - 8f - BarStackHeight() - ((configuration.WorldHeight - DefaultWorldHeight) * PixelsPerYalm) +
              configuration.ScreenOffsetY;
        var dot = configuration.TargetingMeDot && shown[1];
        var dotRadius = Math.Max(1.5f, configuration.TargetingDotSize) + 1f;
        var icons = configuration.TargetingMeIcons;
        var iconSize = Math.Max(8f, configuration.TargetingIconSize);
        var iconGlow = configuration.TargetingMeIconGlow ? 7f : 0f;

        // what each slot covers around its feet point (plates: measured last frame)
        var extents = new (Vector2 Min, Vector2 Max)[3];
        for (var i = 0; i < 3; i++)
        {
            var min = new Vector2(-0.17f * FigureHeight, headTop);
            var max = new Vector2(0.17f * FigureHeight, 0f);
            if (shown[i])
            {
                var plate = PlateExtents(focusIndex, i);
                min = Vector2.Min(min, new Vector2(plate.Min.X, anchorY + plate.Min.Y));
                max = Vector2.Max(max, new Vector2(plate.Max.X, anchorY + plate.Max.Y));
            }

            if (i == 1 && dot)
            {
                min = Vector2.Min(min, new Vector2(-dotRadius, configuration.TargetingDotOffsetY - dotRadius));
                max = Vector2.Max(max, new Vector2(dotRadius, configuration.TargetingDotOffsetY + dotRadius));
            }

            if (i == 0 && icons)
            {
                var bottom = IconsBottom(underFeet, headTop, anchorY, shown[0], focusIndex);
                min = Vector2.Min(min, new Vector2(-(iconSize * 0.5f) - iconGlow, bottom - iconSize - iconGlow));
                max = Vector2.Max(max, new Vector2((iconSize * 0.5f) + iconGlow, bottom + iconGlow));
            }

            extents[i] = (min, max);
        }

        // one scale for the scene: the widest slot and the tallest column fit; it changes in steps
        var widest = 1f;
        var top = 0f;
        var low = 0f;
        foreach (var (min, max) in extents)
        {
            widest = Math.Max(widest, max.X - min.X);
            top = Math.Min(top, min.Y);
            low = Math.Max(low, max.Y);
        }

        var raw = Math.Min(
            1f,
            Math.Min(
                (slotWidth - (2f * margin)) / (widest * baseScale),
                (areaBottom - areaTop) / (Math.Max(1f, low - top) * baseScale)));
        var fit = previewFit[focusIndex];
        if (raw < fit || raw >= Math.Min(1f, fit + 0.05f))          // shrink at once, grow back only by a clear step
            fit = Math.Clamp(MathF.Floor(raw * 40f) / 40f, 0.25f, 1f);
        previewFit[focusIndex] = fit;
        var s = baseScale * fit;
        textScale = fit;

        // the ground stays at a fixed height of the box (low with plates above the heads, high with plates under the
        // feet) and only moves when the scene needs the room
        var minGround = areaTop - (top * s);
        var maxGround = Math.Max(minGround, areaBottom - (low * s));
        var desiredGround = areaTop + ((areaBottom - areaTop) * (underFeet ? 0.26f : 0.82f));
        var groundY = MathF.Round(Math.Clamp(desiredGround, minGround, maxGround));
        var feetX = new float[3];
        for (var i = 0; i < 3; i++)
        {
            var left = minimum.X + (slotWidth * i) + margin;
            var right = minimum.X + (slotWidth * (i + 1)) - margin;
            var x = (left + right) * 0.5f;
            var contentLeft = x + (extents[i].Min.X * s);
            var contentRight = x + (extents[i].Max.X * s);
            if (contentLeft < left)
                x += left - contentLeft;
            else if (contentRight > right)
                x -= contentRight - right;
            feetX[i] = MathF.Round(x);
        }

        // captions, ground line, figures
        var captions = new[] { Loc.T("pv_you"), Loc.T("pv_target"), Loc.T("pv_ally") };
        for (var i = 0; i < 3; i++)
        {
            var caption = shown[i] ? captions[i] : $"{captions[i]} · {Loc.T("pv_off")}";
            var size = ImGui.CalcTextSize(caption);
            drawList.AddText(
                new Vector2(MathF.Round(minimum.X + (slotWidth * (i + 0.5f)) - (size.X * 0.5f)), MathF.Round(captionTop)),
                ToColor(shown[i] ? PreviewLabelColor : PreviewLabelColor with { W = 0.45f }),
                caption);
        }

        drawList.AddLine(
            new Vector2(minimum.X + (14f * baseScale), groundY),
            new Vector2(maximum.X - (14f * baseScale), groundY),
            ToColor(PreviewLabelColor with { W = 0.22f }),
            1f);

        var figureColors = new[] { configuration.LocalPlayerColor, configuration.EnemyColor, configuration.PartyColor };
        for (var i = 0; i < 3; i++)
            DrawFigure(drawList, feetX[i], groundY, FigureHeight * s, figureColors[i], s);

        if (dot)
            DrawTargetingDot(drawList, new Vector2(feetX[1], MathF.Round(groundY + (configuration.TargetingDotOffsetY * s))), 2, s);

        var local = Plugin.ObjectTable.LocalPlayer;
        var youName = local?.Name.TextValue;
        if (string.IsNullOrEmpty(youName))
            youName = Loc.PlateText("pv_you");
        var youJob = local?.ClassJob.RowId ?? 31u;
        if (youJob == 0)
            youJob = 31;
        var youRole = local is not null ? GetRole(local) : (byte)3;

        // plates (measured for the next frame)
        debuffPool.Clear();
        var plateY = MathF.Round(groundY + (anchorY * s));
        if (shown[2])
            DrawMeasured(drawList, SampleAlly(new Vector2(feetX[2], plateY), time), s, focusIndex, 2);
        if (shown[1])
            DrawMeasured(drawList, SampleEnemy(new Vector2(feetX[1], plateY), Vector2.Zero, false, true, time), s, focusIndex, 1);
        if (shown[0])
            DrawMeasured(drawList, SampleYou(new Vector2(feetX[0], plateY), time, youName, youJob, youRole), s, focusIndex, 0);

        // "their job icons above my head": your target's icon over you
        if (icons)
        {
            var size = MathF.Round(iconSize * s);
            var iconMinimum = new Vector2(
                MathF.Round(feetX[0] - (size * 0.5f)),
                MathF.Round(groundY + (IconsBottom(underFeet, headTop, anchorY, shown[0], focusIndex) * s) - size));
            if (configuration.TargetingMeIconGlow)
                DrawGlow(drawList, iconMinimum, iconMinimum + new Vector2(size), configuration.TargetingGlowColor, s);
            DrawJobIcon(drawList, iconMinimum + new Vector2(size * 0.5f), size, 34, -1f);
        }
    }

    /// <summary>A preview plate's extents around its anchor (px at scale 1): measured, or a first guess.</summary>
    private (Vector2 Min, Vector2 Max) PlateExtents(int focusIndex, int slot)
    {
        var index = (focusIndex * 3) + slot;
        if (previewMeasured[index])
            return previewExtents[index];

        var half = (configuration.BarWidth * 0.5f) + 30f;
        return (new Vector2(-half, -30f), new Vector2(half, BarStackHeight() + 10f));
    }

    /// <summary>Bottom of the "their job icons above my head" row over you (px at scale 1, feet = 0).</summary>
    private float IconsBottom(bool underFeet, float headTop, float anchorY, bool plateShown, int focusIndex)
    {
        var top = headTop;
        if (!underFeet && plateShown)
            top = Math.Min(top, anchorY + PlateExtents(focusIndex, 0).Min.Y);
        return top - 6f - ((configuration.TargetingIconHeight - DefaultIconHeight) * PixelsPerYalm) +
               configuration.TargetingIconOffsetY;
    }

    private void DrawMeasured(ImDrawListPtr drawList, BarCandidate candidate, float scale, int focusIndex, int slot)
    {
        DrawBar(drawList, candidate, scale);
        var index = (focusIndex * 3) + slot;
        previewExtents[index] = drawnMinimum.X <= drawnMaximum.X
            ? ((drawnMinimum - candidate.ScreenPosition) / scale, (drawnMaximum - candidate.ScreenPosition) / scale)
            : (Vector2.Zero, Vector2.Zero);
        previewMeasured[index] = true;
    }

    /// <summary>Height of the bar stack as set (HP, MP, LB with borders and spacing), px at scale 1.</summary>
    private float BarStackHeight()
    {
        var border = configuration.BorderThickness;
        var height = 0f;
        var any = false;
        if (configuration.ShowHpBar)
        {
            height = configuration.BarHeight;
            any = true;
        }

        if (configuration.ShowMpBar)
        {
            height = any ? height + (2f * border) + configuration.MpBarSpacing + configuration.MpBarHeight : configuration.MpBarHeight;
            any = true;
        }

        if (configuration.ShowLimitBreakBar)
        {
            height = any
                ? height + (2f * border) + configuration.LimitBreakBarSpacing + configuration.LimitBreakBarHeight
                : configuration.LimitBreakBarHeight;
            any = true;
        }

        return any ? height + border : 0f;
    }

    /// <summary>A simple figure (head + body) standing on the ground line.</summary>
    private static void DrawFigure(ImDrawListPtr drawList, float centerX, float ground, float height, Vector4 color, float uiScale)
    {
        var bodyHeight = height * 0.70f;
        var bodyWidth = height * 0.34f;
        var headRadius = height * 0.14f;
        var bodyMinimum = new Vector2(centerX - (bodyWidth * 0.5f), ground - bodyHeight);
        var bodyMaximum = new Vector2(centerX + (bodyWidth * 0.5f), ground - (1f * uiScale));
        var head = new Vector2(centerX, bodyMinimum.Y - (headRadius * 1.25f));
        var fill = ImGui.ColorConvertFloat4ToU32(color with { W = 0.22f });
        var line = ImGui.ColorConvertFloat4ToU32(color with { W = 0.85f });
        drawList.AddRectFilled(bodyMinimum, bodyMaximum, fill, bodyWidth * 0.5f);
        drawList.AddRect(bodyMinimum, bodyMaximum, line, bodyWidth * 0.5f, ImDrawFlags.None, 1.5f * uiScale);
        drawList.AddCircleFilled(head, headRadius, fill, 24);
        drawList.AddCircle(head, headRadius, line, 24, 1.5f * uiScale);
    }

    // ------------------------------------------------------------------ plates behind plugin windows

    /// <summary>
    /// Draws a plate with its targeting dot. Where a plugin window covers it, it is drawn once per uncovered piece
    /// with a clip rectangle, so it looks like it lies behind that window.
    /// </summary>
    private void DrawOccluded(ImDrawListPtr drawList, BarCandidate candidate, float uiScale)
    {
        var (minimum, maximum) = EstimateBounds(candidate, uiScale);
        var count = VisiblePieces(minimum, maximum);
        ResetDrawn();
        if (count < 0)
        {
            DrawPlate(drawList, candidate, uiScale);
        }
        else
        {
            for (var i = 0; i < count; i++)
            {
                var piece = pieces[i];
                drawList.PushClipRect(piece.Min, piece.Max, true);
                DrawPlate(drawList, candidate, uiScale);
                drawList.PopClipRect();
            }
        }

        if (drawnMinimum.X <= drawnMaximum.X)
            plateExtents[candidate.GameObjectId] = (drawnMinimum - candidate.ScreenPosition, drawnMaximum - candidate.ScreenPosition);
    }

    private void DrawPlate(ImDrawListPtr drawList, BarCandidate candidate, float uiScale)
    {
        if (candidate.HasFeet)
            DrawTargetingDot(drawList, candidate.FeetScreen, candidate.Role, uiScale);
        DrawBar(drawList, candidate, uiScale);
    }

    /// <summary>The role-colour dot at the feet of an enemy that has you as its target.</summary>
    private void DrawTargetingDot(ImDrawListPtr drawList, Vector2 feet, byte role, float uiScale)
    {
        var radius = Math.Max(1.5f, configuration.TargetingDotSize * uiScale);
        drawList.AddCircleFilled(feet, radius + Math.Max(1f, uiScale), ToColor(new Vector4(0f, 0f, 0f, 0.85f)), 20);
        drawList.AddCircleFilled(feet, radius, ToColor(GetRoleColor(role)), 20);
    }

    /// <summary>Where a plate will draw: last frame's measured extents around its anchor, or a generous guess.</summary>
    private (Vector2 Min, Vector2 Max) EstimateBounds(BarCandidate candidate, float uiScale)
    {
        var anchor = candidate.ScreenPosition;
        Vector2 minimum, maximum;
        if (plateExtents.TryGetValue(candidate.GameObjectId, out var extents))
        {
            var margin = new Vector2(4f * uiScale);
            minimum = anchor + extents.Min - margin;
            maximum = anchor + extents.Max + margin;
        }
        else
        {
            var debuffRow = DebuffSettings(candidate.Relation).Shown
                ? configuration.MaxDebuffs * ((configuration.DebuffIconSize * 0.75f) + configuration.DebuffSpacing)
                : 0f;
            var side = ((configuration.BarWidth * 0.5f) + configuration.JobIconSize + configuration.GuardSymbolSize +
                        debuffRow + 260f) * uiScale;
            var up = (configuration.SignMarkerSize + configuration.SignMarkerGap + Math.Abs(configuration.SignMarkerOffsetY) +
                      configuration.DebuffIconSize + 200f) * uiScale;
            var down = (BarStackHeight() + configuration.DebuffIconSize + configuration.DebuffRowGap + 200f) * uiScale;
            minimum = anchor - new Vector2(side, up);
            maximum = anchor + new Vector2(side, down);
        }

        if (candidate.HasFeet)
        {
            var radius = new Vector2(Math.Max(1.5f, configuration.TargetingDotSize * uiScale) + (2f * uiScale));
            minimum = Vector2.Min(minimum, candidate.FeetScreen - radius);
            maximum = Vector2.Max(maximum, candidate.FeetScreen + radius);
        }

        return (minimum, maximum);
    }

    /// <summary>
    /// The parts of the rectangle not covered by plugin windows, left in <see cref="pieces"/>. Returns -1 when
    /// nothing covers it (draw it normally) and 0 when it is fully covered.
    /// </summary>
    private int VisiblePieces(Vector2 minimum, Vector2 maximum)
    {
        var covered = false;
        foreach (var occluder in occluders)
        {
            if (Overlaps(occluder.Min, occluder.Max, minimum, maximum))
            {
                covered = true;
                break;
            }
        }

        if (!covered)
            return -1;

        pieces.Clear();
        pieces.Add((minimum, maximum));
        foreach (var occluder in occluders)
        {
            if (!Overlaps(occluder.Min, occluder.Max, minimum, maximum))
                continue;

            spare.Clear();
            foreach (var piece in pieces)
                Subtract(piece.Min, piece.Max, occluder.Min, occluder.Max, spare);
            (pieces, spare) = (spare, pieces);
            if (pieces.Count == 0)
                break;
        }

        return pieces.Count;
    }

    private static bool Overlaps(Vector2 aMin, Vector2 aMax, Vector2 bMin, Vector2 bMax)
    {
        return aMin.X < bMax.X && aMax.X > bMin.X && aMin.Y < bMax.Y && aMax.Y > bMin.Y;
    }

    /// <summary>Adds the parts of rectangle r outside rectangle c (up to four rectangles).</summary>
    private static void Subtract(Vector2 rMin, Vector2 rMax, Vector2 cMin, Vector2 cMax, List<(Vector2 Min, Vector2 Max)> output)
    {
        if (!Overlaps(rMin, rMax, cMin, cMax))
        {
            output.Add((rMin, rMax));
            return;
        }

        if (cMin.Y > rMin.Y)
            output.Add((rMin, new Vector2(rMax.X, cMin.Y)));                          // above
        if (cMax.Y < rMax.Y)
            output.Add((new Vector2(rMin.X, cMax.Y), rMax));                          // below
        var bandTop = Math.Max(rMin.Y, cMin.Y);
        var bandBottom = Math.Min(rMax.Y, cMax.Y);
        if (cMin.X > rMin.X)
            output.Add((new Vector2(rMin.X, bandTop), new Vector2(cMin.X, bandBottom)));  // left
        if (cMax.X < rMax.X)
            output.Add((new Vector2(cMax.X, bandTop), new Vector2(rMax.X, bandBottom)));  // right
    }

    /// <summary>
    /// Rectangles of the plugin windows on screen (every plugin's ImGui windows, these settings and their pop-ups
    /// too), read from ImGui's window list. Child windows, click-through overlays (no mouse input) and transparent
    /// overlays (no background), such as effect or marker layers, are left out: plates stay above the game, its
    /// effects and such overlays, and are only cut where a real plugin window is.
    /// </summary>
    private void RefreshWindowRects(Vector2 displaySize)
    {
        occluders.Clear();
        if (!configuration.HideBehindPluginWindows)
            return;

        try
        {
            var context = ImGui.GetCurrentContext();
            if (context.IsNull)
                return;

            const ImGuiWindowFlags Overlay = ImGuiWindowFlags.ChildWindow | ImGuiWindowFlags.NoMouseInputs |
                                             ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.DockNodeHost;
            var frame = context.FrameCount;
            ref var windows = ref context.Windows;
            var screenArea = Math.Max(1f, displaySize.X * displaySize.Y);
            for (var i = 0; i < windows.Size; i++)
            {
                var window = windows[i];
                if (window.IsNull || window.Hidden || window.IsFallbackWindow || window.LastFrameActive < frame - 1)
                    continue;
                if ((window.Flags & Overlay) != 0)
                    continue;

                var size = window.Size;
                if (size.X < 2f || size.Y < 2f || size.X * size.Y > screenArea * 0.9f)
                    continue;

                var position = window.Pos;
                occluders.Add((position, position + size));
            }
        }
        catch (Exception exception)
        {
            occluders.Clear();
            Plugin.Log.Debug(exception, "Plugin window rectangles unavailable.");
        }
    }

    private void ResetDrawn()
    {
        drawnMinimum = new Vector2(float.MaxValue);
        drawnMaximum = new Vector2(float.MinValue);
    }

    private void Grow(Vector2 minimum, Vector2 maximum)
    {
        drawnMinimum = Vector2.Min(drawnMinimum, minimum);
        drawnMaximum = Vector2.Max(drawnMaximum, maximum);
    }

    private static uint[] FindPreviewDebuffIcons()
    {
        var icons = new List<uint>();
        try
        {
            foreach (var status in Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Status>())
            {
                if (status.StatusCategory == 2 && status.Icon != 0 && status.RowId > 1000)
                    icons.Add(status.Icon);
                if (icons.Count == 3)
                    break;
            }
        }
        catch (Exception)
        {
            // no sample debuffs
        }

        return icons.ToArray();
    }

    public void Draw()
    {
        if (!Plugin.ClientState.IsPvP)
        {
            guardTrackingStates.Clear();
            guardActionTracker.Clear();
        }

        var testing = TestInWorld;
        var drawing = ShouldDraw();
        if (!drawing)
        {
            positionStates.Clear();
            drawnWithMarker.Clear();
            PlatesNow = 0;
            TargetingMeNow = 0;
            if (!testing)
                return;
        }

        // Plates are drawn on top of the game (its UI and effects) and of click-through plugin overlays, and cut where
        // a plugin window is, so the menus of other plugins and these settings cover them (RefreshWindowRects).
        var uiScale = Math.Max(0.5f, ImGuiHelpers.GlobalScale);
        var displaySize = ImGui.GetIO().DisplaySize;
        var drawList = ImGui.GetForegroundDrawList();
        RefreshWindowRects(displaySize);
        if (plateExtents.Count > 256)
            plateExtents.Clear();

        if (testing)
            DrawTestInWorld(drawList, uiScale);
        if (!drawing)
            return;

        var localPlayer = Plugin.ObjectTable.LocalPlayer;
        if (localPlayer is null || !localPlayer.IsValid())
        {
            positionStates.Clear();
            drawnWithMarker.Clear();
            PlatesNow = 0;
            TargetingMeNow = 0;
            return;
        }

        candidates.Clear();
        debuffPool.Clear();
        targetingMe.Clear();
        RefreshSignMarkers();
        AdvanceFrame();
        if (configuration.ShowLimitBreakBar)
            limitBreakReader.Refresh();

        var deltaTime = GetFrameDeltaTime();
        var currentTick = Environment.TickCount64;
        var currentTargetId = Plugin.TargetManager.Target?.GameObjectId ?? 0;
        var requireNativePlate =
            configuration.RequireNativeNameplatePresence &&
            nameplateTracker.HasCurrentSnapshot;

        foreach (var gameObject in Plugin.ObjectTable.CharacterManagerObjects)
        {
            if (gameObject is not IBattleChara actor ||
                actor.ObjectKind != ObjectKind.Pc)
            {
                continue;
            }

            if (actor.GameObjectId != localPlayer.GameObjectId)
            {
                if (actor.TargetObjectId == localPlayer.GameObjectId &&
                    actor.IsTargetable && !actor.IsDead &&
                    GetRelation(actor, localPlayer.GameObjectId, Plugin.ClientState.IsPvP) == PlayerRelation.Enemy)
                {
                    targetingMeIds.Add(actor.GameObjectId);
                    eventNames[actor.GameObjectId] = actor.Name.TextValue;
                }

                if (actor.IsCasting && actor.CastActionId == ElixirActionId)
                {
                    elixirIds.Add(actor.GameObjectId);
                    eventNames[actor.GameObjectId] = actor.Name.TextValue;
                }
            }

            if (configuration.TargetingMeIcons &&
                actor.GameObjectId != localPlayer.GameObjectId &&
                actor.TargetObjectId == localPlayer.GameObjectId &&
                actor.IsTargetable && !actor.IsDead && actor.CurrentHp > 0 &&
                GetRelation(actor, localPlayer.GameObjectId, Plugin.ClientState.IsPvP) == PlayerRelation.Enemy)
            {
                targetingMe.Add((actor.ClassJob.RowId, GetRole(actor)));
            }

            if (!TryCreateCandidate(
                    actor,
                    localPlayer,
                    currentTargetId,
                    requireNativePlate,
                    uiScale,
                    displaySize,
                    deltaTime,
                    currentTick,
                    out var candidate))
            {
                continue;
            }

            candidates.Add(candidate);
        }

        PruneTrackingStates();
        TrackEvents();
        PlatesNow = candidates.Count;
        PlatesPeak = Math.Max(PlatesPeak, PlatesNow);

        candidates.Sort(static (left, right) =>
        {
            if (left.Relation == PlayerRelation.LocalPlayer ||
                right.Relation == PlayerRelation.LocalPlayer)
            {
                if (left.Relation != right.Relation)
                    return left.Relation == PlayerRelation.LocalPlayer ? 1 : -1;
            }

            if (left.IsCurrentTarget != right.IsCurrentTarget)
                return left.IsCurrentTarget ? 1 : -1;

            return right.Distance.CompareTo(left.Distance);
        });

        drawnWithMarker.Clear();
        foreach (var candidate in candidates)
        {
            DrawOccluded(drawList, candidate, uiScale);
            if (candidate.MarkerIconId != 0)
                drawnWithMarker.Add(candidate.GameObjectId);
        }

        if (configuration.TargetingMeIcons && targetingMe.Count > 0)
            DrawTargetingMeIcons(drawList, localPlayer, uiScale);
    }

    /// <summary>Above my own head: the job icons of the enemies that have me targeted, centred, optional red glow.</summary>
    private void DrawTargetingMeIcons(ImDrawListPtr drawList, IBattleChara localPlayer, float uiScale)
    {
        var anchor = localPlayer.Position + (Vector3.UnitY * configuration.TargetingIconHeight);
        if (!Plugin.GameGui.WorldToScreen(anchor, out var screen, out var inView) || !inView)
            return;

        var size = Math.Max(8f, configuration.TargetingIconSize * uiScale);
        var gap = 4f * uiScale;
        var width = (targetingMe.Count * size) + ((targetingMe.Count - 1) * gap);
        var x = MathF.Round(screen.X - (width * 0.5f));
        var y = MathF.Round(screen.Y + (configuration.TargetingIconOffsetY * uiScale) - size);
        var glow = new Vector2(8f * uiScale);
        var count = VisiblePieces(new Vector2(x, y) - glow, new Vector2(x + width, y + size) + glow);
        if (count < 0)
        {
            DrawTargetingMeRow(drawList, x, y, size, gap, uiScale);
            return;
        }

        for (var i = 0; i < count; i++)
        {
            var piece = pieces[i];
            drawList.PushClipRect(piece.Min, piece.Max, true);
            DrawTargetingMeRow(drawList, x, y, size, gap, uiScale);
            drawList.PopClipRect();
        }
    }

    private void DrawTargetingMeRow(ImDrawListPtr drawList, float x, float y, float size, float gap, float uiScale)
    {
        foreach (var (jobId, _) in targetingMe)
        {
            var minimum = new Vector2(x, y);
            var maximum = minimum + new Vector2(size);
            if (configuration.TargetingMeIconGlow)
                DrawGlow(drawList, minimum, maximum, configuration.TargetingGlowColor, uiScale);
            DrawJobIcon(drawList, minimum + new Vector2(size * 0.5f), size, jobId, -1f);
            x += size + gap;
        }
    }

    /// <summary>Who started / stopped targeting you and who started an Elixir cast (Diagnostics events).</summary>
    private void TrackEvents()
    {
        TargetingMeNow = targetingMeIds.Count;
        if (configuration.RecordEvents)
        {
            foreach (var id in targetingMeIds)
            {
                if (!previousTargetingMeIds.Contains(id))
                    UiLog.Event($"{Name(id)} targets you.");
            }

            foreach (var id in previousTargetingMeIds)
            {
                if (!targetingMeIds.Contains(id))
                    UiLog.Event($"{Name(id)} stopped targeting you.");
            }

            foreach (var id in elixirIds)
            {
                if (!previousElixirIds.Contains(id))
                    UiLog.Event($"{Name(id)} casts Standard-issue Elixir.");
            }
        }

        (previousTargetingMeIds, targetingMeIds) = (targetingMeIds, previousTargetingMeIds);
        (previousElixirIds, elixirIds) = (elixirIds, previousElixirIds);
        targetingMeIds.Clear();
        elixirIds.Clear();
        if (eventNames.Count > 256)
            eventNames.Clear();

        string Name(ulong id) => eventNames.TryGetValue(id, out var name) ? name : "Someone";
    }

    private bool ShouldDraw()
    {
        if (!configuration.Enabled || !Plugin.ClientState.IsLoggedIn)
            return false;

        if (!Plugin.ClientState.IsPvP && !configuration.DrawOutsidePvPForPositioning)
            return false;

        if (configuration.HideWhenGameUiIsHidden && Plugin.GameGui.GameUiHidden)
            return false;

        return true;
    }

    private bool TryCreateCandidate(
        IBattleChara actor,
        IBattleChara localPlayer,
        ulong currentTargetId,
        bool requireNativePlate,
        float uiScale,
        Vector2 displaySize,
        float deltaTime,
        long currentTick,
        out BarCandidate candidate)
    {
        candidate = default;

        if (!actor.IsValid() || actor.GameObjectId == 0)
            return false;

        var guardInfo = UpdateGuardState(actor, currentTick);

        var relation = GetRelation(
            actor,
            localPlayer.GameObjectId,
            Plugin.ClientState.IsPvP);
        if (!ShouldShowRelation(relation))
            return false;

        if (relation != PlayerRelation.LocalPlayer && !actor.IsTargetable)
            return false;

        if (actor.IsDead || actor.MaxHp == 0 || actor.CurrentHp == 0)
            return false;

        if (requireNativePlate &&
            relation != PlayerRelation.LocalPlayer &&
            relation != PlayerRelation.Enemy &&
            !nameplateTracker.Contains(actor.GameObjectId, actor.EntityId))
        {
            return false;
        }

        var distance = Vector3.Distance(localPlayer.Position, actor.Position);
        if (distance > configuration.MaximumDistance)
            return false;

        // The actor's position is smoothed in the world (jumps, knock-ups, network jitter), then projected with
        // this frame's camera: camera turns move the plate exactly with the character, without lag.
        var visualPosition = GetVisualPosition(actor);
        var worldAnchor = StabilizeAnchor(
                              actor.GameObjectId,
                              visualPosition,
                              deltaTime,
                              actor.GameObjectId == localPlayer.GameObjectId) +
                          (Vector3.UnitY * (configuration.PlateUnderFeet
                              ? configuration.UnderFeetHeight
                              : configuration.WorldHeight));
        if (!Plugin.GameGui.WorldToScreen(
                worldAnchor,
                out var screenPosition,
                out var isInViewport) ||
            !isInViewport)
        {
            return false;
        }

        screenPosition.Y += configuration.ScreenOffsetY * uiScale;
        if (configuration.PlateUnderFeet)
            screenPosition.Y += configuration.UnderFeetGap * uiScale;   // bars start just under the feet
        if (configuration.PixelSnap)
            screenPosition = new Vector2(MathF.Round(screenPosition.X), MathF.Round(screenPosition.Y));

        // Conservative screen bounds of everything this plate can draw (bars, Guard icon with its glow, name).
        var halfWidth = configuration.BarWidth * uiScale * 0.5f;
        var borderThickness = configuration.BorderThickness * uiScale;
        var showHp = configuration.ShowHpBar;
        var showMp = configuration.ShowMpBar && actor.MaxMp > 0;
        var symbolSize = configuration.ShowGuardStateSymbol
            ? configuration.GuardSymbolSize * uiScale
            : 0f;
        var glowMargin = 8f * uiScale;
        float leftExtent, rightExtent, stackBottom;
        if (showHp || showMp)
        {
            leftExtent = halfWidth + borderThickness;
            rightExtent = halfWidth + borderThickness;
            if (configuration.ShowGuardStateSymbol)
                rightExtent += (configuration.GuardSymbolSpacing * uiScale) + symbolSize + glowMargin;

            stackBottom = screenPosition.Y + borderThickness + (symbolSize * 0.5f) + glowMargin;
            if (showHp)
                stackBottom += (configuration.BarHeight * uiScale) + borderThickness;
            stackBottom += ((configuration.LimitBreakBarSpacing + configuration.LimitBreakBarHeight) * uiScale) +
                           (borderThickness * 2f);
            if (showMp)
            {
                stackBottom +=
                    (configuration.MpBarSpacing * uiScale) +
                    (borderThickness * 2f) +
                    (configuration.MpBarHeight * uiScale);
            }
        }
        else if (configuration.ShowGuardStateSymbol)
        {
            leftExtent = rightExtent = (symbolSize * 0.5f) + glowMargin;
            stackBottom = screenPosition.Y + symbolSize + glowMargin;
        }
        else if (configuration.ShowNames)
        {
            leftExtent = rightExtent = halfWidth;
            stackBottom = screenPosition.Y;
        }
        else
        {
            return false;
        }

        var (debuffsShown, debuffPosition) = DebuffSettings(relation);
        if (debuffsShown && debuffPosition >= 3)
        {
            var row = (configuration.DebuffIconSize * 0.75f + configuration.DebuffSpacing) * configuration.MaxDebuffs * uiScale;
            leftExtent += row;
            rightExtent += row;
        }

        if (configuration.ShowJobIcon)
            leftExtent += (configuration.JobIconSpacing + configuration.JobIconSize) * uiScale;

        var offsetMargin = (Math.Max(Math.Abs(configuration.GuardIconOffsetX), Math.Abs(configuration.JobIconOffsetX)) +
                            Math.Max(Math.Abs(configuration.GuardIconOffsetY), Math.Abs(configuration.JobIconOffsetY)) + 40f) * uiScale;
        leftExtent += offsetMargin;
        rightExtent += offsetMargin;
        stackBottom += offsetMargin;

        if (configuration.PlateUnderFeet && configuration.ShowNames)
            stackBottom += 40f * uiScale;

        if (debuffsShown)
            stackBottom += (configuration.DebuffIconSize + configuration.DebuffRowGap) * uiScale;

        var topExtent = (debuffsShown ? (configuration.DebuffIconSize + configuration.DebuffRowGap) * uiScale : 0f) + offsetMargin + (symbolSize * 0.5f) + glowMargin + (configuration.ShowNames ? 40f * uiScale : 0f);
        if (screenPosition.X + rightExtent < 0 ||
            screenPosition.X - leftExtent > displaySize.X ||
            stackBottom < 0 ||
            screenPosition.Y - topExtent > displaySize.Y)
        {
            return false;
        }

        var limitBreak = configuration.ShowLimitBreakBar &&
                         limitBreakReader.TryGet(actor.Name.TextValue, out var charge)
            ? charge
            : -1f;

        var elixir = -1f;
        if (configuration.ShowElixirCast && actor.IsCasting && actor.CastActionId == ElixirActionId &&
            actor.TotalCastTime > 0f)
        {
            elixir = Math.Clamp(actor.CurrentCastTime / actor.TotalCastTime, 0f, 1f);
        }

        var debuffStart = debuffPool.Count;
        if (debuffsShown)
            CollectDebuffs(actor, localPlayer.EntityId, relation == PlayerRelation.Enemy && configuration.OnlyMyDebuffs);

        var targetsMe = relation == PlayerRelation.Enemy &&
                        actor.TargetObjectId == localPlayer.GameObjectId;
        var feet = Vector2.Zero;
        var hasFeet = false;
        if (targetsMe && configuration.TargetingMeDot &&
            Plugin.GameGui.WorldToScreen(
                StabilizedPositionOrRaw(actor.GameObjectId, visualPosition),
                out var feetScreen,
                out var feetInView) &&
            feetInView)
        {
            feet = new Vector2(MathF.Round(feetScreen.X), MathF.Round(feetScreen.Y + (configuration.TargetingDotOffsetY * uiScale)));
            hasFeet = true;
        }

        candidate = new BarCandidate(
            actor.Name.TextValue,
            actor.CurrentHp,
            actor.MaxHp,
            actor.CurrentMp,
            actor.MaxMp,
            actor.ShieldPercentage,
            screenPosition,
            distance,
            relation,
            guardInfo,
            limitBreak,
            actor.ClassJob.RowId,
            elixir,
            debuffStart,
            debuffPool.Count - debuffStart,
            targetsMe,
            GetRole(actor),
            signMarkers.TryGetValue(actor.GameObjectId, out var markerIcon) ? markerIcon : 0u,
            actor.GameObjectId,
            feet,
            hasFeet,
            actor.GameObjectId == currentTargetId);

        return true;
    }

    private bool ShouldShowRelation(PlayerRelation relation)
    {
        return relation switch
        {
            PlayerRelation.Enemy => configuration.ShowEnemies,
            PlayerRelation.Party => configuration.ShowPartyMembers,
            PlayerRelation.Alliance => configuration.ShowAllianceMembers,
            PlayerRelation.LocalPlayer => configuration.ShowLocalPlayer,
            _ => configuration.ShowOtherFriendlies,
        };
    }

    private static PlayerRelation GetRelation(
        IBattleChara actor,
        ulong localPlayerId,
        bool isPvP)
    {
        if (actor.GameObjectId == localPlayerId)
            return PlayerRelation.LocalPlayer;

        if (actor.StatusFlags.HasFlag(StatusFlags.PartyMember))
            return PlayerRelation.Party;

        if (actor.StatusFlags.HasFlag(StatusFlags.AllianceMember))
            return PlayerRelation.Alliance;

        if (actor.StatusFlags.HasFlag(StatusFlags.Hostile) || isPvP)
            return PlayerRelation.Enemy;

        return PlayerRelation.OtherFriendly;
    }

    private void DrawBar(ImDrawListPtr drawList, BarCandidate candidate, float uiScale)
    {
        var width = configuration.BarWidth * uiScale;
        var height = configuration.BarHeight * uiScale;
        var halfWidth = width * 0.5f;
        var borderThickness = configuration.BorderThickness * uiScale;
        var rounding = configuration.CornerRounding * uiScale;
        var borderOffset = new Vector2(borderThickness);
        ResetDrawn();       // everything below reports its rectangle (Grow): plate extents for the preview and the UI cut

        var showHp = configuration.ShowHpBar;
        var showMp = configuration.ShowMpBar && candidate.MaximumMp > 0;
        var left = candidate.ScreenPosition.X - halfWidth;
        var rowTop = candidate.ScreenPosition.Y;

        // The bar stack: HP (optional), then MP (optional). Without HP the MP bar takes its row.
        var hasStack = false;
        var stackMinimum = Vector2.Zero;
        var stackMaximum = Vector2.Zero;
        var firstRowCenterY = candidate.ScreenPosition.Y;
        var hpMinimum = Vector2.Zero;
        var hpMaximum = Vector2.Zero;
        var healthRatio = Math.Clamp(
            (float)candidate.CurrentHp / candidate.MaximumHp,
            0f,
            1f);

        if (showHp)
        {
            hpMinimum = new Vector2(left, rowTop);
            hpMaximum = hpMinimum + new Vector2(width, height);
            DrawFilledBar(
                drawList,
                hpMinimum,
                hpMaximum,
                healthRatio,
                GetRelationColor(candidate.Relation),
                configuration.EmptyHealthColor,
                borderThickness,
                rounding);

            if (configuration.ShowShields && candidate.ShieldPercentage > 0)
            {
                var shieldRatio = Math.Clamp(candidate.ShieldPercentage / 100f, 0f, 1f);
                var shieldMaximum = new Vector2(
                    hpMinimum.X + (width * shieldRatio),
                    hpMaximum.Y);

                // The barrier covers the HP fill under it (empty look first), then its own colour on top:
                // it never mixes with the HP colour; its opacity only blends with the empty bar.
                var shieldFlags = shieldRatio >= 0.999f ? ImDrawFlags.RoundCornersAll : ImDrawFlags.RoundCornersLeft;
                drawList.AddRectFilled(hpMinimum, shieldMaximum, ToColor(configuration.BorderColor), rounding, shieldFlags);
                drawList.AddRectFilled(hpMinimum, shieldMaximum, ToColor(configuration.EmptyHealthColor), rounding, shieldFlags);
                drawList.AddRectFilled(hpMinimum, shieldMaximum, ToColor(configuration.ShieldColor), rounding, shieldFlags);

                var hpEnd = hpMinimum.X + (width * healthRatio);
                if (healthRatio > 0f && hpEnd <= shieldMaximum.X + 0.5f)
                {
                    var markerWidth = Math.Max(1f, 2f * uiScale);
                    drawList.AddRectFilled(
                        new Vector2(hpEnd - (markerWidth * 0.5f), hpMinimum.Y),
                        new Vector2(hpEnd + (markerWidth * 0.5f), hpMaximum.Y),
                        ToColor(GetRelationColor(candidate.Relation) with { W = 1f }));
                }
            }

            stackMinimum = hpMinimum - borderOffset;
            stackMaximum = hpMaximum + borderOffset;
            firstRowCenterY = hpMinimum.Y + (height * 0.5f);
            hasStack = true;
            rowTop = stackMaximum.Y + (configuration.MpBarSpacing * uiScale) + borderThickness;
        }

        if (showMp)
        {
            var mpHeight = configuration.MpBarHeight * uiScale;
            var mpMinimum = new Vector2(left, rowTop);
            var mpMaximum = mpMinimum + new Vector2(width, mpHeight);
            var mpRatio = Math.Clamp(
                (float)candidate.CurrentMp / candidate.MaximumMp,
                0f,
                1f);

            DrawFilledBar(
                drawList,
                mpMinimum,
                mpMaximum,
                mpRatio,
                configuration.MpColor,
                configuration.EmptyMpColor,
                borderThickness,
                rounding);

            if (!hasStack)
            {
                stackMinimum = mpMinimum - borderOffset;
                firstRowCenterY = mpMinimum.Y + (mpHeight * 0.5f);
                hasStack = true;
            }

            stackMaximum = mpMaximum + borderOffset;
        }

        // Limit Break: under MP (or the first row when no other bar is shown); only where the CC team list has it.
        if (configuration.ShowLimitBreakBar && candidate.LimitBreak >= 0f)
        {
            var lbHeight = configuration.LimitBreakBarHeight * uiScale;
            var lbTop = hasStack
                ? stackMaximum.Y + (configuration.LimitBreakBarSpacing * uiScale) + borderThickness
                : candidate.ScreenPosition.Y;
            var lbMinimum = new Vector2(left, lbTop);
            var lbMaximum = lbMinimum + new Vector2(width, lbHeight);
            DrawLimitBreakBar(drawList, lbMinimum, lbMaximum, candidate.LimitBreak, borderThickness, rounding);

            if (!hasStack)
            {
                stackMinimum = lbMinimum - borderOffset;
                firstRowCenterY = lbMinimum.Y + (lbHeight * 0.5f);
                hasStack = true;
            }

            stackMaximum = lbMaximum + borderOffset;
        }

        if (hasStack)
            Grow(stackMinimum, stackMaximum);

        // Icons are centred on the bar stack (HP + MP + LB). With "match bar height" they are as tall as the stack.
        var stackHeight = hasStack ? stackMaximum.Y - stackMinimum.Y : 0f;
        var stackCenterY = hasStack ? (stackMinimum.Y + stackMaximum.Y) * 0.5f : 0f;

        // Guard: right of the bars, or alone centred on the anchor when no bar is shown. X / Y offsets on top.
        var hasIcon = false;
        var iconMinimum = Vector2.Zero;
        var iconMaximum = Vector2.Zero;
        if (configuration.ShowGuardStateSymbol)
        {
            var symbolSize = configuration.GuardIconAutoSize && hasStack
                ? stackHeight
                : Math.Max(6f, configuration.GuardSymbolSize * uiScale);
            var symbolSpacing = Math.Max(0f, configuration.GuardSymbolSpacing * uiScale);
            var guardOffset = new Vector2(configuration.GuardIconOffsetX, configuration.GuardIconOffsetY) * uiScale;
            var symbolCenter = (hasStack
                ? new Vector2(stackMaximum.X + symbolSpacing + (symbolSize * 0.5f), stackCenterY)
                : new Vector2(candidate.ScreenPosition.X, candidate.ScreenPosition.Y + (symbolSize * 0.5f))) + guardOffset;

            if (!configuration.UseGuardActionIcon ||
                !DrawGuardIcon(drawList, symbolCenter, symbolSize, candidate.Guard, uiScale))
            {
                DrawGuardSymbol(
                    drawList,
                    symbolCenter,
                    symbolSize,
                    candidate.Guard.State,
                    uiScale);
            }

            iconMinimum = symbolCenter - new Vector2(symbolSize * 0.5f);
            iconMaximum = symbolCenter + new Vector2(symbolSize * 0.5f);
            hasIcon = true;
            var guardGlow = new Vector2(((configuration.GuardIconBorderThickness * 0.5f) + 7f) * uiScale);
            Grow(iconMinimum - guardGlow, iconMaximum + guardGlow);
        }

        var hasJob = false;
        var jobMinimum = Vector2.Zero;
        var jobMaximum = Vector2.Zero;
        var extraTop = float.MaxValue;        // top of things drawn over the bars (crossed swords, debuffs over)
        var extraBottom = float.MinValue;     // bottom of debuffs drawn under the bars

        // Job icon (party list style by default): left of the bars, else left of the Guard icon. X / Y offsets on top.
        if (configuration.ShowJobIcon && candidate.JobId != 0)
        {
            var jobSize = configuration.JobIconAutoSize && hasStack
                ? stackHeight
                : Math.Max(6f, configuration.JobIconSize * uiScale);
            var jobSpacing = Math.Max(0f, configuration.JobIconSpacing * uiScale);
            var jobOffset = new Vector2(configuration.JobIconOffsetX, configuration.JobIconOffsetY) * uiScale;
            var jobCenter = (hasStack
                ? new Vector2(stackMinimum.X - jobSpacing - (jobSize * 0.5f), stackCenterY)
                : hasIcon
                    ? new Vector2(iconMinimum.X - jobSpacing - (jobSize * 0.5f), (iconMinimum.Y + iconMaximum.Y) * 0.5f)
                    : new Vector2(candidate.ScreenPosition.X, candidate.ScreenPosition.Y + (jobSize * 0.5f))) + jobOffset;
            if (candidate.TargetsMe && configuration.TargetingMeJobGlow)
            {
                DrawGlow(
                    drawList,
                    jobCenter - new Vector2(jobSize * 0.5f),
                    jobCenter + new Vector2(jobSize * 0.5f),
                    configuration.TargetingGlowColor,
                    uiScale);
            }

            DrawJobIcon(drawList, jobCenter, jobSize, candidate.JobId, candidate.ElixirProgress);
            if (candidate.TargetsMe && configuration.TargetingMeSwords)
            {
                var swordSize = jobSize * 0.75f;
                var swordCenterY = jobCenter.Y - (jobSize * 0.5f) - (swordSize * 0.5f) - (2f * uiScale);
                DrawCrossedSwords(
                    drawList,
                    new Vector2(jobCenter.X, swordCenterY),
                    swordSize,
                    uiScale);
                extraTop = Math.Min(extraTop, swordCenterY - (swordSize * 0.5f));
                var swordHalf = new Vector2((swordSize * 0.5f) + (2f * uiScale));
                Grow(new Vector2(jobCenter.X, swordCenterY) - swordHalf, new Vector2(jobCenter.X, swordCenterY) + swordHalf);
            }
            jobMinimum = jobCenter - new Vector2(jobSize * 0.5f);
            jobMaximum = jobCenter + new Vector2(jobSize * 0.5f);
            hasJob = true;
            var jobGlow = new Vector2(candidate.TargetsMe && configuration.TargetingMeJobGlow ? 7f * uiScale : 0f);
            Grow(jobMinimum - jobGlow, jobMaximum + jobGlow);
        }

        if (configuration.HighlightCurrentTarget && candidate.IsCurrentTarget && (hasStack || hasIcon))
        {
            if (hasStack)
            {
                var targetOffset = new Vector2(1.5f * uiScale);
                drawList.AddRect(
                    stackMinimum - targetOffset,
                    stackMaximum + targetOffset,
                    ToColor(configuration.TargetOutlineColor),
                    rounding + borderThickness + targetOffset.X,
                    ImDrawFlags.None,
                    2f * uiScale);
                Grow(stackMinimum - targetOffset - new Vector2(uiScale), stackMaximum + targetOffset + new Vector2(uiScale));
            }
            else
            {
                var targetOffset = new Vector2(
                    (configuration.GuardIconBorderThickness + 3f) * uiScale);
                drawList.AddRect(
                    iconMinimum - targetOffset,
                    iconMaximum + targetOffset,
                    ToColor(configuration.TargetOutlineColor),
                    ((iconMaximum.X - iconMinimum.X) * 0.12f) + targetOffset.X,
                    ImDrawFlags.None,
                    2f * uiScale);
                Grow(iconMinimum - targetOffset - new Vector2(uiScale), iconMaximum + targetOffset + new Vector2(uiScale));
            }
        }

        if (configuration.ShowHpPercent && showHp)
        {
            var hpText = FormatHp(candidate.CurrentHp, candidate.MaximumHp, healthRatio);
            var hpCenterX = candidate.ScreenPosition.X;
            var hpTop = hpMinimum.Y;
            DrawCrispText(
                drawList,
                hpFont,
                configuration.HpTextFontScale * textScale,
                hpText,
                textSize => new Vector2(hpCenterX - (textSize.X * 0.5f), hpTop + ((height - textSize.Y) * 0.5f)),
                uiScale);
        }

        // Debuffs: one row centred on the whole plate (job icon + bars + Guard icon); under the plate when it is
        // above the head, over it when it is under the feet. The row grows from the middle outwards.
        var overTop = float.MaxValue;
        var (debuffsShownHere, debuffPositionHere) = DebuffSettings(candidate.Relation);
        if (debuffsShownHere && candidate.DebuffCount > 0 && (hasStack || hasIcon || hasJob))
        {
            var plateLeft = float.MaxValue;
            var plateRight = float.MinValue;
            var plateTop = float.MaxValue;
            var plateBottom = float.MinValue;
            if (hasStack)
            {
                plateLeft = Math.Min(plateLeft, stackMinimum.X);
                plateRight = Math.Max(plateRight, stackMaximum.X);
                plateTop = Math.Min(plateTop, stackMinimum.Y);
                plateBottom = Math.Max(plateBottom, stackMaximum.Y);
            }

            if (hasIcon)
            {
                plateLeft = Math.Min(plateLeft, iconMinimum.X);
                plateRight = Math.Max(plateRight, iconMaximum.X);
                plateTop = Math.Min(plateTop, iconMinimum.Y);
                plateBottom = Math.Max(plateBottom, iconMaximum.Y);
            }

            if (hasJob)
            {
                plateLeft = Math.Min(plateLeft, jobMinimum.X);
                plateRight = Math.Max(plateRight, jobMaximum.X);
                plateTop = Math.Min(plateTop, jobMinimum.Y);
                plateBottom = Math.Max(plateBottom, jobMaximum.Y);
            }

            var debuffHeight = Math.Max(8f, configuration.DebuffIconSize * uiScale);
            var debuffWidth = debuffHeight * 0.75f;                     // status icons are 3:4
            var debuffGap = Math.Max(0f, configuration.DebuffSpacing * uiScale);
            var count = Math.Min(candidate.DebuffCount, Math.Max(1, (int)MathF.Round(configuration.MaxDebuffs)));
            var rowWidth = (count * debuffWidth) + ((count - 1) * debuffGap);
            var rowGap = configuration.DebuffRowGap * uiScale;
            var plateCenterY = hasStack ? (stackMinimum.Y + stackMaximum.Y) * 0.5f : (plateTop + plateBottom) * 0.5f;

            // 0 auto (under the plate above the head, over it under the feet), 1 under, 2 over: centred row.
            // 3 right of the Guard icon, growing to the right; 4 left of the job icon, growing to the left.
            var position = debuffPositionHere;
            if (position == 0)
                position = configuration.PlateUnderFeet ? 2 : 1;

            float rowX, rowY, step;
            switch (position)
            {
                case 3:
                    rowX = plateRight + rowGap;
                    rowY = plateCenterY - (debuffHeight * 0.5f);
                    step = debuffWidth + debuffGap;
                    break;
                case 4:
                    rowX = plateLeft - rowGap - debuffWidth;
                    rowY = plateCenterY - (debuffHeight * 0.5f);
                    step = -(debuffWidth + debuffGap);
                    break;
                case 2:
                    rowX = ((plateLeft + plateRight) * 0.5f) - (rowWidth * 0.5f);
                    rowY = plateTop - rowGap - debuffHeight;
                    step = debuffWidth + debuffGap;
                    break;
                default:
                    rowX = ((plateLeft + plateRight) * 0.5f) - (rowWidth * 0.5f);
                    rowY = plateBottom + rowGap;
                    step = debuffWidth + debuffGap;
                    break;
            }

            if (configuration.PixelSnap)
            {
                rowX = MathF.Round(rowX);
                rowY = MathF.Round(rowY);
            }

            if (position == 2)
                extraTop = Math.Min(extraTop, rowY);
            else if (position == 1)
                extraBottom = Math.Max(extraBottom, rowY + debuffHeight);

            for (var i = 0; i < count; i++)
            {
                DrawDebuff(
                    drawList,
                    debuffPool[candidate.DebuffStart + i],
                    new Vector2(rowX + (i * step), rowY),
                    debuffWidth,
                    debuffHeight,
                    uiScale);
            }

            var lastX = rowX + ((count - 1) * step);
            Grow(
                new Vector2(Math.Min(rowX, lastX) - (2f * uiScale), rowY - (2f * uiScale)),
                new Vector2(Math.Max(rowX, lastX) + debuffWidth + (2f * uiScale), rowY + debuffHeight + (2f * uiScale)));
        }

        if (configuration.ShowNames)
        {
            // Above the head: the name sits over the plate. Under the feet: under the plate, away from the body.
            var bottom = Math.Max(
                Math.Max(hasStack ? stackMaximum.Y : candidate.ScreenPosition.Y, hasIcon ? iconMaximum.Y : float.MinValue),
                extraBottom);
            var underFeet = configuration.PlateUnderFeet;
            var top = Math.Min(
                hasStack
                    ? stackMinimum.Y
                    : hasIcon
                        ? iconMinimum.Y
                        : candidate.ScreenPosition.Y,
                extraTop);
            var nameCenterX = candidate.ScreenPosition.X;
            DrawCrispText(
                drawList,
                nameFont,
                configuration.NameFontScale * textScale,
                candidate.Name,
                NameColor(candidate.Relation),
                textSize =>
                {
                    var namePosition = new Vector2(
                        nameCenterX - (textSize.X * 0.5f),
                        underFeet ? bottom + (2f * uiScale) : top - textSize.Y - (2f * uiScale));
                    if (!underFeet)
                        overTop = Math.Min(overTop, namePosition.Y);
                    return namePosition;
                },
                uiScale);
        }

        // Sign marker (attack 1-8 etc.) centred over the whole plate, above everything else.
        if (configuration.MoveSignMarkers && candidate.MarkerIconId != 0 && (hasStack || hasIcon || hasJob))
        {
            var markLeft = float.MaxValue;
            var markRight = float.MinValue;
            var upper = Math.Min(overTop, extraTop);
            if (hasStack)
            {
                markLeft = Math.Min(markLeft, stackMinimum.X);
                markRight = Math.Max(markRight, stackMaximum.X);
                upper = Math.Min(upper, stackMinimum.Y);
            }

            if (hasIcon)
            {
                markLeft = Math.Min(markLeft, iconMinimum.X);
                markRight = Math.Max(markRight, iconMaximum.X);
                upper = Math.Min(upper, iconMinimum.Y);
            }

            if (hasJob)
            {
                markLeft = Math.Min(markLeft, jobMinimum.X);
                markRight = Math.Max(markRight, jobMaximum.X);
                upper = Math.Min(upper, jobMinimum.Y);
            }

            var markerSize = Math.Max(8f, configuration.SignMarkerSize * uiScale);
            var markerMinimum = new Vector2(
                MathF.Round(((markLeft + markRight) * 0.5f) - (markerSize * 0.5f)),
                MathF.Round(upper - (configuration.SignMarkerGap * uiScale) - markerSize + (configuration.SignMarkerOffsetY * uiScale)));
            Grow(markerMinimum, markerMinimum + new Vector2(markerSize));
            try
            {
                if (Plugin.TextureProvider
                        .GetFromGameIcon(new GameIconLookup(candidate.MarkerIconId, false, true, null))
                        .TryGetWrap(out var markerTexture, out _) &&
                    markerTexture is not null)
                {
                    drawList.AddImage(markerTexture.Handle, markerMinimum, markerMinimum + new Vector2(markerSize));
                }
            }
            catch (Exception)
            {
                // marker icon not available
            }
        }
    }

    /// <summary>
    /// The PvP Guard hotbar icon. Border colour = state (ready / active / cooldown). Active: pulsing glow.
    /// Cooldown: dimmed icon, clock-wipe shade of the time still to recharge and the seconds left.
    /// Returns false while the icon texture is not loaded yet (the shield symbol is drawn instead).
    /// </summary>
    private bool DrawGuardIcon(
        ImDrawListPtr drawList,
        Vector2 center,
        float size,
        GuardInfo guard,
        float uiScale)
    {
        if (guardIconId == 0)
            return false;

        IDalamudTextureWrap? texture;
        try
        {
            if (!Plugin.TextureProvider
                    .GetFromGameIcon(new GameIconLookup(guardIconId, false, true, null))
                    .TryGetWrap(out texture, out _) ||
                texture is null)
            {
                return false;
            }
        }
        catch (Exception)
        {
            return false;
        }

        var half = new Vector2(size * 0.5f);
        var minimum = center - half;
        var maximum = center + half;
        var rounding = size * 0.12f;
        var thickness = Math.Max(1f, configuration.GuardIconBorderThickness * uiScale);
        var stateColor = guard.State switch
        {
            GuardState.Active => configuration.GuardActiveColor,
            GuardState.Cooldown => configuration.GuardCooldownColor,
            _ => configuration.GuardReadyColor,
        };

        if (guard.State == GuardState.Active && configuration.GuardActiveGlow)
        {
            var pulse = 0.70f + (0.30f * MathF.Sin(Environment.TickCount64 / 1000f * MathF.Tau * 2f));
            for (var ring = 3; ring >= 1; ring--)
            {
                var grow = (thickness * 0.5f) + (ring * 1.7f * uiScale);
                var glow = stateColor with { W = stateColor.W * pulse * (0.62f / (ring + 0.55f)) };
                drawList.AddRect(
                    minimum - new Vector2(grow),
                    maximum + new Vector2(grow),
                    ToColor(glow),
                    rounding + grow,
                    ImDrawFlags.None,
                    2.4f * uiScale);
            }
        }

        drawList.AddRectFilled(minimum, maximum, ToColor(new Vector4(0f, 0f, 0f, 0.9f)), rounding);
        var tint = guard.State == GuardState.Cooldown
            ? new Vector4(0.62f, 0.62f, 0.62f, 1f)
            : Vector4.One;
        drawList.AddImageRounded(
            texture.Handle,
            minimum,
            maximum,
            Vector2.Zero,
            Vector2.One,
            ToColor(tint),
            rounding);

        if (guard.State == GuardState.Cooldown)
        {
            DrawCooldownSweep(drawList, minimum, maximum, guard.CooldownFraction);
            if (configuration.ShowGuardCooldownText)
                DrawCountdown(drawList, center, size, guard.CooldownRemaining, uiScale);
        }

        drawList.AddRect(
            minimum,
            maximum,
            ToColor(stateColor),
            rounding,
            ImDrawFlags.None,
            thickness);
        return true;
    }

    /// <summary>Hotbar-style clock wipe: the shaded part is the share of the recast still to go.</summary>
    private static void DrawCooldownSweep(
        ImDrawListPtr drawList,
        Vector2 minimum,
        Vector2 maximum,
        float remainingFraction)
    {
        remainingFraction = Math.Clamp(remainingFraction, 0f, 1f);
        if (remainingFraction <= 0f)
            return;

        var center = (minimum + maximum) * 0.5f;
        var radius = ((maximum - minimum).Length() * 0.5f) + 1f;
        var shade = ToColor(new Vector4(0f, 0f, 0f, 0.58f));
        var start = (-MathF.PI / 2f) + (MathF.Tau * (1f - remainingFraction));
        var end = (-MathF.PI / 2f) + MathF.Tau;
        const float step = MathF.PI / 4f;

        drawList.PushClipRect(minimum, maximum, true);
        for (var from = start; from < end - 0.0001f; from += step)
        {
            var to = Math.Min(from + step, end);
            drawList.PathClear();
            drawList.PathLineTo(center);
            drawList.PathArcTo(center, radius, from, to, 8);
            drawList.PathFillConvex(shade);
        }

        drawList.PopClipRect();
    }

    private void DrawCountdown(
        ImDrawListPtr drawList,
        Vector2 center,
        float size,
        float seconds,
        float uiScale)
    {
        var text = Math.Max(1f, MathF.Ceiling(seconds)).ToString("0");
        var fontSize = size * 0.56f;
        IDisposable? pushedFont = null;
        try
        {
            if (countdownFont is { Available: true })
                pushedFont = countdownFont.Push();

            var font = ImGui.GetFont();
            var nativeSize = Math.Max(1f, ImGui.GetFontSize());
            var textSize = ImGui.CalcTextSize(text) * (fontSize / nativeSize);
            var position = center - (textSize * 0.5f);
            var outline = ToColor(new Vector4(0f, 0f, 0f, 0.95f));
            var offset = Math.Max(1f, 1.2f * uiScale);
            foreach (var direction in OutlineOffsets)
                drawList.AddText(font, fontSize, position + (direction * offset), outline, text);

            drawList.AddText(font, fontSize, position, ToColor(configuration.TextColor), text);
        }
        finally
        {
            pushedFont?.Dispose();
        }
    }

    /// <summary>ClassJob role: 1 tank, 4 healer, 2 / 3 damage.</summary>
    private static byte GetRole(IBattleChara actor)
    {
        try
        {
            return actor.ClassJob.Value.Role;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private Vector4 GetRoleColor(byte role)
    {
        return role switch
        {
            1 => configuration.TankColor,
            4 => configuration.HealerColor,
            _ => configuration.DpsColor,
        };
    }

    private static float Pulse()
    {
        return 0.65f + (0.35f * MathF.Sin(Environment.TickCount64 / 1000f * MathF.Tau * 1.6f));
    }

    private void DrawGlow(ImDrawListPtr drawList, Vector2 minimum, Vector2 maximum, Vector4 color, float uiScale)
    {
        var pulse = Pulse();
        for (var ring = 3; ring >= 1; ring--)
        {
            var grow = ring * 1.8f * uiScale;
            var glow = color with { W = color.W * pulse * (0.70f / (ring + 0.5f)) };
            drawList.AddRect(
                minimum - new Vector2(grow),
                maximum + new Vector2(grow),
                ToColor(glow),
                2f * uiScale + grow,
                ImDrawFlags.None,
                2.4f * uiScale);
        }

        drawList.AddRect(minimum, maximum, ToColor(color with { W = color.W * 0.9f }), 2f * uiScale, ImDrawFlags.None, 1.6f * uiScale);
    }

    /// <summary>Two crossed swords (vector drawing) centred at <paramref name="center"/>.</summary>
    private void DrawCrossedSwords(ImDrawListPtr drawList, Vector2 center, float size, float uiScale)
    {
        Glyphs.CrossedSwords(
            drawList,
            center,
            size,
            ToColor(configuration.TargetingGlowColor with { W = 1f }),
            ToColor(new Vector4(0f, 0f, 0f, 0.9f)),
            uiScale);
    }


    /// <summary>
    /// Sign markers (attack 1-8, bind, ignore, shapes) placed on players: MarkingController.Markers is in the order
    /// of the Marker sheet rows 1..17, whose Icon is the marker icon.
    /// </summary>
    private unsafe void RefreshSignMarkers()
    {
        signMarkers.Clear();
        if (!configuration.MoveSignMarkers || markerIcons.Length == 0)
            return;

        try
        {
            var controller = FFXIVClientStructs.FFXIV.Client.Game.UI.MarkingController.Instance();
            if (controller == null)
                return;

            var markers = controller->Markers;
            for (var i = 0; i < markers.Length && i < markerIcons.Length; i++)
            {
                var objectId = (ulong)markers[i];
                if (objectId != 0 && objectId != 0xE0000000 && markerIcons[i] != 0)
                    signMarkers[objectId] = markerIcons[i];
            }
        }
        catch (Exception exception)
        {
            Plugin.Log.Debug(exception, "Sign markers unavailable.");
        }
    }

    /// <summary>Hides the game's own sign marker over players whose plate this plugin drew last frame.</summary>
    public void HideNativeSignMarkers(IReadOnlyList<Dalamud.Game.Gui.NamePlate.INamePlateUpdateHandler> handlers)
    {
        if (!settings.MoveSignMarkers || drawnWithMarker.Count == 0)
            return;

        foreach (var handler in handlers)
        {
            if (handler.MarkerIconId != 0 && drawnWithMarker.Contains(handler.GameObjectId))
                handler.MarkerIconId = 0;
        }
    }

    /// <summary>Job icon sets: 0 = framed with the role colour (party list), 1 = gold glyph, 2 = light tile.</summary>
    private void DrawJobIcon(ImDrawListPtr drawList, Vector2 center, float size, uint jobId, float elixirProgress)
    {
        var baseId = configuration.JobIconStyle switch
        {
            1 => 62000u,
            2 => 62800u,
            _ => 62100u,
        };

        try
        {
            if (!Plugin.TextureProvider
                    .GetFromGameIcon(new GameIconLookup(baseId + jobId, false, true, null))
                    .TryGetWrap(out var texture, out _) ||
                texture is null)
            {
                return;
            }

            var half = new Vector2(size * 0.5f);
            var minimum = center - half;
            var maximum = center + half;
            drawList.AddImage(texture.Handle, minimum, maximum);

            // Elixir being drunk: the icon fills from the bottom up with the cast progress.
            if (elixirProgress >= 0f)
            {
                var top = maximum.Y - ((maximum.Y - minimum.Y) * elixirProgress);
                var fill = configuration.ElixirFillColor;
                drawList.AddRectFilled(new Vector2(minimum.X, top), maximum, ToColor(fill));
                drawList.AddLine(
                    new Vector2(minimum.X, top),
                    new Vector2(maximum.X, top),
                    ToColor(fill with { W = 1f }),
                    Math.Max(1f, size * 0.06f));
            }
        }
        catch (Exception)
        {
            // icon not available: draw nothing
        }
    }

    /// <summary>Empty inside, filled to the charge; when full a two-colour gradient that sweeps back and forth.</summary>
    private void DrawLimitBreakBar(
        ImDrawListPtr drawList,
        Vector2 minimum,
        Vector2 maximum,
        float charge,
        float borderThickness,
        float rounding)
    {
        drawList.AddRectFilled(minimum, maximum, ToColor(configuration.LimitBreakEmptyColor), rounding);

        if (charge >= 0.999f)
        {
            var phase = 0.5f + (0.5f * MathF.Sin(
                Environment.TickCount64 / 1000f * MathF.Tau * Math.Max(0.1f, configuration.LimitBreakBlinkSpeed)));
            var a = Vector4.Lerp(configuration.LimitBreakFullColorA, configuration.LimitBreakFullColorB, phase);
            var b = Vector4.Lerp(configuration.LimitBreakFullColorB, configuration.LimitBreakFullColorA, phase);
            drawList.AddRectFilledMultiColor(minimum, maximum, ToColor(a), ToColor(b), ToColor(b), ToColor(a));
        }
        else if (charge > 0f)
        {
            var fillMaximum = new Vector2(minimum.X + ((maximum.X - minimum.X) * charge), maximum.Y);
            drawList.AddRectFilled(minimum, fillMaximum, ToColor(configuration.LimitBreakColor), rounding);
        }

        if (borderThickness > 0f)
        {
            var half = new Vector2(borderThickness * 0.5f);
            drawList.AddRect(
                minimum - half,
                maximum + half,
                ToColor(configuration.BorderColor),
                rounding + half.X,
                ImDrawFlags.None,
                borderThickness);
        }
    }

    private void DrawFilledBar(
        ImDrawListPtr drawList,
        Vector2 minimum,
        Vector2 maximum,
        float fillRatio,
        Vector4 filledColor,
        Vector4 emptyColor,
        float borderThickness,
        float rounding)
    {
        var borderOffset = new Vector2(borderThickness);
        drawList.AddRectFilled(
            minimum - borderOffset,
            maximum + borderOffset,
            ToColor(configuration.BorderColor),
            rounding + borderThickness);
        drawList.AddRectFilled(
            minimum,
            maximum,
            ToColor(emptyColor),
            rounding);

        var fillMaximum = new Vector2(
            minimum.X + ((maximum.X - minimum.X) * fillRatio),
            maximum.Y);
        if (fillMaximum.X > minimum.X)
        {
            drawList.AddRectFilled(
                minimum,
                fillMaximum,
                ToColor(filledColor),
                rounding);
        }
    }

    private void DrawGuardSymbol(
        ImDrawListPtr drawList,
        Vector2 center,
        float size,
        GuardState state,
        float uiScale)
    {
        var halfSize = size * 0.5f;
        var top = center.Y - halfSize;
        var left = center.X - (halfSize * 0.78f);
        var right = center.X + (halfSize * 0.78f);
        var shoulder = top + (size * 0.18f);
        var lowerSide = top + (size * 0.62f);
        var bottom = top + size;

        var topLeft = new Vector2(left, shoulder);
        var topRight = new Vector2(right, shoulder);
        var lowerRight = new Vector2(right - (size * 0.08f), lowerSide);
        var tip = new Vector2(center.X, bottom);
        var lowerLeft = new Vector2(left + (size * 0.08f), lowerSide);

        var stateColor = state switch
        {
            GuardState.Active => configuration.GuardActiveColor,
            GuardState.Cooldown => configuration.GuardCooldownColor,
            _ => configuration.GuardReadyColor,
        };
        var fillColor = state == GuardState.Active
            ? stateColor
            : configuration.EmptyHealthColor;
        var fill = ToColor(fillColor);
        var outline = ToColor(stateColor);

        drawList.AddTriangleFilled(center, topLeft, topRight, fill);
        drawList.AddTriangleFilled(center, topRight, lowerRight, fill);
        drawList.AddTriangleFilled(center, lowerRight, tip, fill);
        drawList.AddTriangleFilled(center, tip, lowerLeft, fill);
        drawList.AddTriangleFilled(center, lowerLeft, topLeft, fill);

        var outlineThickness = Math.Max(1f, 1.5f * uiScale);
        drawList.AddLine(topLeft, topRight, outline, outlineThickness);
        drawList.AddLine(topRight, lowerRight, outline, outlineThickness);
        drawList.AddLine(lowerRight, tip, outline, outlineThickness);
        drawList.AddLine(tip, lowerLeft, outline, outlineThickness);
        drawList.AddLine(lowerLeft, topLeft, outline, outlineThickness);

        // Marks are placed from the shield's own centroid; the slash runs corner to corner with the same inset
        // from the outline at both ends (the old fixed slash stuck out at the lower left).
        var outlinePoints = new[] { topLeft, topRight, lowerRight, tip, lowerLeft };
        var centroid = PolygonCentroid(outlinePoints);
        if (state == GuardState.Ready)
        {
            drawList.AddLine(centroid + new Vector2(-0.22f, 0.01f) * size, centroid + new Vector2(-0.06f, 0.16f) * size, outline, outlineThickness);
            drawList.AddLine(centroid + new Vector2(-0.06f, 0.16f) * size, centroid + new Vector2(0.23f, -0.16f) * size, outline, outlineThickness);
        }
        else if (state == GuardState.Active)
        {
            drawList.AddCircleFilled(
                centroid,
                Math.Max(1.5f, size * 0.11f),
                ToColor(configuration.TextColor));
        }
        else
        {
            var direction = Vector2.Normalize(new Vector2(1f, -1f));
            if (TryChord(outlinePoints, centroid, direction, out var low, out var high))
            {
                var inset = size * 0.13f;
                if (high - low > 2f * inset)
                {
                    drawList.AddLine(
                        centroid + (direction * (low + inset)),
                        centroid + (direction * (high - inset)),
                        outline,
                        outlineThickness);
                }
            }
        }
    }

    private static Vector2 PolygonCentroid(Vector2[] points)
    {
        float area = 0f, cx = 0f, cy = 0f;
        for (var i = 0; i < points.Length; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Length];
            var cross = (a.X * b.Y) - (b.X * a.Y);
            area += cross;
            cx += (a.X + b.X) * cross;
            cy += (a.Y + b.Y) * cross;
        }

        if (Math.Abs(area) < 1e-6f)
            return points[0];
        area *= 0.5f;
        return new Vector2(cx / (6f * area), cy / (6f * area));
    }

    /// <summary>Where the line origin + t * direction enters and leaves the convex outline (t range).</summary>
    private static bool TryChord(Vector2[] points, Vector2 origin, Vector2 direction, out float low, out float high)
    {
        low = float.MaxValue;
        high = float.MinValue;
        for (var i = 0; i < points.Length; i++)
        {
            var a = points[i];
            var edge = points[(i + 1) % points.Length] - a;
            var denominator = (direction.X * edge.Y) - (direction.Y * edge.X);
            if (Math.Abs(denominator) < 1e-6f)
                continue;
            var offset = a - origin;
            var t = ((offset.X * edge.Y) - (offset.Y * edge.X)) / denominator;
            var u = ((offset.X * direction.Y) - (offset.Y * direction.X)) / denominator;
            if (u < -1e-4f || u > 1f + 1e-4f)
                continue;
            low = Math.Min(low, t);
            high = Math.Max(high, t);
        }

        return low < high;
    }

    private GuardInfo UpdateGuardState(IBattleChara actor, long currentTick)
    {
        guardTrackingStates.TryGetValue(actor.GameObjectId, out var tracked);

        var lastObservedUseAt = tracked.LastObservedUseAt;
        if (guardActionTracker.TryGetLatestUse(
                actor.EntityId,
                out var observedUseAt) &&
            observedUseAt > lastObservedUseAt)
        {
            lastObservedUseAt = observedUseAt;
        }

        var isDead = actor.IsDead || actor.CurrentHp == 0;
        if (isDead)
        {
            guardTrackingStates[actor.GameObjectId] = new GuardTrackingState(
                false,
                true,
                0,
                lastObservedUseAt,
                frameNumber);
            return new GuardInfo(GuardState.Ready, 0f, 0f, 0f);
        }

        var cooldownEndsAt = tracked.WasDead ? 0 : tracked.CooldownEndsAt;
        if (lastObservedUseAt > tracked.LastObservedUseAt &&
            cooldownEndsAt <= currentTick)
        {
            cooldownEndsAt = lastObservedUseAt +
                             (long)MathF.Round(guardRecastSeconds * 1_000f);
        }

        var isActive = TryGetGuardRemainingTime(actor, out var remainingTime);
        if (isActive && !tracked.WasActive && configuration.RecordEvents)
            UiLog.Event($"{actor.Name.TextValue} used Guard.");
        if (isActive && !tracked.WasActive && cooldownEndsAt <= currentTick)
        {
            var observedAge = GuardDurationSeconds - Math.Clamp(
                remainingTime,
                0f,
                GuardDurationSeconds);
            var remainingRecast = Math.Max(
                0f,
                guardRecastSeconds - observedAge);
            cooldownEndsAt = currentTick + (long)MathF.Round(remainingRecast * 1_000f);
        }

        var state = isActive
            ? GuardState.Active
            : currentTick < cooldownEndsAt
                ? GuardState.Cooldown
                : GuardState.Ready;

        guardTrackingStates[actor.GameObjectId] = new GuardTrackingState(
            isActive,
            false,
            cooldownEndsAt,
            lastObservedUseAt,
            frameNumber);

        var cooldownRemaining = Math.Max(0f, (cooldownEndsAt - currentTick) / 1_000f);
        return new GuardInfo(
            state,
            isActive ? remainingTime : 0f,
            cooldownRemaining,
            guardRecastSeconds > 0f ? Math.Clamp(cooldownRemaining / guardRecastSeconds, 0f, 1f) : 0f);
    }

    /// <summary>Detrimental statuses (Status sheet category 2) with an icon, in the game's order; stack icons follow the stack count.</summary>
    /// <summary>Debuffs on / position (0 auto .. 4 left of the job icon) for enemies, allies or yourself.</summary>
    private (bool Shown, int Position) DebuffSettings(PlayerRelation relation)
    {
        return relation switch
        {
            PlayerRelation.Enemy => (configuration.ShowDebuffs, configuration.DebuffPosition),
            PlayerRelation.LocalPlayer => (configuration.ShowOwnDebuffs, configuration.OwnDebuffPosition),
            _ => (configuration.ShowAllyDebuffs, configuration.AllyDebuffPosition),
        };
    }

    private void CollectDebuffs(IBattleChara actor, uint localEntityId, bool onlyMine)
    {
        foreach (var status in actor.StatusList)
        {
            if (status is null || status.StatusId == 0)
                continue;

            try
            {
                var data = status.GameData.Value;
                if (data.StatusCategory != 2 || data.Icon == 0)
                    continue;

                var mine = status.SourceId == localEntityId;
                if (onlyMine && !mine)
                    continue;

                var icon = data.Icon;
                if (data.MaxStacks > 0 && status.Param > 1)
                    icon += (uint)Math.Min(status.Param, data.MaxStacks) - 1;

                debuffPool.Add(new DebuffIcon(
                    icon,
                    float.IsFinite(status.RemainingTime) ? status.RemainingTime : 0f,
                    mine));
            }
            catch (Exception)
            {
                // status without sheet data: skip
            }
        }
    }

    private static bool TryGetGuardRemainingTime(
        IBattleChara actor,
        out float remainingTime)
    {
        foreach (var status in actor.StatusList)
        {
            if (status.StatusId != GuardStatusId &&
                status.StatusId != AlternateGuardStatusId)
                continue;

            remainingTime = float.IsFinite(status.RemainingTime)
                ? status.RemainingTime
                : GuardDurationSeconds;
            return true;
        }

        remainingTime = 0f;
        return false;
    }

    /// <summary>
    /// Exponential follow of the actor position in world space, frame-rate independent. Horizontal and vertical
    /// speeds are separate (vertical low = jumps barely move the plate); jumps over SnapYalms (teleports, respawn)
    /// are taken at once.
    /// </summary>
    private Vector3 StabilizedPositionOrRaw(ulong actorId, Vector3 rawPosition)
    {
        return configuration.StabilizePositions && positionStates.TryGetValue(actorId, out var state) &&
               state.LastSeenFrame == frameNumber
            ? state.Position
            : rawPosition;
    }

    /// <summary>
    /// The model's root as rendered (DrawObject), so the plate sits exactly on the drawn character; the game object
    /// position is the fallback.
    /// </summary>
    private static unsafe Vector3 GetVisualPosition(IBattleChara actor)
    {
        try
        {
            var gameObject = (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)actor.Address;
            if (gameObject != null && gameObject->DrawObject != null)
            {
                var root = gameObject->DrawObject->Position;
                var position = new Vector3(root.X, root.Y, root.Z);
                if (float.IsFinite(position.X) && float.IsFinite(position.Y) && float.IsFinite(position.Z) &&
                    Vector3.DistanceSquared(position, actor.Position) < 25f)
                {
                    return position;
                }
            }
        }
        catch (Exception)
        {
            // fall back to the game object position
        }

        return actor.Position;
    }

    private Vector3 StabilizeAnchor(ulong actorId, Vector3 rawPosition, float deltaTime, bool exact)
    {
        if (!configuration.StabilizePositions || exact)
        {
            positionStates[actorId] = new PositionState(rawPosition, frameNumber);
            return rawPosition;
        }

        var result = rawPosition;
        if (positionStates.TryGetValue(actorId, out var state) &&
            frameNumber - state.LastSeenFrame <= 5)
        {
            var delta = rawPosition - state.Position;
            var distance = delta.Length();
            if (float.IsFinite(distance) && distance < Math.Max(0.5f, configuration.SnapYalms))
            {
                var horizontal = configuration.FollowHorizontal >= 59.5f
                    ? 1f
                    : 1f - MathF.Exp(-Math.Max(0.1f, configuration.FollowHorizontal) * deltaTime);
                var vertical = configuration.FollowVertical >= 59.5f
                    ? 1f
                    : 1f - MathF.Exp(-Math.Max(0.1f, configuration.FollowVertical) * deltaTime);
                result = new Vector3(
                    state.Position.X + (delta.X * horizontal),
                    state.Position.Y + (delta.Y * vertical),
                    state.Position.Z + (delta.Z * horizontal));
            }
        }

        positionStates[actorId] = new PositionState(result, frameNumber);
        return result;
    }

    private void AdvanceFrame()
    {
        frameNumber++;
        if (frameNumber != 0)
            return;

        positionStates.Clear();
        guardTrackingStates.Clear();
        frameNumber = 1;
    }

    private void PruneTrackingStates()
    {
        if (frameNumber % 120 != 0)
            return;

        if (positionStates.Count > 0)
        {
            stalePositionIds.Clear();
            foreach (var (actorId, state) in positionStates)
            {
                if (frameNumber - state.LastSeenFrame > 300)
                    stalePositionIds.Add(actorId);
            }

            foreach (var actorId in stalePositionIds)
                positionStates.Remove(actorId);
        }

        if (guardTrackingStates.Count > 0)
        {
            staleGuardIds.Clear();
            foreach (var (actorId, state) in guardTrackingStates)
            {
                if (frameNumber - state.LastSeenFrame > 300)
                    staleGuardIds.Add(actorId);
            }

            foreach (var actorId in staleGuardIds)
                guardTrackingStates.Remove(actorId);
        }
    }

    private static float GetFrameDeltaTime()
    {
        var deltaTime = ImGui.GetIO().DeltaTime;
        if (!float.IsFinite(deltaTime) || deltaTime <= 0f)
            return 1f / 60f;

        return Math.Min(deltaTime, 0.1f);
    }

    private void DrawShadowedText(
        ImDrawListPtr drawList,
        Vector2 position,
        string text,
        float uiScale,
        float fontSize)
    {
        var font = ImGui.GetFont();
        drawList.AddText(
            font,
            fontSize,
            position + new Vector2(uiScale * Math.Max(1f, fontSize / Math.Max(1f, ImGui.GetFontSize()))),
            ToColor(new Vector4(0f, 0f, 0f, 0.95f)),
            text);
        drawList.AddText(font, fontSize, position, ToColor(configuration.TextColor), text);
    }

    /// <summary>One status icon; own debuffs can get a border; the time left is shown at the bottom.</summary>
    private void DrawDebuff(
        ImDrawListPtr drawList,
        DebuffIcon debuff,
        Vector2 minimum,
        float width,
        float height,
        float uiScale)
    {
        var maximum = minimum + new Vector2(width, height);
        try
        {
            if (Plugin.TextureProvider
                    .GetFromGameIcon(new GameIconLookup(debuff.IconId, false, true, null))
                    .TryGetWrap(out var texture, out _) &&
                texture is not null)
            {
                drawList.AddImage(texture.Handle, minimum, maximum);
            }
        }
        catch (Exception)
        {
            return;
        }

        if (debuff.Mine && configuration.HighlightMyDebuffs)
        {
            var thickness = Math.Max(1f, 1.6f * uiScale);
            var half = new Vector2(thickness * 0.5f);
            drawList.AddRect(
                minimum - half,
                maximum + half,
                ToColor(configuration.MyDebuffBorderColor),
                2f * uiScale,
                ImDrawFlags.None,
                thickness);
        }

        if (configuration.ShowDebuffTimers && debuff.Remaining > 0.05f && debuff.Remaining < 3600f)
        {
            var text = debuff.Remaining >= 60f
                ? Loc.Minutes(debuff.Remaining)
                : $"{MathF.Ceiling(debuff.Remaining):0}";
            var centerX = minimum.X + (width * 0.5f);
            var bottom = maximum.Y;
            DrawCrispText(
                drawList,
                debuffFont,
                (height * 0.46f) / BaseTextPixels,
                text,
                textSize => new Vector2(centerX - (textSize.X * 0.5f), bottom - (textSize.Y * 0.80f)),
                uiScale);
        }
    }

    /// <summary>
    /// Text in the game's Axis font built at the exact pixel size (sharp at any size). Until the font is built,
    /// the default font is scaled. Positions are rounded to whole pixels.
    /// </summary>
    private void DrawCrispText(
        ImDrawListPtr drawList,
        ScaledFont font,
        float scale,
        string text,
        Func<Vector2, Vector2> place,
        float uiScale)
    {
        DrawCrispText(drawList, font, scale, text, configuration.TextColor, place, uiScale);
    }

    private Vector4 NameColor(PlayerRelation relation) => relation switch
    {
        PlayerRelation.Enemy => configuration.EnemyNameColor,
        PlayerRelation.Party => configuration.PartyNameColor,
        PlayerRelation.Alliance => configuration.AllianceNameColor,
        PlayerRelation.LocalPlayer => configuration.MyNameColor,
        _ => configuration.FriendlyNameColor,
    };

    private void DrawCrispText(
        ImDrawListPtr drawList,
        ScaledFont font,
        float scale,
        string text,
        Vector4 color,
        Func<Vector2, Vector2> place,
        float uiScale)
    {
        scale = Math.Clamp(scale, 0.3f, 4f);
        IDisposable? pushed = null;
        try
        {
            var handle = font.Get(BaseTextPixels * scale);
            float size;
            if (handle is { Available: true })
            {
                pushed = handle.Push();
                size = ImGui.GetFontSize();
            }
            else
            {
                size = ImGui.GetFontSize() * scale;
            }

            var imFont = ImGui.GetFont();
            var textSize = ImGui.CalcTextSize(text) * (size / Math.Max(1f, ImGui.GetFontSize()));
            var position = place(textSize);
            position = new Vector2(MathF.Round(position.X), MathF.Round(position.Y));
            var shadow = MathF.Max(1f, MathF.Round(size / BaseTextPixels)) * Math.Max(1f, uiScale);
            Grow(position, position + textSize + new Vector2(shadow));
            drawList.AddText(imFont, size, position + new Vector2(shadow), ToColor(new Vector4(0f, 0f, 0f, 0.95f)), text);
            drawList.AddText(imFont, size, position, ToColor(color), text);
        }
        finally
        {
            pushed?.Dispose();
        }
    }

    private sealed class ScaledFont : IDisposable
    {
        private IFontHandle? handle;
        private float size;

        public IFontHandle? Get(float wanted)
        {
            wanted = MathF.Round(wanted);
            if (handle is null || Math.Abs(wanted - size) > 0.5f)
            {
                handle?.Dispose();
                size = wanted;
                try
                {
                    handle = Plugin.PluginInterface.UiBuilder.FontAtlas.NewGameFontHandle(
                        new GameFontStyle(GameFontFamily.Axis, wanted));
                }
                catch (Exception)
                {
                    handle = null;
                }
            }

            return handle;
        }

        public void Dispose()
        {
            handle?.Dispose();
        }
    }

    /// <summary>0 = 100%, 1 = 65000, 2 = 65000 / 65000, 3 = 65k, 4 = 65k / 65k.</summary>
    private string FormatHp(uint current, uint maximum, float ratio)
    {
        return configuration.HpTextFormat switch
        {
            1 => current.ToString("0"),
            2 => $"{current:0} / {maximum:0}",
            3 => Loc.ShortNumber(current),
            4 => $"{Loc.ShortNumber(current)} / {Loc.ShortNumber(maximum)}",
            _ => $"{MathF.Round(ratio * 100f):0}%",
        };

    }

    private Vector4 GetRelationColor(PlayerRelation relation)
    {
        return relation switch
        {
            PlayerRelation.Enemy => configuration.EnemyColor,
            PlayerRelation.Party => configuration.PartyColor,
            PlayerRelation.Alliance => configuration.AllianceColor,
            PlayerRelation.LocalPlayer => configuration.LocalPlayerColor,
            _ => configuration.FriendlyColor,
        };
    }

    private static uint ToColor(Vector4 color)
    {
        return ImGui.ColorConvertFloat4ToU32(color);
    }

    private enum PlayerRelation
    {
        Enemy,
        Party,
        Alliance,
        LocalPlayer,
        OtherFriendly,
    }

    private enum GuardState
    {
        Ready,
        Active,
        Cooldown,
    }

    private readonly record struct BarCandidate(
        string Name,
        uint CurrentHp,
        uint MaximumHp,
        uint CurrentMp,
        uint MaximumMp,
        byte ShieldPercentage,
        Vector2 ScreenPosition,
        float Distance,
        PlayerRelation Relation,
        GuardInfo Guard,
        float LimitBreak,
        uint JobId,
        float ElixirProgress,
        int DebuffStart,
        int DebuffCount,
        bool TargetsMe,
        byte Role,
        uint MarkerIconId,
        ulong GameObjectId,
        Vector2 FeetScreen,
        bool HasFeet,
        bool IsCurrentTarget);

    private readonly record struct DebuffIcon(
        uint IconId,
        float Remaining,
        bool Mine);

    private readonly record struct GuardInfo(
        GuardState State,
        float ActiveRemaining,
        float CooldownRemaining,
        float CooldownFraction);

    private readonly record struct PositionState(
        Vector3 Position,
        uint LastSeenFrame);

    private readonly record struct GuardTrackingState(
        bool WasActive,
        bool WasDead,
        long CooldownEndsAt,
        long LastObservedUseAt,
        uint LastSeenFrame);
}
