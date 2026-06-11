// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using ARIEC60870.Core.Mapping;

namespace ARIEC60870.Desktop.ViewModels;

public sealed class IoaMappingRow
{
    public IoaMappingRow(Iec10xPointMappingEntry point, IReadOnlyCollection<Iec10xPointMappingEntry> allPoints)
    {
        Ca = point.Ca?.ToString(CultureInfo.InvariantCulture) ?? "*";
        Ioa = point.Ioa.ToString(CultureInfo.InvariantCulture);
        Name = string.IsNullOrWhiteSpace(point.Name) ? $"IOA {point.Ioa}" : point.Name;
        Group = point.Group;
        TypeId = point.TypeId?.ToString(CultureInfo.InvariantCulture) ?? "-";
        Type = string.IsNullOrWhiteSpace(point.SignalType) ? "-" : point.SignalType;
        Class = point.ExpectedClass?.ToString(CultureInfo.InvariantCulture) ?? "-";
        Cot = point.ExpectedCot?.ToString(CultureInfo.InvariantCulture) ?? "-";
        Unit = string.IsNullOrWhiteSpace(point.Unit) ? "-" : point.Unit;
        CommandPolicy = string.IsNullOrWhiteSpace(point.CommandPolicy) ? "MonitorOnly" : point.CommandPolicy;
        FeedbackIoa = point.FeedbackIoa?.ToString(CultureInfo.InvariantCulture) ?? "-";
        Mnemonic = string.IsNullOrWhiteSpace(point.Mnemonic) ? "-" : point.Mnemonic;
        BayType = string.IsNullOrWhiteSpace(point.BayType) ? "-" : point.BayType;
        Binding = BuildBinding(point, allPoints);
    }

    public string Ca { get; }
    public string Ioa { get; }
    public string Name { get; }
    public string Group { get; }
    public string TypeId { get; }
    public string Type { get; }
    public string Class { get; }
    public string Cot { get; }
    public string Unit { get; }
    public string CommandPolicy { get; }
    public string FeedbackIoa { get; }
    public string Binding { get; }
    public string Mnemonic { get; }
    public string BayType { get; }

    private static string BuildBinding(Iec10xPointMappingEntry point, IReadOnlyCollection<Iec10xPointMappingEntry> allPoints)
    {
        if (point.FeedbackIoa.HasValue)
        {
            var feedback = allPoints.FirstOrDefault(x => x.Ioa == point.FeedbackIoa.Value);
            var feedbackName = feedback is null || string.IsNullOrWhiteSpace(feedback.Name) ? $"IOA {point.FeedbackIoa.Value}" : feedback.Name;
            return $"Command → feedback {point.FeedbackIoa.Value} ({feedbackName})";
        }

        var command = allPoints.FirstOrDefault(x => x.FeedbackIoa == point.Ioa);
        if (command is not null)
        {
            var commandName = string.IsNullOrWhiteSpace(command.Name) ? $"IOA {command.Ioa}" : command.Name;
            return $"Feedback for command {command.Ioa} ({commandName})";
        }

        return point.CommandPolicy.Contains("Command", StringComparison.OrdinalIgnoreCase)
            ? "Command point - feedback not mapped"
            : "Monitor point";
    }
}
