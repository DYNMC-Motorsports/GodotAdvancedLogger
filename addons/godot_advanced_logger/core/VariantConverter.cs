using System;
using System.Collections.Generic;
using Godot;

namespace GodotAdvancedLogger.addons.godot_advanced_logger.core;

/// <summary>
/// Converts Godot <see cref="Variant"/> values into plain CLR objects.
/// </summary>
/// <remarks>
/// Context dictionaries coming from GDScript hold <see cref="Variant"/>s, but the writers
/// serialize context with <c>System.Text.Json</c>, which cannot handle a raw Variant. It
/// would either emit nonsense or throw and take the whole batch down with it. Everything
/// crossing the language boundary is reduced here to a primitive, a string, a list or a
/// nested dictionary first.
/// </remarks>
public static class VariantConverter
{
    /// <summary>
    /// Nesting limit for arrays and dictionaries. Godot collections may contain themselves,
    /// so an unbounded walk could recurse until the stack gives out.
    /// </summary>
    private const int MaxDepth = 4;

    /// <summary>
    /// Converts a GDScript context dictionary into the shape the writers expect.
    /// </summary>
    /// <returns><c>null</c> for a null or empty input, so no empty object is serialized.</returns>
    public static Dictionary<string, object> ToContext(Godot.Collections.Dictionary source)
    {
        if (source == null || source.Count == 0) return null;

        var result = new Dictionary<string, object>(source.Count);

        foreach (var kvp in source)
        {
            // Keys are stringified: JSON object keys must be strings, and GDScript happily allows an int or a Vector2 as a dictionary key.
            string key = kvp.Key.ToString();
            if (string.IsNullOrEmpty(key)) continue;

            result[key] = ToObject(kvp.Value, 0);
        }

        return result;
    }

    /// <summary>Reduces a single <see cref="Variant"/> to a serializable CLR value.</summary>
    public static object ToObject(Variant value, int depth = 0)
    {
        switch (value.VariantType)
        {
            case Variant.Type.Nil:
                return null;

            case Variant.Type.Bool:
                return value.AsBool();

            case Variant.Type.Int:
                return value.AsInt64();

            case Variant.Type.Float:
                return value.AsDouble();

            case Variant.Type.String:
            case Variant.Type.StringName:
            case Variant.Type.NodePath:
                return value.AsString();

            case Variant.Type.Array:
                return depth >= MaxDepth ? Describe(value) : ConvertArray(value, depth);

            case Variant.Type.Dictionary:
                return depth >= MaxDepth ? Describe(value) : ConvertDictionary(value, depth);

            default:
                // Vectors, Colors, Rects, Objects, packed arrays: no meaningful JSON shape.
                return Describe(value);
        }
    }

    private static object ConvertArray(Variant value, int depth)
    {
        var array = value.AsGodotArray();
        var list = new List<object>(array.Count);

        foreach (var item in array)
        {
            list.Add(ToObject(item, depth + 1));
        }

        return list;
    }

    private static object ConvertDictionary(Variant value, int depth)
    {
        var dictionary = value.AsGodotDictionary();
        var map = new Dictionary<string, object>(dictionary.Count);

        foreach (var kvp in dictionary)
        {
            string key = kvp.Key.ToString();
            if (string.IsNullOrEmpty(key)) continue;

            map[key] = ToObject(kvp.Value, depth + 1);
        }

        return map;
    }

    /// <summary>
    /// String form of a value with no JSON representation.
    /// </summary>
    /// <remarks>
    /// Guarded: <c>ToString()</c> on a Variant wrapping a freed Object can throw, and a
    /// logging call must never be the thing that brings the game down.
    /// </remarks>
    private static string Describe(Variant value)
    {
        try
        {
            return value.ToString();
        }
        catch (Exception e)
        {
            return $"<unreadable {value.VariantType}: {e.GetType().Name}>";
        }
    }
}
