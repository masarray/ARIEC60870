// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Serialization;
using ARIEC60870.Core.Model;

namespace ARIEC60870.Core.Mapping;

/// <summary>
/// User-owned IEC-103 FUN/INF mapping profile.
///
/// ARIEC60870 deliberately ships without vendor/relay-specific signal names.
/// The protocol decoder remains universal; final signal names come from this
/// user/project mapping profile, normally derived from the relay communication
/// database, FAT signal list, or legally available project documentation.
/// </summary>
public sealed class Iec103SignalMappingProfile
{
    public string Schema { get; set; } = "ariec60870-mapping-profile-v1";
    public string ProfileName { get; set; } = "Unloaded mapping profile";
    public string DeviceName { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public byte? LinkAddress { get; set; }
    public byte? CommonAddress { get; set; }
    public List<Iec103SignalMappingEntry> Signals { get; set; } = new();

    [JsonIgnore]
    public static Iec103SignalMappingProfile Empty { get; } = new()
    {
        ProfileName = "No user mapping profile loaded",
        DeviceName = string.Empty,
        ProjectName = string.Empty,
        Signals = new List<Iec103SignalMappingEntry>()
    };

    [JsonIgnore]
    public bool HasSignals => Signals.Count > 0;

    public static Iec103SignalMappingProfile LoadFromFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Mapping profile path is empty.", nameof(path));
        }

        var json = File.ReadAllText(path);
        var profile = JsonSerializer.Deserialize<Iec103SignalMappingProfile>(json, JsonOptions)
            ?? throw new InvalidOperationException("Mapping profile JSON is empty or invalid.");

