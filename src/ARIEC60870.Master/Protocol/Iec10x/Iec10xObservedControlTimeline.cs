// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ARIEC60870.Core.Model;

namespace ARIEC60870.Master.Protocol.Iec10x;

/// <summary>
/// Bounded, source-scoped interpretation of already-decoded IEC-101/104 control ASDUs.
/// Observes traffic only: never sends commands, re-decodes raw bytes, or claims
/// physical operation. A missing stage in a bounded preview is not an RTU timeout.
/// </summary>
public sealed class Iec10xObservedControlTimeline
{
    private readonly Dictionary<ControlKey, Pending> _pending = new();
    private const int MaxPending = 128;

    private readonly record struct ControlKey(string Source, int CommonAddress, int Ioa,
        byte TypeId, int? Originator);

    private sealed class Pending
    {
        public required bool Select { get; init; }
        public required string RequestedValue { get; init; }
        public long? RequestOffset { get; init; }
        public bool ActConSeen { get; set; }
        public bool Ambiguous { get; set; }
    }

    public int PendingCount => _pending.Count;
    public void Reset() => _pending.Clear();

    /// <returns>A short, evidence-qualified explanation for the physical frame,
    /// or empty when this ASDU is unrelated to an IEC-10x control.</returns>
    public string Observe(FrameDirection direction, Iec10xAsduDecode? asdu,
        string physicalSource, long? rawStreamOffset, bool trusted)
    {
        if (asdu is null || !IsControl(asdu.TypeId)) return string.Empty;

        var cot = asdu.CauseDetails;
        if (!trusted || cot is null || asdu.Issues.Count != 0 ||
            asdu.ObjectCount != 1 || asdu.Objects.Count != 1)
            return "CONTROL UNVERIFIED: malformed, incomplete, multi-object, or untrusted ASDU; " +
                   "inspect original HEX and field-width settings. No response pairing made.";

        var obj = asdu.Objects[0];
        if (obj.Issues.Count != 0)
            return "CONTROL UNVERIFIED: information object has decoder issues; inspect original HEX.";

        var command = Iec10xCommandDetails.Decode(asdu.TypeId, obj, cot);
        if (command is null || command.Select is null)
            return "CONTROL UNVERIFIED: command qualifier/S-E unavailable; no response pairing made.";

        var source = string.IsNullOrWhiteSpace(physicalSource)
            ? "SINGLE" : physicalSource.Trim().ToUpperInvariant();
        var key = new ControlKey(source, asdu.CommonAddress, obj.InformationObjectAddress,
            asdu.TypeId, cot.OriginatorAddress);
        var location = $"Type={asdu.TypeId}, CA={key.CommonAddress}, IOA={key.Ioa}";
        var step = command.Select.Value ? "SELECT" : "EXECUTE";
        var expected = command.ValueMeaning;

        if (cot.Test)
            return $"CONTROL TEST frame COT={cot.Code}, {location}, {step}: " +
                   "TEST bit set; not valid operational acceptance evidence.";

        if (direction == FrameDirection.MasterToSlave)
        {
            if (cot.Code is not (6 or 8) || cot.Negative)
                return $"MASTER control {step} COT={cot.Code} ({cot.Phase}), {location}; " +
                       "not a standard positive activation; check raw qualifier and COT.";

            if (cot.Code == 8)
            {
                _pending.Remove(key);
                return $"MASTER DEACTIVATION (COT=8), {location}; awaiting RTU response. " +
                       "Cancellation is not evidence of execution.";
            }

            if (_pending.TryGetValue(key, out var previous))
            {
                // A confirmed SELECT can legitimately be followed by an EXECUTE.
                if (!(previous.Select && previous.ActConSeen && !command.Select.Value))
                {
                    previous.Ambiguous = true;
                    return $"MASTER {step} repeated/overlapping, {location}: request pairing ambiguous. " +
                           "This could be a retry or a separate command; inspect the full sequence.";
                }
            }

            if (_pending.Count >= MaxPending)
                _pending.Remove(_pending.Keys.First()); // bounded forensic state, not a wire claim
            _pending[key] = new Pending
            {
                Select = command.Select.Value,
                RequestedValue = expected,
                RequestOffset = rawStreamOffset
            };
            return $"MASTER {step} COT=6, {location}, {expected}; awaiting RTU ACTCON (COT=7). " +
                   "Link ACK alone does not confirm the application command.";
        }

        if (direction != FrameDirection.SlaveToMaster) return string.Empty;

        var hasRequest = _pending.TryGetValue(key, out var pending);
        var reference = hasRequest
            ? $" · preceding request RX offset={pending!.RequestOffset?.ToString() ?? "unknown"}"
            : " · matching Master request not observed in this preview";
        var phaseMismatch = hasRequest && pending!.Select != command.Select.Value;
        if (phaseMismatch || (hasRequest && pending!.Ambiguous))
            return $"RTU command response COT={cot.Code}, {location}: " +
                   (phaseMismatch ? "SELECT/EXECUTE echo differs from observed request" :
                       "overlapping/retried commands make matching ambiguous") +
                   "; no acceptance verdict." + reference;

        if (cot.Code is >= 44 and <= 47)
        {
            _pending.Remove(key);
            return $"RTU APPLICATION ERROR COT={cot.Code} ({cot.Phase}), {location}. " +
                   "Check the indicated Type/COT/CA/IOA and selected interoperability profile." + reference;
        }

        if (cot.Negative)
        {
            _pending.Remove(key);
            return $"RTU NEGATIVE CONFIRMATION (P/N=1, COT={cot.Code}, {cot.Phase}), " +
                   $"{location}, {step}. Control was not positively accepted. " +
                   "COT alone does not identify a device interlock or the root cause: " +
                   "check permission, local/remote mode, interlocks, qualifier and mapping." + reference;
        }

        if (cot.Code == 7)
        {
            if (!hasRequest)
                return $"RTU ACTCON COT=7, {location}, {step}; matching Master request " +
                       "not observed. No complete transaction verdict.";
            pending!.ActConSeen = true;
            return $"RTU POSITIVE ACTCON COT=7 for {step}, {location}; application accepted. " +
                   "Execution/physical position still unverified; inspect ACTTERM and process feedback." +
                   reference;
        }

        if (cot.Code == 10)
        {
            if (!hasRequest || !pending!.ActConSeen)
                return $"RTU ACTTERM COT=10, {location}, {step}; " +
                       "preceding source-matched ACTCON not observed. " +
                       "Incomplete evidence; not proof that operation succeeded." + reference;
            _pending.Remove(key);
            return $"RTU ACTTERM COT=10 for {step}, {location}; application phase ended. " +
                   "Physical position/target feedback remains unverified." + reference;
        }

        if (cot.Code == 9)
        {
            _pending.Remove(key);
            return $"RTU DEACTCON COT=9, {location}; deactivation acknowledged, " +
                   "not evidence of an executed command." + reference;
        }

        return $"RTU control-related COT={cot.Code} ({cot.Phase}), {location}; " +
               "not positive ACTCON or physical execution proof." + reference;
    }

    private static bool IsControl(byte typeId)
        => typeId is >= 45 and <= 51 or >= 58 and <= 64;
}
