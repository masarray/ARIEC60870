// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Serialization;

namespace ARIEC60870.Core.Mapping;

/// <summary>
/// User-editable IEC-101/104 IOA point profile. The bundled PLN/Pusertif seed is only
/// a starting profile; project teams can copy/edit it for any utility, RTU or gateway.
/// </summary>
public sealed class Iec10xPointMappingProfile
{
    public string Schema { get; set; } = "ariec10x-ioa-profile-v1";
    public string ProfileName { get; set; } = "No IOA profile loaded";
    public string Region { get; set; } = "Global";
    public string ProjectName { get; set; } = string.Empty;
    public string Source { get; set; } = "User profile";
    public int? CommonAddress { get; set; }
    public Iec10xInteroperabilityDefaults? DefaultSettings { get; set; }
    public List<Iec10xTestScenario> TestScenarios { get; set; } = new();
    public List<Iec10xPointMappingEntry> Points { get; set; } = new();

    [JsonIgnore]
    public static Iec10xPointMappingProfile Empty { get; } = new()
    {
        ProfileName = "No IOA profile loaded",
        Source = "No profile",
        Points = new List<Iec10xPointMappingEntry>()
    };

    [JsonIgnore]
    public bool HasPoints => Points.Count > 0;

    public static Iec10xPointMappingProfile LoadFromFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("IOA profile path is empty.", nameof(path));
        }

        var json = File.ReadAllText(path);
        var profile = JsonSerializer.Deserialize<Iec10xPointMappingProfile>(json, JsonOptions)
            ?? throw new InvalidOperationException("IOA profile JSON is empty or invalid.");
        profile.Validate();
        return profile;
    }

    public void SaveToFile(string path)
    {
        Validate();
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }

    public void Validate()
    {
        if (!string.Equals(Schema, "ariec10x-ioa-profile-v1", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Unsupported IOA profile schema. Expected ariec10x-ioa-profile-v1.");
        }

        var duplicate = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var point in Points)
        {
            if (point.Ioa < 0 || point.Ioa > 0xFFFFFF)
            {
                throw new InvalidOperationException($"Point '{point.Name}' has invalid IOA={point.Ioa}. Expected 0..16777215.");
            }
            if (point.Ca.HasValue && (point.Ca.Value < 0 || point.Ca.Value > 0xFFFF))
            {
                throw new InvalidOperationException($"Point '{point.Name}' has invalid CA={point.Ca}. Expected 0..65535.");
            }
            if (point.TypeId.HasValue && (point.TypeId.Value < 0 || point.TypeId.Value > 255))
            {
                throw new InvalidOperationException($"Point '{point.Name}' has invalid TypeId={point.TypeId}. Expected 0..255.");
            }
            if (string.IsNullOrWhiteSpace(point.Name))
            {
                throw new InvalidOperationException($"Point CA={point.Ca}, IOA={point.Ioa} has empty signal name.");
            }

            var key = point.BuildKey();
            if (!duplicate.Add(key))
            {
                throw new InvalidOperationException($"Duplicate IOA profile entry for {key}.");
            }
        }
    }

    public Iec10xPointMappingEntry? Resolve(int? commonAddress, int? ioa, int? typeId)
    {
        if (!ioa.HasValue)
        {
            return null;
        }

        // Strict match first: project databases should normally match CA + IOA + Type ID.
        var strict = Points.FirstOrDefault(x =>
                x.Ioa == ioa.Value &&
                (!x.Ca.HasValue || !commonAddress.HasValue || x.Ca.Value == commonAddress.Value) &&
                (!x.TypeId.HasValue || !typeId.HasValue || x.TypeId.Value == typeId.Value))
            ?? Points.FirstOrDefault(x =>
                x.Ioa == ioa.Value &&
                (!x.Ca.HasValue || !commonAddress.HasValue || x.Ca.Value == commonAddress.Value));
        if (strict is not null)
        {
            return strict;
        }

        // Field-test fallback: some lab simulators/devices return CA different from the
        // approved sheet while the IOA list itself is still the intended database.
        // Keep the signal readable, then let the forensic layer/report highlight CA mismatch.
        var sameIoa = Points.Where(x => x.Ioa == ioa.Value).ToArray();
        if (sameIoa.Length == 0)
        {
            return null;
        }

        var typeMatch = sameIoa.Where(x => !x.TypeId.HasValue || !typeId.HasValue || x.TypeId.Value == typeId.Value).ToArray();
        if (typeMatch.Length == 1)
        {
            return typeMatch[0];
        }

        return sameIoa.Length == 1 ? sameIoa[0] : null;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
}


public sealed class Iec10xInteroperabilityDefaults
{
    public string ProfileIntent { get; set; } = string.Empty;
    public int? CommonAddress { get; set; }
    public int? LinkAddress { get; set; }
    public int? LinkAddressSize { get; set; }
    public int? CauseOfTransmissionSize { get; set; }
    public int? CommonAddressSize { get; set; }
    public int? InformationObjectAddressSize { get; set; }
    public int? BaudRate { get; set; }
    public string SerialMode { get; set; } = string.Empty;
    public string TransmissionMode { get; set; } = string.Empty;
    public string SerialAddressHint { get; set; } = string.Empty;
    public string TcpHost { get; set; } = string.Empty;
    public int? TcpPort { get; set; }
}

public sealed class Iec10xTestScenario
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;
    public int RepeatCount { get; set; }
    public string Criteria { get; set; } = string.Empty;
    public List<int> InformationObjectAddresses { get; set; } = new();
    public List<int> CommandObjectAddresses { get; set; } = new();
    public Dictionary<string, string> Acceptance { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class Iec10xPointMappingEntry
{
    public int? Ca { get; set; }
    public int Ioa { get; set; }
    public int? TypeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Group { get; set; } = "Unassigned";
    public string SignalType { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public double Scale { get; set; } = 1.0;
    public double Offset { get; set; }
    public string CommandPolicy { get; set; } = "MonitorOnly";
    public string Description { get; set; } = string.Empty;
    public string Mnemonic { get; set; } = string.Empty;
    public string BayType { get; set; } = string.Empty;
    public string TestCategory { get; set; } = string.Empty;
    public int? ExpectedClass { get; set; }
    public int? ExpectedCot { get; set; }
    public int? FeedbackIoa { get; set; }
    public double? EngineeringMin { get; set; }
    public double? EngineeringMax { get; set; }
    public Dictionary<string, string> StateMap { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string BuildKey() => $"CA{(Ca?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "*")}:IOA{Ioa}:T{(TypeId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "*")}";

    public string ResolveDisplayValue(string rawValue)
    {
        var stateKey = rawValue?.Trim() ?? string.Empty;
        if (StateMap.TryGetValue(stateKey, out var mappedState) && !string.IsNullOrWhiteSpace(mappedState))
        {
            return mappedState;
        }

        if (!string.IsNullOrWhiteSpace(rawValue) && double.TryParse(rawValue, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var numeric))
        {
            var scaled = numeric * Scale + Offset;
            var value = scaled.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            return string.IsNullOrWhiteSpace(Unit) ? value : value + " " + Unit;
        }

        return string.IsNullOrWhiteSpace(rawValue) ? "-" : rawValue;
    }
}
