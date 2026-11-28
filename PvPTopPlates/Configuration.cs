using System;
using System.Numerics;
using Dalamud.Configuration;

namespace PvPTopPlates;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; }

    /// <summary>0 = game language, 1 English, 2 French, 3 German, 4 Japanese, 5 Russian.</summary>
    public int UiLanguage { get; set; }

    /// <summary>Settings window theme: 0 Midnight blue, 1 Black, 2 Light, 3 Crystal (Final Fantasy).</summary>
    public int Theme { get; set; }
    public bool PinPreview { get; set; } = true;
    public bool RecordEvents { get; set; }

    public bool Enabled { get; set; } = true;
    public bool DrawOutsidePvPForPositioning { get; set; }
    public bool HideWhenGameUiIsHidden { get; set; } = true;

    /// <summary>Plates are cut where a plugin window covers them; the game's own UI never hides them.</summary>
    public bool HideBehindPluginWindows { get; set; } = true;
    public bool RequireNativeNameplatePresence { get; set; } = true;

    public bool ShowEnemies { get; set; } = true;
    public bool ShowPartyMembers { get; set; } = true;
    public bool ShowAllianceMembers { get; set; } = true;
    public bool ShowLocalPlayer { get; set; }
    public bool ShowOtherFriendlies { get; set; }

    public bool ShowNames { get; set; }
    public bool ShowHpBar { get; set; } = true;
    public bool ShowHpPercent { get; set; }
    public bool ShowMpBar { get; set; } = true;
    public bool ShowGuardStateSymbol { get; set; } = true;
    public bool UseGuardActionIcon { get; set; } = true;
    public bool GuardActiveGlow { get; set; } = true;
    public bool ShowGuardCooldownText { get; set; } = true;
    public bool ShowShields { get; set; } = true;
    public bool ShowLimitBreakBar { get; set; } = true;
    public bool ShowJobIcon { get; set; } = true;
    public bool JobIconAutoSize { get; set; } = true;
    public bool GuardIconAutoSize { get; set; } = true;
    public int HpTextFormat { get; set; }
    public int JobIconStyle { get; set; }
    public bool HighlightCurrentTarget { get; set; } = true;
    public bool StabilizePositions { get; set; } = true;

    public float MaximumDistance { get; set; } = 50f;
    public float WorldHeight { get; set; } = 2.15f;
    public bool PlateUnderFeet { get; set; }
    public float UnderFeetHeight { get; set; }
    public float UnderFeetGap { get; set; } = 6f;
    public float ScreenOffsetY { get; set; }
    public float BarWidth { get; set; } = 105f;
    public float BarHeight { get; set; } = 11f;
    public float MpBarHeight { get; set; } = 4f;
    public float MpBarSpacing { get; set; } = 2f;
    public float GuardSymbolSize { get; set; } = 16f;
    public float GuardSymbolSpacing { get; set; } = 5f;
    public float GuardIconBorderThickness { get; set; } = 2f;
    public float JobIconSize { get; set; } = 28f;
    public float JobIconOffsetX { get; set; }
    public float JobIconOffsetY { get; set; }
    public float GuardIconOffsetX { get; set; }
    public float GuardIconOffsetY { get; set; }
    public float NameFontScale { get; set; } = 1f;
    public float HpTextFontScale { get; set; } = 1f;
    public float JobIconSpacing { get; set; } = 4f;
    public float LimitBreakBarHeight { get; set; } = 4f;
    public float LimitBreakBarSpacing { get; set; } = 2f;
    public float LimitBreakBlinkSpeed { get; set; } = 1.5f;
    public float BorderThickness { get; set; } = 2f;
    public float CornerRounding { get; set; } = 2f;
    public float StabilizationDeadZone { get; set; } = 0.60f;
    public float StabilizationResponse { get; set; } = 18f;
    public float StabilizationSnapDistance { get; set; } = 32f;
    public float FollowHorizontal { get; set; } = 60f;
    public float FollowVertical { get; set; } = 60f;
    public bool ShowElixirCast { get; set; } = true;
    public bool ShowDebuffs { get; set; } = true;
    public bool TargetingMeDot { get; set; } = true;
    public bool MoveSignMarkers { get; set; } = true;
    public float SignMarkerSize { get; set; } = 32f;
    public float SignMarkerGap { get; set; } = 2f;
    public float SignMarkerOffsetY { get; set; }
    public bool TargetingMeIcons { get; set; }
    public bool TargetingMeIconGlow { get; set; } = true;
    public bool TargetingMeJobGlow { get; set; }
    public bool TargetingMeSwords { get; set; }
    public float TargetingDotSize { get; set; } = 5f;
    public float TargetingDotOffsetY { get; set; }
    public float TargetingIconSize { get; set; } = 28f;
    public float TargetingIconHeight { get; set; } = 2.6f;
    public float TargetingIconOffsetY { get; set; }
    public Vector4 TankColor { get; set; } = new(0.25f, 0.50f, 1f, 1f);
    public Vector4 HealerColor { get; set; } = new(0.25f, 0.85f, 0.35f, 1f);
    public Vector4 DpsColor { get; set; } = new(0.92f, 0.22f, 0.22f, 1f);
    public Vector4 TargetingGlowColor { get; set; } = new(1f, 0.15f, 0.12f, 1f);
    public bool OnlyMyDebuffs { get; set; }
    public bool HighlightMyDebuffs { get; set; }
    public int DebuffPosition { get; set; }

    /// <summary>Debuffs on allies (party, alliance, other friendly players) and on yourself; positions as for enemies.</summary>
    public bool ShowAllyDebuffs { get; set; }
    public int AllyDebuffPosition { get; set; }
    public bool ShowOwnDebuffs { get; set; }
    public int OwnDebuffPosition { get; set; }
    public bool ShowDebuffTimers { get; set; } = true;
    public float DebuffIconSize { get; set; } = 26f;
    public float DebuffSpacing { get; set; } = 2f;
    public float DebuffRowGap { get; set; } = 3f;
    public float MaxDebuffs { get; set; } = 10f;
    public Vector4 MyDebuffBorderColor { get; set; } = new(1f, 0.82f, 0.24f, 1f);
    public Vector4 ElixirFillColor { get; set; } = new(0.30f, 1f, 0.55f, 0.55f);
    public float SnapYalms { get; set; } = 4f;
    public bool PixelSnap { get; set; } = true;

    public Vector4 EnemyColor { get; set; } = new(0.93f, 0.20f, 0.25f, 0.96f);
    public Vector4 PartyColor { get; set; } = new(0.30f, 0.84f, 0.52f, 0.96f);
    public Vector4 AllianceColor { get; set; } = new(0.28f, 0.66f, 0.96f, 0.96f);
    public Vector4 FriendlyColor { get; set; } = new(0.42f, 0.78f, 0.91f, 0.96f);
    public Vector4 LocalPlayerColor { get; set; } = new(0.96f, 0.72f, 0.30f, 0.96f);
    public Vector4 EmptyHealthColor { get; set; } = new(0.035f, 0.035f, 0.045f, 0.88f);
    public Vector4 MpColor { get; set; } = new(0.30f, 0.46f, 0.98f, 0.96f);
    public Vector4 EmptyMpColor { get; set; } = new(0.025f, 0.035f, 0.09f, 0.88f);
    public Vector4 GuardReadyColor { get; set; } = new(0.86f, 0.86f, 0.86f, 0.90f);
    public Vector4 GuardActiveColor { get; set; } = new(0.25f, 1f, 0.40f, 1f);
    public Vector4 GuardCooldownColor { get; set; } = new(0.95f, 0.25f, 0.25f, 0.95f);
    public Vector4 LimitBreakColor { get; set; } = new(0.35f, 0.72f, 1f, 0.96f);
    public Vector4 LimitBreakEmptyColor { get; set; } = new(0f, 0f, 0f, 0f);
    public Vector4 LimitBreakFullColorA { get; set; } = new(1f, 0.85f, 0.25f, 1f);
    public Vector4 LimitBreakFullColorB { get; set; } = new(1f, 0.40f, 0.12f, 1f);
    public Vector4 BorderColor { get; set; } = new(0.01f, 0.01f, 0.015f, 0.98f);
    public Vector4 ShieldColor { get; set; } = new(1f, 0.82f, 0.30f, 0.95f);
    public Vector4 TextColor { get; set; } = new(1f, 1f, 1f, 1f);

    /// <summary>Name colours per player group (since config version 5; TextColor is the {HP} / timer text).</summary>
    public Vector4 EnemyNameColor { get; set; } = new(1f, 1f, 1f, 1f);
    public Vector4 PartyNameColor { get; set; } = new(1f, 1f, 1f, 1f);
    public Vector4 AllianceNameColor { get; set; } = new(1f, 1f, 1f, 1f);
    public Vector4 FriendlyNameColor { get; set; } = new(1f, 1f, 1f, 1f);
    public Vector4 MyNameColor { get; set; } = new(1f, 1f, 1f, 1f);
    public Vector4 TargetOutlineColor { get; set; } = new(1f, 0.82f, 0.24f, 1f);

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }

    /// <summary>A detached copy: the settings preview changes switches on it without touching the saved settings.</summary>
    public Configuration Clone() => (Configuration)MemberwiseClone();

    /// <summary>
    /// Version 1 (custom build): the Guard colours are now the borders of the Guard action icon.
    /// Colours still at the old defaults move to the new ones; colours the user picked stay.
    /// </summary>
    public bool Migrate()
    {
        if (Version >= 1)
            return false;

        var defaults = new Configuration();
        if (GuardReadyColor == new Vector4(0.28f, 0.90f, 0.62f, 1f))
            GuardReadyColor = defaults.GuardReadyColor;
        if (GuardActiveColor == new Vector4(1f, 0.78f, 0.20f, 1f))
            GuardActiveColor = defaults.GuardActiveColor;
        if (GuardCooldownColor == new Vector4(0.42f, 0.44f, 0.50f, 0.92f))
            GuardCooldownColor = defaults.GuardCooldownColor;

        Version = 1;
        return true;
    }

    /// <summary>Version 2: vertical follow became exact by default (the plate rises with jumps like the name).</summary>
    public bool Migrate2()
    {
        if (Version >= 2)
            return false;

        if (Math.Abs(FollowVertical - 5f) < 0.01f)
            FollowVertical = 60f;

        Version = 2;
        return true;
    }

    /// <summary>Version 3: the border around own debuffs is off by default.</summary>
    public bool Migrate3()
    {
        if (Version >= 3)
            return false;

        HighlightMyDebuffs = false;
        Version = 3;
        return true;
    }

    /// <summary>Version 4: sideways follow exact by default (no lag / shaking while sprinting).</summary>
    public bool Migrate4()
    {
        if (Version >= 4)
            return false;

        if (Math.Abs(FollowHorizontal - 30f) < 0.01f)
            FollowHorizontal = 60f;

        Version = 4;
        return true;
    }

    /// <summary>Version 5: names got their own colour per group, starting from the old shared text colour.</summary>
    public bool Migrate5()
    {
        if (Version >= 5)
            return false;

        EnemyNameColor = TextColor;
        PartyNameColor = TextColor;
        AllianceNameColor = TextColor;
        FriendlyNameColor = TextColor;
        MyNameColor = TextColor;
        Version = 5;
        return true;
    }

    public void ResetToDefaults()
    {
        var defaults = new Configuration();

        UiLanguage = defaults.UiLanguage;
        Theme = defaults.Theme;
        PinPreview = defaults.PinPreview;
        RecordEvents = defaults.RecordEvents;
        Enabled = defaults.Enabled;
        DrawOutsidePvPForPositioning = defaults.DrawOutsidePvPForPositioning;
        HideWhenGameUiIsHidden = defaults.HideWhenGameUiIsHidden;
        HideBehindPluginWindows = defaults.HideBehindPluginWindows;
        RequireNativeNameplatePresence = defaults.RequireNativeNameplatePresence;

        ShowEnemies = defaults.ShowEnemies;
        ShowPartyMembers = defaults.ShowPartyMembers;
        ShowAllianceMembers = defaults.ShowAllianceMembers;
        ShowLocalPlayer = defaults.ShowLocalPlayer;
        ShowOtherFriendlies = defaults.ShowOtherFriendlies;

        ShowNames = defaults.ShowNames;
        ShowHpBar = defaults.ShowHpBar;
        ShowHpPercent = defaults.ShowHpPercent;
        ShowMpBar = defaults.ShowMpBar;
        ShowGuardStateSymbol = defaults.ShowGuardStateSymbol;
        UseGuardActionIcon = defaults.UseGuardActionIcon;
        GuardActiveGlow = defaults.GuardActiveGlow;
        ShowGuardCooldownText = defaults.ShowGuardCooldownText;
        ShowShields = defaults.ShowShields;
        ShowLimitBreakBar = defaults.ShowLimitBreakBar;
        ShowJobIcon = defaults.ShowJobIcon;
        JobIconAutoSize = defaults.JobIconAutoSize;
        GuardIconAutoSize = defaults.GuardIconAutoSize;
        HpTextFormat = defaults.HpTextFormat;
        JobIconStyle = defaults.JobIconStyle;
        HighlightCurrentTarget = defaults.HighlightCurrentTarget;
        StabilizePositions = defaults.StabilizePositions;

        MaximumDistance = defaults.MaximumDistance;
        WorldHeight = defaults.WorldHeight;
        PlateUnderFeet = defaults.PlateUnderFeet;
        UnderFeetHeight = defaults.UnderFeetHeight;
        UnderFeetGap = defaults.UnderFeetGap;
        ScreenOffsetY = defaults.ScreenOffsetY;
        BarWidth = defaults.BarWidth;
        BarHeight = defaults.BarHeight;
        MpBarHeight = defaults.MpBarHeight;
        MpBarSpacing = defaults.MpBarSpacing;
        GuardSymbolSize = defaults.GuardSymbolSize;
        GuardSymbolSpacing = defaults.GuardSymbolSpacing;
        GuardIconBorderThickness = defaults.GuardIconBorderThickness;
        JobIconSize = defaults.JobIconSize;
        JobIconOffsetX = defaults.JobIconOffsetX;
        JobIconOffsetY = defaults.JobIconOffsetY;
        GuardIconOffsetX = defaults.GuardIconOffsetX;
        GuardIconOffsetY = defaults.GuardIconOffsetY;
        NameFontScale = defaults.NameFontScale;
        HpTextFontScale = defaults.HpTextFontScale;
        JobIconSpacing = defaults.JobIconSpacing;
        LimitBreakBarHeight = defaults.LimitBreakBarHeight;
        LimitBreakBarSpacing = defaults.LimitBreakBarSpacing;
        LimitBreakBlinkSpeed = defaults.LimitBreakBlinkSpeed;
        BorderThickness = defaults.BorderThickness;
        CornerRounding = defaults.CornerRounding;
        StabilizationDeadZone = defaults.StabilizationDeadZone;
        StabilizationResponse = defaults.StabilizationResponse;
        StabilizationSnapDistance = defaults.StabilizationSnapDistance;
        FollowHorizontal = defaults.FollowHorizontal;
        FollowVertical = defaults.FollowVertical;
        SnapYalms = defaults.SnapYalms;
        PixelSnap = defaults.PixelSnap;
        ShowElixirCast = defaults.ShowElixirCast;
        ShowDebuffs = defaults.ShowDebuffs;
        TargetingMeDot = defaults.TargetingMeDot;
        MoveSignMarkers = defaults.MoveSignMarkers;
        SignMarkerSize = defaults.SignMarkerSize;
        SignMarkerGap = defaults.SignMarkerGap;
        SignMarkerOffsetY = defaults.SignMarkerOffsetY;
        TargetingMeIcons = defaults.TargetingMeIcons;
        TargetingMeIconGlow = defaults.TargetingMeIconGlow;
        TargetingMeJobGlow = defaults.TargetingMeJobGlow;
        TargetingMeSwords = defaults.TargetingMeSwords;
        TargetingDotSize = defaults.TargetingDotSize;
        TargetingDotOffsetY = defaults.TargetingDotOffsetY;
        TargetingIconSize = defaults.TargetingIconSize;
        TargetingIconHeight = defaults.TargetingIconHeight;
        TargetingIconOffsetY = defaults.TargetingIconOffsetY;
        TankColor = defaults.TankColor;
        HealerColor = defaults.HealerColor;
        DpsColor = defaults.DpsColor;
        TargetingGlowColor = defaults.TargetingGlowColor;
        OnlyMyDebuffs = defaults.OnlyMyDebuffs;
        HighlightMyDebuffs = defaults.HighlightMyDebuffs;
        DebuffPosition = defaults.DebuffPosition;
        ShowAllyDebuffs = defaults.ShowAllyDebuffs;
        AllyDebuffPosition = defaults.AllyDebuffPosition;
        ShowOwnDebuffs = defaults.ShowOwnDebuffs;
        OwnDebuffPosition = defaults.OwnDebuffPosition;
        ShowDebuffTimers = defaults.ShowDebuffTimers;
        DebuffIconSize = defaults.DebuffIconSize;
        DebuffSpacing = defaults.DebuffSpacing;
        DebuffRowGap = defaults.DebuffRowGap;
        MaxDebuffs = defaults.MaxDebuffs;
        MyDebuffBorderColor = defaults.MyDebuffBorderColor;
        ElixirFillColor = defaults.ElixirFillColor;

        EnemyColor = defaults.EnemyColor;
        PartyColor = defaults.PartyColor;
        AllianceColor = defaults.AllianceColor;
        FriendlyColor = defaults.FriendlyColor;
        LocalPlayerColor = defaults.LocalPlayerColor;
        EmptyHealthColor = defaults.EmptyHealthColor;
        MpColor = defaults.MpColor;
        EmptyMpColor = defaults.EmptyMpColor;
        GuardReadyColor = defaults.GuardReadyColor;
        GuardActiveColor = defaults.GuardActiveColor;
        GuardCooldownColor = defaults.GuardCooldownColor;
        LimitBreakColor = defaults.LimitBreakColor;
        LimitBreakEmptyColor = defaults.LimitBreakEmptyColor;
        LimitBreakFullColorA = defaults.LimitBreakFullColorA;
        LimitBreakFullColorB = defaults.LimitBreakFullColorB;
        BorderColor = defaults.BorderColor;
        ShieldColor = defaults.ShieldColor;
        TextColor = defaults.TextColor;
        EnemyNameColor = defaults.EnemyNameColor;
        PartyNameColor = defaults.PartyNameColor;
        AllianceNameColor = defaults.AllianceNameColor;
        FriendlyNameColor = defaults.FriendlyNameColor;
        MyNameColor = defaults.MyNameColor;
        TargetOutlineColor = defaults.TargetOutlineColor;
    }
}
