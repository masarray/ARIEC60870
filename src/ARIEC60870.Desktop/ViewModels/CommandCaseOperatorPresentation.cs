// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Windows;
using System.Windows.Data;
using ARIEC60870.Master.Protocol.Iec10x;

namespace ARIEC60870.Desktop.ViewModels;

/// <summary>
/// Human-readable, evidence-limited descriptions for the Master-owned casebook.
/// Never changes command matching, source identity or the safety verdict.
/// </summary>
public static class CommandCaseOperatorPresentation
{
    public static string Status(Iec10xCommandCasebook.CommandCase c) => c.Status switch
    {
        "Ambiguous" => "Multiple commands · review",
        "SELECT sent" => "SELECT sent · waiting",
        "SELECT accepted" => "SELECT accepted by RTU",
        "EXECUTE sent" => "EXECUTE sent · waiting",
        "EXECUTE accepted" => "EXECUTE accepted by RTU",
        "EXECUTE accepted · ACTTERM not observed" => "EXECUTE confirmed · end not seen",
        "Feedback matched · ACTTERM not observed" => "Feedback matched · end not seen",
        "Feedback mismatch · ACTTERM not observed" => "Feedback differs · end not seen",
        "ACTTERM observed" => "Protocol ended · verify CB",
        "Feedback matched" => "Mapped feedback matched",
        "Feedback mismatch" => "Feedback differs · review",
        "RTU rejected" => "RTU rejected command",
        "Observed incomplete" => "Incomplete observation",
        "Unmatched response" => "Unpaired RTU response",
        "Protocol anomaly" => "Unexpected RTU message",
        "Cancelled" => "Cancellation confirmed",
        _ => c.Status
    };

    public static string Guidance(Iec10xCommandCasebook.CommandCase c)
    {
        var last = c.Stages.LastOrDefault()?.Phase ?? string.Empty;
        if (c.Status == "Ambiguous")
        {
            if (last.Contains("overlapping", StringComparison.OrdinalIgnoreCase))
                return "Stop repeating commands. Multiple SELECT/EXECUTE requests overlap on this target; compare TX/RX stages below and check other Masters. No unique operation result is confirmed.";
            if (last.Contains("out of order", StringComparison.OrdinalIgnoreCase))
                return "RTU reply was duplicate or earlier than the matching request. Inspect the original frames and physical source; do not interpret it as command success.";
            return "The RTU reply does not uniquely match the requested SELECT/EXECUTE phase or command value. Inspect TX/RX frames and concurrent Masters; do not retry automatically.";
        }
        return c.Status switch
        {
            "SELECT sent" => "Waiting for RTU SELECT ACTCON; the link-level ACK is not acceptance.",
            "SELECT accepted" => "RTU accepted SELECT. Execute only if authorized and the correct target is still selected.",
            "EXECUTE sent" => "Waiting for RTU EXECUTE ACTCON; transmission does not prove switchgear movement.",
            "EXECUTE accepted" => "RTU accepted EXECUTE (ACTCON). Await ACTTERM and matching CB feedback.",
            "EXECUTE accepted · ACTTERM not observed" => "Next request started after positive EXECUTE ACTCON. Previous ACTTERM and mapped CB feedback were not observed; completion is unverified. Inspect the older case if needed.",
            "Feedback matched · ACTTERM not observed" => "Next request started after EXECUTE ACTCON and mapped CB feedback. Previous ACTTERM was not observed; reported process state is not independent proof of physical CB operation.",
            "Feedback mismatch · ACTTERM not observed" => "RTU feedback differed from the requested value, and ACTTERM was not observed before the next command. Inspect the earlier transaction and mapping; no verified completion.",
            "ACTTERM observed" => "RTU ended the application transaction. Verify the mapped CB status; physical movement is not proven by ACTTERM.",
            "Feedback matched" => "The mapped digital feedback matches the requested state; verify equipment independently where required.",
            "Feedback mismatch" => "CB feedback does not match the requested state. Check mapping, signal quality and RTU sequence; do not assume operation.",
            "RTU rejected" => "Check local/remote, interlocks, command qualifier and CA/IOA mapping. The exact RTU reason is not established.",
            "Unmatched response" => "No matching TX request was observed. Inspect source, link, CA/IOA and capture completeness.",
            "Observed incomplete" => "The observation ended before confirmation or feedback. Inspect the original capture; do not assume RTU failure.",
            "Cancelled" => "RTU confirmed command deactivation; no physical operation is proven.",
            _ => c.Interpretation
        };
    }
}

/// <summary>Converter operates only on immutable snapshots; no grid mutation.</summary>
public sealed class CommandCaseOperatorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Iec10xCommandCasebook.CommandCase c
            ? (string.Equals(parameter as string, "Guidance", StringComparison.Ordinal)
                ? CommandCaseOperatorPresentation.Guidance(c)
                : CommandCaseOperatorPresentation.Status(c))
            : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => DependencyProperty.UnsetValue;
}
