// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Serialization;

namespace ARIEC60870.Master.Model;

/// <summary>
/// An explicit, versioned interpretation policy. This does not modify raw evidence,
/// silently adopt historical national conventions, or enable an unimplemented link mode.
/// </summary>
public sealed record Iec10xInteroperabilityProfile
{
    public const int CurrentSchemaVersion = 1;
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string Id { get; init; } = "generic-iec10x";
    public string Edition { get; init; } = "generic-v1";
    public string Convention { get; init; } = "Generic IEC";
    public Iec60870ProtocolMode Protocol { get; init; } = Iec60870ProtocolMode.Iec101;
    public int LinkWidth { get; init; } = 1;
    public int CotWidth { get; init; } = 2;
    public int CaWidth { get; init; } = 2;
    public int IoaWidth { get; init; } = 3;
    public string LinkMode { get; init; } = "Unbalanced";
    public string GiAfterSwitch { get; init; } = "Disabled";
    public bool IsHistoricalNationalConvention { get; init; }

    // This property is a project capability declaration, NOT an implementation switch.
    // A profile may describe balanced IEC-101; active balanced TX remains disabled.
    public static Iec10xInteroperabilityProfile Generic(Iec60870ProtocolMode mode) => mode switch
    {
        Iec60870ProtocolMode.Iec101 => new() { Protocol = mode, Id = "generic-iec101" },
        Iec60870ProtocolMode.Iec104 => new()
        {
            Protocol = mode, Id = "generic-iec104", LinkWidth = 0, LinkMode = "Tcp"
        },
        _ => new()
        {
            Protocol = mode, Id = "generic-iec103", CotWidth = 1, CaWidth = 1,
            IoaWidth = 1
        }
    };

    public static Iec10xInteroperabilityProfile HistoricalNuc101() => Generic(Iec60870ProtocolMode.Iec101) with
    {
        Id = "historical-nuc101", Edition = "NUC101 rev2.0", Convention = "Historical NUC101",
        IsHistoricalNationalConvention = true,
        GiAfterSwitch = "Disabled"
    };

    public static Iec10xInteroperabilityProfile HistoricalNuc104() => Generic(Iec60870ProtocolMode.Iec104) with
    {
        Id = "historical-nuc104", Edition = "NUC104 rev1.2", Convention = "Historical NUC104",
        IsHistoricalNationalConvention = true
    };

    public string Fingerprint => $"{Id}@{Edition};protocol={Protocol};link={LinkWidth};cot={CotWidth};ca={CaWidth};ioa={IoaWidth};mode={LinkMode};gi={GiAfterSwitch}";

