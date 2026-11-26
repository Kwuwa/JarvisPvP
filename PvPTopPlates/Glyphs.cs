using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace PvPTopPlates;

/// <summary>Small vector drawings shared by the plates and the settings window.</summary>
internal static class Glyphs
{
    /// <summary>Two crossed swords centred at <paramref name="center"/> (blade, crossguard, pommel; dark outline).</summary>
    public static void CrossedSwords(ImDrawListPtr drawList, Vector2 center, float size, uint color, uint outline, float uiScale)
    {
        var half = size * 0.5f;
        var blade = System.Math.Max(1.5f, size * 0.11f);
        foreach (var direction in new[] { 1f, -1f })
        {
            var tip = center + new Vector2(direction * half, -half);
            var hilt = center + new Vector2(-direction * half * 0.62f, half * 0.62f);
            var pommel = center + new Vector2(-direction * half, half);
            var axis = Vector2.Normalize(tip - pommel);
            var normal = new Vector2(-axis.Y, axis.X);
            var guardA = hilt + (normal * size * 0.20f);
            var guardB = hilt - (normal * size * 0.20f);
            foreach (var (width, col) in new[] { (blade + (2f * uiScale), outline), (blade, color) })
            {
                drawList.AddLine(hilt, tip, col, width);
                drawList.AddLine(guardA, guardB, col, width);
                drawList.AddLine(hilt, pommel, col, width * 0.9f);
            }
        }
    }
}
