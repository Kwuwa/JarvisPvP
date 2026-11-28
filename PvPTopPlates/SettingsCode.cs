using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text.Json;

namespace PvPTopPlates;

/// <summary>
/// Settings codes for sharing: "JPVP1:" + base64 of the deflated JSON of every plate setting. The language, theme and
/// layout of the settings window are personal and never part of a code. Imported values are checked by type and
/// range, unknown or broken entries are skipped.
/// </summary>
internal static class SettingsCode
{
    public const string Prefix = "JPVP1:";
    private const int MaxJsonBytes = 64 * 1024;
    private const float FloatLimit = 5_000f;

    private static readonly HashSet<string> Personal = new(StringComparer.Ordinal)
    {
        nameof(Configuration.Version),
        nameof(Configuration.UiLanguage),
        nameof(Configuration.Theme),
        nameof(Configuration.PinPreview),
        nameof(Configuration.RecordEvents),
    };

    /// <summary>Highest valid value of the settings that pick an entry of a list.</summary>
    private static readonly Dictionary<string, int> IntMaximum = new(StringComparer.Ordinal)
    {
        [nameof(Configuration.HpTextFormat)] = 4,
        [nameof(Configuration.JobIconStyle)] = 2,
        [nameof(Configuration.DebuffPosition)] = 4,
        [nameof(Configuration.AllyDebuffPosition)] = 4,
        [nameof(Configuration.OwnDebuffPosition)] = 4,
        [nameof(Configuration.NameFont)] = 4,
    };

    private static readonly PropertyInfo[] Shared = typeof(Configuration)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.CanRead && p.CanWrite && !Personal.Contains(p.Name))
        .Where(p => p.PropertyType == typeof(bool) || p.PropertyType == typeof(int) || p.PropertyType == typeof(float) || p.PropertyType == typeof(Vector4))
        .OrderBy(p => p.Name, StringComparer.Ordinal)
        .ToArray();

    public static string Export(Configuration configuration)
    {
        var values = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var property in Shared)
        {
            var value = property.GetValue(configuration);
            if (value is Vector4 colour)
                value = new[] { colour.X, colour.Y, colour.Z, colour.W };
            if (value != null)
                values[property.Name] = value;
        }

        var json = JsonSerializer.SerializeToUtf8Bytes(values);
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.SmallestSize, true))
            deflate.Write(json, 0, json.Length);
        return Prefix + Convert.ToBase64String(output.ToArray());
    }

    /// <summary>Reads a code (it may sit inside other text, e.g. a pasted chat message) and applies it.</summary>
    /// <returns>The number of settings applied; 0 when the text holds no valid code.</returns>
    public static int Import(string? text, Configuration configuration)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        var start = text.IndexOf(Prefix, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return 0;

        var token = new string(text[(start + Prefix.Length)..]
            .TakeWhile(c => char.IsLetterOrDigit(c) || c is '+' or '/' or '=')
            .ToArray());

        JsonElement root;
        try
        {
            var bytes = Convert.FromBase64String(token);
            using var input = new DeflateStream(new MemoryStream(bytes), CompressionMode.Decompress);
            using var json = new MemoryStream();
            var buffer = new byte[4096];
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                json.Write(buffer, 0, read);
                if (json.Length > MaxJsonBytes)
                    return 0;
            }

            using var document = JsonDocument.Parse(json.ToArray());
            root = document.RootElement.Clone();
        }
        catch (Exception)
        {
            return 0;
        }

        if (root.ValueKind != JsonValueKind.Object)
            return 0;

        // read everything first, so a broken code changes nothing
        var values = new List<(PropertyInfo Property, object Value)>();
        foreach (var property in Shared)
        {
            if (!root.TryGetProperty(property.Name, out var element))
                continue;
            if (TryRead(property, element, out var value))
                values.Add((property, value));
        }

        foreach (var (property, value) in values)
            property.SetValue(configuration, value);
        return values.Count;
    }

    /// <summary>Copies every shared setting (used to undo an import).</summary>
    public static void CopyShared(Configuration from, Configuration to)
    {
        foreach (var property in Shared)
            property.SetValue(to, property.GetValue(from));
    }

    private static bool TryRead(PropertyInfo property, JsonElement element, out object value)
    {
        value = null!;
        var type = property.PropertyType;
        if (type == typeof(bool))
        {
            if (element.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return false;
            value = element.GetBoolean();
            return true;
        }

        if (type == typeof(int))
        {
            if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var number))
                return false;
            var maximum = IntMaximum.TryGetValue(property.Name, out var known) ? known : 1_000;
            value = Math.Clamp(number, 0, maximum);
            return true;
        }

        if (type == typeof(float))
        {
            if (!TryFloat(element, out var number))
                return false;
            value = Math.Clamp(number, -FloatLimit, FloatLimit);
            return true;
        }

        if (type == typeof(Vector4))
        {
            if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() != 4)
                return false;
            var parts = new float[4];
            var i = 0;
            foreach (var item in element.EnumerateArray())
            {
                if (!TryFloat(item, out var part))
                    return false;
                parts[i++] = Math.Clamp(part, 0f, 1f);
            }

            value = new Vector4(parts[0], parts[1], parts[2], parts[3]);
            return true;
        }

        return false;
    }

    private static bool TryFloat(JsonElement element, out float number)
    {
        number = 0f;
        return element.ValueKind == JsonValueKind.Number && element.TryGetSingle(out number) && float.IsFinite(number);
    }
}
