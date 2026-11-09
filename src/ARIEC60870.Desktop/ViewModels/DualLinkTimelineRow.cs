// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using ARIEC60870.Master.Model;

namespace ARIEC60870.Desktop.ViewModels;

public sealed class DualLinkTimelineRow
{
    public DualLinkTimelineRow(Iec103MasterEvidenceEvent item)
    {
        Sequence = item.SequenceNumber;
        Time = item.TimestampUtc.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
        Link = ResolveLink(item);
        Event = string.IsNullOrWhiteSpace(item.Category) ? item.State.ToString() : item.Category;
        FullReason = string.IsNullOrWhiteSpace(item.Summary) ? item.OperatorMessage : item.Summary;
        FullResult = string.IsNullOrWhiteSpace(item.Detail) ? item.ProtocolMeaning : item.Detail;
        if (item.PollingReason == "IEC101-RX-LINK-ADDRESS-MISMATCH" &&
            item.ExpectedLinkAddress is int expected && item.Frame?.LinkAddress is int actual)
        {
            Reason = $"Expected {expected} · Received {actual}";
            Result = "Rejected · Verify RTU address and converter routing";
        }
        else
        {
            Reason = Compact(FullReason, 90);
            Result = Compact(string.IsNullOrWhiteSpace(item.ProtocolMeaning) ? FullResult : item.ProtocolMeaning, 95);
        }
    }

    public long Sequence { get; }
    public string Time { get; }
    public string Link { get; }
    public string Event { get; }
    public string Reason { get; }
    public string Result { get; }
    public string FullReason { get; }
    public string FullResult { get; }

    private static string Compact(string text, int limit) =>
        text.Length <= limit ? text : text[..(limit - 1)].TrimEnd() + "…";

    private static string ResolveLink(Iec103MasterEvidenceEvent item)
    {
        var text = string.Join(' ', item.DataClass, item.SignalGroup, item.Summary, item.Detail, item.OperatorMessage);
        if (text.Contains("Link A", StringComparison.OrdinalIgnoreCase)) return "Link A";
        if (text.Contains("Link B", StringComparison.OrdinalIgnoreCase)) return "Link B";
        if (text.Contains("active", StringComparison.OrdinalIgnoreCase)) return "Active";
        if (text.Contains("standby", StringComparison.OrdinalIgnoreCase)) return "Standby";
        return "Controller";
    }
}
