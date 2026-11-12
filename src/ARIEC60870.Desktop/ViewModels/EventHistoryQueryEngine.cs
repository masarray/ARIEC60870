// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.IO;
using System.Text;
using ARIEC60870.Master.Model;

namespace ARIEC60870.Desktop.ViewModels;

/// <summary>
/// One immutable, source-numbered query horizon. Null ordinals means the
/// unfiltered fast path (zero index scan, constant memory).
/// </summary>
public sealed class EventHistoryQuerySnapshot
{
    private readonly long[]? _ordinals;
    public long Ceiling { get; }
    public long Count => _ordinals?.LongLength ?? Ceiling;
    public bool IsFiltered => _ordinals is not null;

    public EventHistoryQuerySnapshot(long ceiling, long[]? ordinals)
    {
        if (ceiling < 0) throw new ArgumentOutOfRangeException(nameof(ceiling));
        Ceiling = ceiling;
        _ordinals = ordinals;
    }

    public EventHistoryQuerySnapshot Append(long newCeiling, IReadOnlyList<long> newMatches)
    {
        if (newCeiling < Ceiling) throw new ArgumentOutOfRangeException(nameof(newCeiling));
        if (_ordinals is null) return new EventHistoryQuerySnapshot(newCeiling, null);
        var merged = new long[checked(_ordinals.Length + newMatches.Count)];
        Array.Copy(_ordinals, merged, _ordinals.Length);
        for (var i = 0; i < newMatches.Count; i++)
        {
            var number = newMatches[i];
            if (number <= Ceiling || number > newCeiling ||
                i > 0 && number <= newMatches[i - 1])
                throw new InvalidDataException("Event query delta is not source-ordered.");
            merged[_ordinals.Length + i] = number;
        }
        return new EventHistoryQuerySnapshot(newCeiling, merged);
    }

    public long OriginalNumberAt(long position)
    {
        if (position < 0 || position >= Count) throw new ArgumentOutOfRangeException(nameof(position));
        return _ordinals is null ? position + 1 : _ordinals[checked((int)position)];
    }

    public long FindPosition(long eventNumber)
    {
        if (eventNumber < 1) return -1;
        if (_ordinals is null) return eventNumber <= Ceiling ? eventNumber - 1 : -1;
        return Array.BinarySearch(_ordinals, eventNumber) is var index && index >= 0 ? index : -1;
    }

    public IReadOnlyList<EventHistoryJournal.Entry> ReadPage(
        EventHistoryJournal journal, long start, int limit = EventHistoryPagination.PageSize)
    {
        ArgumentNullException.ThrowIfNull(journal);
        if (start < 0 || limit is < 1 or > 200)
            throw new ArgumentOutOfRangeException(nameof(start));
        if (start >= Count) return Array.Empty<EventHistoryJournal.Entry>();
        var take = (int)Math.Min(limit, Count - start);
        if (_ordinals is null) return journal.ReadAfter(start, take);
        var selected = new long[take];
        Array.Copy(_ordinals, checked((int)start), selected, 0, take);
        return journal.ReadSelected(selected);
    }

    /// <summary>
    /// Page projection for the operator grid only. The displayed No. follows
    /// the current query order (including page offset), while EventNumber and
    /// its typed original-frame evidence remain unchanged for engineering audit.
    /// </summary>
    public IReadOnlyList<RelayEventRow> ReadDisplayPage(
        EventHistoryJournal journal, long start, int limit = EventHistoryPagination.PageSize)
    {
        var entries = ReadPage(journal, start, limit);
        var rows = new RelayEventRow[entries.Count];
        for (var i = 0; i < entries.Count; i++)
        {
            var row = entries[i].ToRow();
            row.DisplayNumber = checked(start + i + 1);
            rows[i] = row;
        }
        return rows;
    }
}

/// <summary>
/// Historical searching never touches WPF collections, serial RX or the
/// canonical event ledger. Reuses the exact same predicate for paging and
/// CSV export. Cancellation is checked between bounded disk reads.
/// </summary>
public static class EventHistoryQueryEngine
{
    public static EventHistoryQuerySnapshot Build(
        EventHistoryJournal journal, long ceiling, string? query,
        IReadOnlyCollection<string> include, IReadOnlyCollection<string> exclude,
        Func<RelayEventRow, string> classify, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(include);
        ArgumentNullException.ThrowIfNull(exclude);
        ArgumentNullException.ThrowIfNull(classify);
        if (ceiling < 0 || ceiling > journal.Count)
            throw new ArgumentOutOfRangeException(nameof(ceiling));
        var allowed = new HashSet<string>(include, StringComparer.Ordinal);
        var blocked = new HashSet<string>(exclude, StringComparer.Ordinal);
        var search = query?.Trim() ?? string.Empty;
        var all = search.Length == 0 && blocked.Count == 0 &&
            new[] { "Digital", "Measurement", "Commands", "GI", "Other" }
                .All(allowed.Contains);
        if (all) return new EventHistoryQuerySnapshot(ceiling, null);
        var matches = Scan(journal, 0, ceiling, search, allowed, blocked,
            classify, cancellationToken);
        return new EventHistoryQuerySnapshot(ceiling, matches.ToArray());
    }