    public static Iec10xInteroperabilityProfile Import(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 65536)
            throw new InvalidDataException("Interoperability profile JSON is empty or exceeds 64 KiB.");
        Iec10xInteroperabilityProfile? profile;
        try
        {
            profile = JsonSerializer.Deserialize<Iec10xInteroperabilityProfile>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true,
                    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                    NumberHandling = JsonNumberHandling.Strict });
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Malformed or unsupported interoperability profile JSON.", ex);
        }
        if (profile is null) throw new InvalidDataException("Missing interoperability profile.");
        profile.ValidateDefinition();
        return profile;
    }

    public string Export() => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });

    /// <summary>Custom profiles must have explicit project ownership; never impersonate a convention.</summary>
    public static Iec10xInteroperabilityProfile ImportProject(string json)
    {
        var profile = Import(json);
        if (!profile.Id.StartsWith("project-", StringComparison.Ordinal) ||
            profile.IsHistoricalNationalConvention)
            throw new InvalidDataException("Project profile Id must begin 'project-' without a historical convention claim.");
        return profile;
    }

    /// <summary>Pure before/after preview: never changes active settings or raw interpretation.</summary>
    public IReadOnlyList<string> DifferencesFrom(Iec10xInteroperabilityProfile current)
    {
        ArgumentNullException.ThrowIfNull(current);
        var changes = new List<string>();
        void Compare<T>(string field, T before, T after) where T : notnull
        {
            if (!EqualityComparer<T>.Default.Equals(before, after))
                changes.Add($"{field}: {before} → {after}");
        }
        Compare("Protocol", current.Protocol, Protocol);
        Compare("Link width", current.LinkWidth, LinkWidth);
        Compare("COT width", current.CotWidth, CotWidth);
        Compare("CA width", current.CaWidth, CaWidth);
        Compare("IOA width", current.IoaWidth, IoaWidth);
        Compare("Link mode", current.LinkMode, LinkMode);
        Compare("GI policy", current.GiAfterSwitch, GiAfterSwitch);
        return changes;
    }

    public void ValidateDefinition()
    {
        if (SchemaVersion != CurrentSchemaVersion)
            throw new InvalidDataException($"Unsupported interoperability profile schema version {SchemaVersion}.");
        if (string.IsNullOrWhiteSpace(Id) || Id.Length > 100 ||
            string.IsNullOrWhiteSpace(Edition) || Edition.Length > 100 ||
            string.IsNullOrWhiteSpace(Convention) || Convention.Length > 100)
            throw new InvalidDataException("Interoperability profile identity or edition is missing/oversized.");
        if (LinkWidth is < 0 or > 2 || CotWidth is < 1 or > 2 ||
            CaWidth is < 1 or > 2 || IoaWidth is < 1 or > 3)
            throw new InvalidDataException("Unsupported IEC-10x field width in interoperability profile.");
        if (GiAfterSwitch is not ("Disabled" or "Manual" or "OnFailover" or "ProjectRequired"))
            throw new InvalidDataException("Invalid GI-after-switch policy.");
        if (Protocol == Iec60870ProtocolMode.Iec104)
        {
            if (LinkWidth != 0 || LinkMode != "Tcp" || CotWidth != 2 || CaWidth != 2 || IoaWidth != 3)
                throw new InvalidDataException("IEC-104 requires fixed 2/2/3 ASDU widths and TCP framing without IEC-101 link address.");
            if (GiAfterSwitch != "Disabled")
                throw new InvalidDataException("Serial GI-after-switch policy is not applicable to IEC-104.");
        }
        else if (Protocol == Iec60870ProtocolMode.Iec101)
        {
            if (LinkMode is not ("Balanced" or "Unbalanced"))
                throw new InvalidDataException("IEC-101 mode must be explicitly Balanced or Unbalanced.");
            if (LinkMode == "Unbalanced" && LinkWidth == 0)
                throw new InvalidDataException("This product's IEC-101 unbalanced master requires 1 or 2 link-address octets.");
        }
        else if (Protocol == Iec60870ProtocolMode.Iec103)
        {
            if (LinkWidth != 1 || CotWidth != 1 || CaWidth != 1 || IoaWidth != 1 ||
                LinkMode != "Unbalanced" || GiAfterSwitch != "Disabled")
                throw new InvalidDataException("IEC-103 relay profile must use supported FUN/INF and fixed serial field widths.");
        }
        else throw new InvalidDataException("Unsupported IEC protocol mode.");
        if (IsHistoricalNationalConvention &&
            !((Id == "historical-nuc101" && Protocol == Iec60870ProtocolMode.Iec101) ||
              (Id == "historical-nuc104" && Protocol == Iec60870ProtocolMode.Iec104)))
            throw new InvalidDataException("Historical national-convention claim does not match a known profile.");
    }

    /// <summary>
    /// Fail-closed on active transmission. Passive replay is deliberately separate:
    /// unsupported active balanced mode must never block inspection of raw captured bytes.
    /// </summary>
    public void ValidateActive(Iec103MasterSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ValidateDefinition();
        if (settings.ProtocolMode != Protocol)
            throw new InvalidOperationException("Profile protocol does not match the selected session protocol.");
        if (settings.CauseOfTransmissionSize != CotWidth ||
            settings.CommonAddressSize != CaWidth ||
            settings.InformationObjectAddressSize != IoaWidth ||
            (Protocol != Iec60870ProtocolMode.Iec104 && settings.LinkAddressSize != LinkWidth))
            throw new InvalidOperationException($"Interoperability field widths differ from the selected profile. Expected Link/COT/CA/IOA={LinkWidth}/{CotWidth}/{CaWidth}/{IoaWidth}; configured={settings.LinkAddressSize}/{settings.CauseOfTransmissionSize}/{settings.CommonAddressSize}/{settings.InformationObjectAddressSize}. Review the profile before TX.");
        if (Protocol == Iec60870ProtocolMode.Iec101 &&
            (LinkMode != "Unbalanced" || !settings.TransmissionMode.Equals("Unbalanced", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Active IEC-101 balanced transmission is not implemented. Passive RX-only analysis is allowed.");
        if (Protocol == Iec60870ProtocolMode.Iec101 &&
            (settings.LinkAddress < 0 || settings.LinkAddress > (LinkWidth == 1 ? 255 : 65535)))
            throw new InvalidOperationException("Configured link address exceeds the selected width.");
        if (settings.CommonAddress < 0 || settings.CommonAddress > (CaWidth == 1 ? 255 : 65535))
            throw new InvalidOperationException("Configured common address exceeds the selected width.");
    }
}
