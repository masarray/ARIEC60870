// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Desktop.ViewModels;

/// <summary>
/// Source-backed, selected-frame property. Children are decoded details, not
/// tooltip paragraphs. Never infer quality flags from a missing descriptor.
/// </summary>
public sealed record FrameInspectorProperty(
    string Field, string Value, string Detail,
    IReadOnlyList<FrameInspectorFieldPresentation.DetailRow>? Children = null)
{
    public bool HasChildren => Children is { Count: > 0 };
}

/// <summary>
/// Light, flat projection of a two/three-depth property tree. A DataGrid keeps
/// the Name/Value columns aligned at every depth; only explicitly opened
/// children are projected, and original protocol evidence is unchanged.
/// </summary>
public sealed record FrameInspectorVisibleRow(
    string Key, string Field, string Value, string Detail, int Depth,
    bool HasChildren, bool IsExpanded, string Tone)
{
    public string ExpansionMark => HasChildren ? IsExpanded ? "▾" : "▸" : "";
}

public static class FrameInspectorTreePresentation
{
    public static string RootKey(int index) => $"property/{index}";
    public static string QualityDetailsKey(int index) => $"property/{index}/quality-details";

    public static IReadOnlyList<FrameInspectorVisibleRow> Build(
        IReadOnlyList<FrameInspectorProperty> properties, ISet<string> expanded)
    {
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(expanded);
        var result = new List<FrameInspectorVisibleRow>(properties.Count + 12);

        for (var i = 0; i < properties.Count; i++)
        {
            var node = properties[i];
            var key = RootKey(i);
            var isOpen = node.HasChildren && expanded.Contains(key);
            result.Add(new(key, node.Field, node.Value, node.Detail, 0,
                node.HasChildren, isOpen, Tone(node.Field, node.Value)));

            if (!isOpen || node.Children is null) continue;
            var children = node.Children;
            // The quality bytes have one explicit intermediate group, like
            // engineering property browsers: Quality → Quality details → flags.
            // This visual grouping does not change the canonical decoded facts.
            var depth = 1;
            if (node.Field == "Quality")
            {
                var groupKey = QualityDetailsKey(i);
                var groupOpen = expanded.Contains(groupKey);
                result.Add(new(groupKey, "Quality details", "Decoded bit fields",
                    "Flags are shown only when present in the original quality descriptor. Hover a field for the standard interpretation.",
                    depth, true, groupOpen, "Group"));
                if (!groupOpen) continue;
                depth = 2;
            }

            for (var j = 0; j < children.Count; j++)
            {
                var child = children[j];
                result.Add(new($"{key}/child/{j}", child.Field, child.Value,
                    child.Explanation, depth, false, false, Tone(child.Field, child.Value)));
            }
        }
        return result;
    }

    public static IReadOnlySet<string> InitiallyExpanded(
        IReadOnlyList<FrameInspectorProperty> properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        var expanded = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < properties.Count; i++)
        {
            if (properties[i].Field != "Quality" || !properties[i].HasChildren) continue;
            expanded.Add(RootKey(i));
            expanded.Add(QualityDetailsKey(i));
        }
        return expanded;
    }

    private static string Tone(string field, string value)
    {
        if ((field == "Invalid (IV)" || field == "Frame checksum" ||
             field == "Frame length") &&
            (value == "True" || value == "Invalid"))
            return "Critical";
        if ((field is "Overflow (OV)" or "Not topical (NT)" or "Blocked (BL)" or
              "Substituted (SB)" or "Carry (CY)" or "Adjusted (CA)" or
              "Access demand (ACD)") &&
            (value == "True" || value.StartsWith("1 ·", StringComparison.Ordinal)))
            return "Warning";
        if ((field == "Quality" || field == "Frame checksum" || field == "Frame length") &&
            (value == "Good" || value == "Valid"))
            return "Good";
        return "Normal";
    }
}