    public static EventHistoryQuerySnapshot Extend(
        EventHistoryJournal journal, EventHistoryQuerySnapshot previous, long newCeiling,
        string? query, IReadOnlyCollection<string> include, IReadOnlyCollection<string> exclude,
        Func<RelayEventRow, string> classify, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(previous);
        if (newCeiling < previous.Ceiling || newCeiling > journal.Count)
            throw new ArgumentOutOfRangeException(nameof(newCeiling));
        if (newCeiling == previous.Ceiling) return previous;
        if (!previous.IsFiltered) return previous.Append(newCeiling, Array.Empty<long>());
        var added = Scan(journal, previous.Ceiling, newCeiling, query?.Trim() ?? "",
            new HashSet<string>(include, StringComparer.Ordinal),
            new HashSet<string>(exclude, StringComparer.Ordinal), classify, cancellationToken);
        return previous.Append(newCeiling, added);
    }

    private static List<long> Scan(EventHistoryJournal journal, long start, long ceiling,
        string search, HashSet<string> allowed, HashSet<string> blocked,
        Func<RelayEventRow, string> classify, CancellationToken cancellationToken)
    {
        var matches = new List<long>();
        for (var cursor = start; cursor < ceiling; cursor += 200)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = journal.ReadAfter(cursor, (int)Math.Min(200, ceiling - cursor));
            if (batch.Count == 0)
                throw new InvalidDataException("Event History scan returned an incomplete page.");
            foreach (var entry in batch)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = entry.ToQueryRow();
                var category = classify(row);
                if (!allowed.Contains(category) || blocked.Contains(category)) continue;
                if (search.Length > 0 &&
                    !QuickFindMatcher.MatchesEvent(row, search) &&
                    !MatchesEventNumber(entry.EventNumber, search)) continue;
                matches.Add(entry.EventNumber);
            }
        }
        return matches;
    }

    public static bool MatchesEventNumber(long number, string query)
    {
        var text = query.Trim().TrimStart('#');
        return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture,
            out var target) && number == target;
    }
}

/// <summary>
/// CSV export is a bounded streaming read of the exact same source-numbered
/// query snapshot. Writes a temporary file and only publishes on success.
/// </summary>
public static class EventHistoryCsvExporter
{
    public static async Task<long> ExportAsync(
        EventHistoryJournal journal, EventHistoryQuerySnapshot snapshot,
        string destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (string.IsNullOrWhiteSpace(destination)) throw new ArgumentException("Missing CSV path.", nameof(destination));
        var directory = Path.GetDirectoryName(Path.GetFullPath(destination))
            ?? throw new IOException("Missing CSV destination directory.");
        Directory.CreateDirectory(directory);
        var partial = Path.Combine(directory, "." + Path.GetFileName(destination) +
            "." + Guid.NewGuid().ToString("N") + ".partial");
        long written = 0;
        try
        {
            await using (var file = new FileStream(partial, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var csv = new StreamWriter(file, new UTF8Encoding(true), 65536))
            {
                await csv.WriteLineAsync(
                    "EventNumber,HostArrivalUtc,RelayTimestamp,Protocol,Source,Signal,PreviousValue,Value,CA,IOA,TI,COT,Quality,QualityInvalid,FrameReference,RawHex");
                for (long start = 0; start < snapshot.Count; start += 200)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var batch = await Task.Run(() =>
                        snapshot.ReadPage(journal, start, (int)Math.Min(200, snapshot.Count - start)),
                        cancellationToken).ConfigureAwait(false);
                    foreach (var entry in batch)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var row = entry.ToRow();
                        var original = row.OriginalFrameEvidence;
                        var data = new[]
                        {
                            entry.EventNumber.ToString(CultureInfo.InvariantCulture),
                            entry.Source.ArrivalTimeUtc.ToString("O", CultureInfo.InvariantCulture),
                            entry.Source.RelayTimeText,
                            row.Protocol, row.PhysicalSource, row.Signal, row.Previous,
                            row.NewValue, row.CommonAddress, row.IoAddress,
                            row.TypeId, row.Cot, row.Quality,
                            row.IsQualityInvalid ? "true" : "false",
                            original?.EvidenceReference ?? row.EvidenceReference,
                            original?.RawHex ?? row.RawHex
                        };
                        await csv.WriteLineAsync(string.Join(",", data.Select(Escape))).ConfigureAwait(false);
                        written++;
                    }
                }
                await csv.FlushAsync(cancellationToken).ConfigureAwait(false);
                file.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(partial, destination, overwrite: true);
            return written;
        }
        finally
        {
            if (File.Exists(partial))
            {
                try { File.Delete(partial); }
                catch (IOException) { /* temporary recovery file retained by OS */ }
            }
        }
    }

    // RFC-style quoting + spreadsheet formula-injection protection, including
    // leading whitespace before a formula. Always quote every text field.
    public static string Escape(string? cell)
    {
        var value = cell ?? string.Empty;
        var trimmed = value.AsSpan().TrimStart();
        if (!trimmed.IsEmpty && (trimmed[0] is '=' or '+' or '-' or '@' or '\t' or '\r' or '\n'))
            value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
