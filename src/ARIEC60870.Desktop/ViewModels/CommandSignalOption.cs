// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

namespace ARIEC60870.Desktop.ViewModels;

public sealed class CommandSignalOption
{
    public string Name { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public string SearchText { get; init; } = string.Empty;
    public string CommandPolicy { get; init; } = string.Empty;
    public int? CommonAddress { get; init; }
    public int InformationObjectAddress { get; init; }
    public int? TypeId { get; init; }
    public int? FeedbackIoa { get; init; }
    public string FeedbackName { get; init; } = string.Empty;
    public double? EngineeringMin { get; init; }
    public double? EngineeringMax { get; init; }
    public string Unit { get; init; } = string.Empty;
}
