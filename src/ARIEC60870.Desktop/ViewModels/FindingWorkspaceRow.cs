// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;

namespace ARIEC60870.Desktop.ViewModels;

public sealed class FindingWorkspaceRow
{
    public FindingWorkspaceRow(
        string severity,
        string code,
        string problem,
        string why,
        string proof,
        string fix,
        string confidence,
        long sequence,
        string interpretation,
        IReadOnlyList<FindingWorkspaceFrameRow> frames)
    {
        Severity = severity;
        Code = code;
        Problem = problem;
        Why = why;
        Proof = proof;
        Fix = fix;
        Confidence = confidence;
        Sequence = sequence <= 0 ? "-" : "#" + sequence;
        Interpretation = interpretation;
        Frames = frames;
    }

    public string Severity { get; }
    public string Code { get; }
    public string Problem { get; }
    public string Why { get; }
    public string Proof { get; }
    public string Fix { get; }
    public string Confidence { get; }
    public string Sequence { get; }
    public string Interpretation { get; }
    public IReadOnlyList<FindingWorkspaceFrameRow> Frames { get; }
}

public sealed class FindingWorkspaceFrameRow
{
    public FindingWorkspaceFrameRow(
        string direction,
        string sequence,
        string time,
        string service,
        string address,
        string title,
        string meaning,
        string raw,
        string tone)
    {
        Direction = direction;
        Sequence = sequence;
        Time = time;
        Service = service;
        Address = address;
        Title = title;
        Meaning = meaning;
        Raw = raw;
        Tone = tone;
    }

    public string Direction { get; }
    public string Sequence { get; }
    public string Time { get; }
    public string Service { get; }
    public string Address { get; }
    public string Title { get; }
    public string Meaning { get; }
    public string Raw { get; }
    public string Tone { get; }
}
