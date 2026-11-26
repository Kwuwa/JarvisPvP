using System;
using System.Collections.Generic;

namespace PvPTopPlates;

/// <summary>
/// In-memory log shown on the Diagnostics page (also forwarded to the Dalamud log). Only lives while the game
/// runs; holds the last <see cref="Capacity"/> lines.
/// </summary>
internal static class UiLog
{
    public const int Capacity = 500;

    private static readonly object Gate = new();
    private static readonly LinkedList<Entry> Entries = new();
    private static long nextId;

    public enum Level
    {
        Info = 1,
        Warning = 2,
        Error = 3,
    }

    public static long LastId
    {
        get
        {
            lock (Gate)
                return nextId;
        }
    }

    public static void Info(string text) => Add(Level.Info, text);

    public static void Warn(string text, Exception? exception = null)
    {
        Add(Level.Warning, exception is null ? text : $"{text} ({exception.GetType().Name}: {exception.Message})");
    }

    public static void Error(string text, Exception? exception = null)
    {
        Add(Level.Error, exception is null ? text : $"{text} ({exception.GetType().Name}: {exception.Message})");
    }

    /// <summary>Recorded game event (Guard, Elixir, targeting), shown as information.</summary>
    public static void Event(string text) => Add(Level.Info, "[event] " + text);

    public static List<Entry> Snapshot()
    {
        lock (Gate)
            return new List<Entry>(Entries);
    }

    public static void Clear()
    {
        lock (Gate)
            Entries.Clear();
    }

    private static void Add(Level level, string text)
    {
        lock (Gate)
        {
            Entries.AddLast(Entry.Create(++nextId, DateTime.Now, level, text));
            while (Entries.Count > Capacity)
                Entries.RemoveFirst();
        }

        switch (level)
        {
            case Level.Error:
                Plugin.Log.Error(text);
                break;
            case Level.Warning:
                Plugin.Log.Warning(text);
                break;
            default:
                Plugin.Log.Information(text);
                break;
        }
    }

    /// <summary>One log line; <see cref="Line"/> is formatted once when the line is added.</summary>
    public readonly record struct Entry(long Id, DateTime Time, Level Level, string Text, string Line)
    {
        public static Entry Create(long id, DateTime time, Level level, string text)
        {
            var tag = level switch
            {
                Level.Error => "ERR",
                Level.Warning => "WRN",
                _ => "INF",
            };
            return new Entry(id, time, level, text, $"{time:HH:mm:ss.fff} [{tag}] {text}");
        }

        public string Format() => Line;
    }
}