        profile.Validate();
        return profile;
    }

    public void SaveToFile(string path)
    {
        Validate();
        var json = JsonSerializer.Serialize(this, JsonOptions);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, json);
    }

    public void Validate()
    {
        if (!string.Equals(Schema, "ariec60870-mapping-profile-v1", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Unsupported mapping profile schema. Expected ariec60870-mapping-profile-v1.");
        }

        var duplicateCheck = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var signal in Signals)
        {
            if (signal.Fun < 0 || signal.Fun > 255)
            {
                throw new InvalidOperationException($"Signal '{signal.Name}' has invalid FUN={signal.Fun}. Expected 0..255.");
            }

            if (signal.Inf < 0 || signal.Inf > 255)
            {
                throw new InvalidOperationException($"Signal '{signal.Name}' has invalid INF={signal.Inf}. Expected 0..255.");
            }

            if (string.IsNullOrWhiteSpace(signal.Name))
            {
                throw new InvalidOperationException($"Mapping FUN={signal.Fun}, INF={signal.Inf} has empty signal name.");
            }

            var key = Iec103SignalMappingEntry.BuildKey(signal.Fun, signal.Inf, signal.Type);
            if (!duplicateCheck.Add(key))
            {
                throw new InvalidOperationException($"Duplicate mapping entry for {key}.");
            }
        }
    }

    public Iec103SignalMappingResult Resolve(AsduDecode? asdu)
    {
        if (asdu?.FunctionType is null || asdu.InformationNumber is null)
        {
            return Iec103SignalMappingResult.Unmapped(asdu);
        }

        var type = InferSignalType(asdu);
        var entry = Signals.FirstOrDefault(x =>
            x.Fun == asdu.FunctionType.Value &&
            x.Inf == asdu.InformationNumber.Value &&
            (string.IsNullOrWhiteSpace(x.Type) || string.Equals(x.Type, type, StringComparison.OrdinalIgnoreCase)))
            ?? Signals.FirstOrDefault(x => x.Fun == asdu.FunctionType.Value && x.Inf == asdu.InformationNumber.Value);

        if (entry is null)
        {
            return Iec103SignalMappingResult.Unmapped(asdu);
        }

        var rawValue = ResolveRawValue(asdu);
        var displayValue = entry.ResolveDisplayValue(asdu.Dpi, rawValue);

        return new Iec103SignalMappingResult
        {
            IsMapped = true,
            ProfileName = ProfileName,
            SignalId = string.IsNullOrWhiteSpace(entry.Id) ? entry.Key : entry.Id,
            SignalKey = entry.Key,
            SignalName = entry.Name,
            SignalGroup = entry.Group,
            SignalType = string.IsNullOrWhiteSpace(entry.Type) ? type : entry.Type,
            DisplayValue = displayValue,
            RawValue = rawValue,
            Unit = entry.Unit,
            Description = entry.Description,
            Source = entry.Source,
            Fun = asdu.FunctionType.Value,
            Inf = asdu.InformationNumber.Value
        };
    }

    public static string InferSignalType(AsduDecode asdu) => asdu.TypeId switch
    {
        1 => "DPI",
        2 => "DPI_RT",
        3 => "MEASURAND_I",
        4 => "MEASURAND_RT",
        9 => "MEASURAND_II",
        _ => "ASDU_" + asdu.TypeId.ToString(System.Globalization.CultureInfo.InvariantCulture)
    };

    private static string ResolveRawValue(AsduDecode asdu)
    {
        if (asdu.Dpi.HasValue)
        {
            return (asdu.Dpi.Value & 0x03).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (asdu.NumericValue.HasValue)
        {
            return asdu.NumericValue.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (asdu.DataBytes.Count > 0)
        {
            return string.Join(" ", asdu.DataBytes.Select(x => x.ToString("X2")));
        }

        return string.Empty;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
}

public sealed class Iec103SignalMappingEntry
{
    public string Id { get; set; } = string.Empty;
    public int Fun { get; set; }
    public int Inf { get; set; }

    /// <summary>
    /// Optional signal type qualifier. Leave empty to match any ASDU type for the same FUN/INF.
    /// Common values: DPI, DPI_RT, MEASURAND_I, MEASURAND_II.
    /// </summary>
    public string Type { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Group { get; set; } = "Unassigned";
    public string Description { get; set; } = string.Empty;
    public string Source { get; set; } = "User profile";
    public string Unit { get; set; } = string.Empty;
    public double Scale { get; set; } = 1.0;
    public double Offset { get; set; }
    public Dictionary<string, string> StateMap { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonIgnore]
    public string Key => BuildKey(Fun, Inf, Type);

    public static string BuildKey(int fun, int inf, string? type)
    {
        var typeText = string.IsNullOrWhiteSpace(type) ? "*" : type.Trim().ToUpperInvariant();
        return $"FUN{fun:000}:INF{inf:000}:{typeText}";
    }

    public string ResolveDisplayValue(int? dpi, string rawValue)
    {
        if (dpi.HasValue)
        {
            var key = (dpi.Value & 0x03).ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (StateMap.TryGetValue(key, out var mappedState) && !string.IsNullOrWhiteSpace(mappedState))
            {
                return mappedState;
            }
        }

        if (!string.IsNullOrWhiteSpace(rawValue) && double.TryParse(rawValue, out var numeric))
        {
            var scaled = numeric * Scale + Offset;
            var value = scaled.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            return string.IsNullOrWhiteSpace(Unit) ? value : value + " " + Unit;
        }

        return string.IsNullOrWhiteSpace(rawValue) ? "-" : rawValue;
    }
}

public sealed class Iec103SignalMappingResult
{
    public bool IsMapped { get; init; }
    public string ProfileName { get; init; } = string.Empty;
    public string SignalId { get; init; } = string.Empty;
    public string SignalKey { get; init; } = string.Empty;
    public string SignalName { get; init; } = string.Empty;
    public string SignalGroup { get; init; } = string.Empty;
    public string SignalType { get; init; } = string.Empty;
    public string DisplayValue { get; init; } = string.Empty;
    public string RawValue { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public int? Fun { get; init; }
    public int? Inf { get; init; }

    public static Iec103SignalMappingResult Unmapped(AsduDecode? asdu)
    {
        var type = asdu is null ? string.Empty : Iec103SignalMappingProfile.InferSignalType(asdu);
        var raw = asdu?.Dpi.HasValue == true
            ? (asdu.Dpi.Value & 0x03).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : asdu?.NumericValue.HasValue == true
                ? asdu.NumericValue.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : string.Empty;

        return new Iec103SignalMappingResult
        {
            IsMapped = false,
            ProfileName = string.Empty,
            SignalId = string.Empty,
            SignalKey = asdu?.FunctionType is null || asdu.InformationNumber is null
                ? string.Empty
                : Iec103SignalMappingEntry.BuildKey(asdu.FunctionType.Value, asdu.InformationNumber.Value, type),
            SignalName = string.Empty,
            SignalGroup = "Unmapped",
            SignalType = type,
            DisplayValue = raw,
            RawValue = raw,
            Fun = asdu?.FunctionType,
            Inf = asdu?.InformationNumber
        };
    }
}
