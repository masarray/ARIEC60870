// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.IO.Ports;

namespace ARIEC60870.Monitor;

/// <summary>
/// Field CLI input contract. Pure preflight: no COM opened, no disk written,
/// no protocol command issued. Shares PassiveCaptureOptions validation with GUI.
/// </summary>
public sealed record PassiveFieldCapturePlan(PassiveCaptureOptions Options, int DurationSeconds)
{
    private static readonly HashSet<string> AllowedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "scada-com", "rtu-com", "link-b-scada-com", "link-b-rtu-com",
        "output", "baud", "parity", "data-bits", "stop-bits", "seconds",
        "link-width", "cot-width", "ca-width", "ioa-width", "link-mode", "frame-gap-ms"
    };

    public static PassiveFieldCapturePlan Parse(IReadOnlyDictionary<string, string> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        foreach (var name in parameters.Keys)
        {
            if (!AllowedKeys.Contains(name))
                throw new ArgumentException("Unknown field option --" + name + ". Run --help for supported options.");
        }

        string Read(string key, string fallback = "") =>
            parameters.TryGetValue(key, out var value) ? value.Trim() : fallback;

        int Number(string key, string fallback) =>
            int.Parse(Read(key, fallback), NumberStyles.None, CultureInfo.InvariantCulture);

        var seconds = Number("seconds", "180");
        if (seconds is < 1 or > 86400)
            throw new ArgumentOutOfRangeException(nameof(parameters), "Duration must be 1–86400 seconds.");

        var baud = Number("baud", "9600");
        var parity = Enum.Parse<Parity>(Read("parity", "Even"), ignoreCase: true);
        var stopBits = Enum.Parse<StopBits>(Read("stop-bits", "One"), ignoreCase: true);
        var dataBits = Number("data-bits", "8");

        PassiveSerialSource Source(string key, string label) =>
            new(Read(key), label, baud, parity, dataBits, stopBits);

        var linkBScada = Read("link-b-scada-com");
        var linkBRtu = Read("link-b-rtu-com");
        if (string.IsNullOrWhiteSpace(linkBScada) != string.IsNullOrWhiteSpace(linkBRtu))
            throw new ArgumentException("Link B requires both --link-b-scada-com and --link-b-rtu-com.");

        var options = new PassiveCaptureOptions(
            Read("output"),
            Source("scada-com", "Link A SCADA TX -> RTU"),
            Source("rtu-com", "Link A RTU TX -> SCADA"),
            LinkAddressWidth: Number("link-width", "1"),
            CotWidth: Number("cot-width", "2"),
            CommonAddressWidth: Number("ca-width", "2"),
            InformationAddressWidth: Number("ioa-width", "3"),
            FrameGapMilliseconds: Number("frame-gap-ms", "900"),
            LinkMode: Read("link-mode", "Auto"),
            LinkBScadaTx: linkBScada.Length > 0 ? Source("link-b-scada-com", "Link B SCADA TX -> RTU") : null,
            LinkBRtuTx: linkBRtu.Length > 0 ? Source("link-b-rtu-com", "Link B RTU TX -> SCADA") : null,
            RecordFullSession: true);
        options.Validate();
        return new PassiveFieldCapturePlan(options, seconds);
    }

    /// <summary>
    /// Advisory inventory only; an installed COM port is not proof of UART,
    /// physical isolation, correct wiring, source direction or remote traffic.
    /// </summary>
    public string[] MissingPorts(IEnumerable<string> availablePorts)
    {
        ArgumentNullException.ThrowIfNull(availablePorts);
        var available = new HashSet<string>(availablePorts.Select(x => x.Trim()),
            StringComparer.OrdinalIgnoreCase);
        return Options.Sources.Select(x => x.PortName).Where(x => !available.Contains(x)).ToArray();
    }
}
