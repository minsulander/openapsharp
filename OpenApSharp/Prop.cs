using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.Configuration.Attributes;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace OpenApSharp;

/// <summary>
/// C# equivalent of openap.prop: provides access to aircraft and engine data.
/// </summary>
public static class Prop {
    private static readonly IDeserializer YamlDeserializer =
        new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

    private static readonly Lazy<IReadOnlyList<EngineRecord>> Engines =
        new(LoadEngines);

    public static Aircraft Aircraft(string ac, bool useSynonym = false) {
        if (string.IsNullOrWhiteSpace(ac))
            throw new ArgumentException("Aircraft ICAO code must be provided.", nameof(ac));

        ac = ac.ToLowerInvariant();

        var aircraftDir = Path.Combine(OpenApDataPathResolver.GetPath("aircraft"));
        var synonymFile = Path.Combine(OpenApDataPathResolver.GetPath("aircraft", "_synonym.csv"));

        var yamlFile = Directory.GetFiles(aircraftDir, $"{ac}.yml").FirstOrDefault();

        if (yamlFile is null) {
            if (!useSynonym || !File.Exists(synonymFile)) {
                throw new InvalidOperationException(
                    $"Aircraft {ac} not available. Enable 'useSynonym' to search synonyms.");
            }

            using var reader = new StreamReader(synonymFile);
            using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture) {
                PrepareHeaderForMatch = args => args.Header.ToLower()
            });
            var synonyms = csv.GetRecords<AircraftSynonym>().ToList();
            var match = synonyms.FirstOrDefault(s =>
                string.Equals(s.Orig, ac, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException(
                    $"Aircraft {ac} not available, and no synonym found.");

            yamlFile = Directory.GetFiles(aircraftDir, $"{match.New}.yml").FirstOrDefault()
                       ?? throw new InvalidOperationException(
                           $"Aircraft synonym {match.New} for {ac} not available.");
        }

        var yaml = File.ReadAllText(yamlFile);
        var aircraft = YamlDeserializer.Deserialize<Aircraft>(yaml);

        // Build limits section for compatibility with Python
        aircraft.Limits ??= new AircraftLimits {
            MTOW = aircraft.Mtow,
            MLW = aircraft.Mlw,
            OEW = aircraft.Oew,
            MFC = aircraft.Mfc,
            VMO = aircraft.Vmo,
            MMO = aircraft.Mmo,
            Ceiling = aircraft.Ceiling
        };

        return aircraft;
    }

    public static Engine Engine(string eng) {
        if (string.IsNullOrWhiteSpace(eng))
            throw new ArgumentException("Engine name must be provided.", nameof(eng));

        var upper = eng.Trim().ToUpperInvariant();
        var engines = Engines.Value;

        var candidates = engines
            .Where(e => e.Name != null &&
                        e.Name.ToUpperInvariant().StartsWith(upper, StringComparison.Ordinal))
            .ToList();

        if (candidates.Count == 0)
            throw new InvalidOperationException($"Data for engine {eng} not found.");

        var record = candidates[0];

        // Compute fuel_ch as in Python for completeness (not yet used by thrust).
        double fuelCh;
        if (record.CruiseSfc.HasValue && !double.IsNaN(record.CruiseSfc.Value)) {
            var sfcCr = record.CruiseSfc.Value;
            var sfcTo = record.FfTo / (record.MaxThrust / 1000.0);
            fuelCh = Math.Round(
                (sfcCr - sfcTo) / (record.CruiseAlt.GetValueOrDefault() * Aero.Ft), 8);
        } else {
            fuelCh = 6.7e-7;
        }

        return new Engine(record, eng, fuelCh);
    }

    private static IReadOnlyList<EngineRecord> LoadEngines() {
        var engineFile = OpenApDataPathResolver.GetPath("engine", "engines.csv");
        using var reader = new StreamReader(engineFile);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture) {
            HasHeaderRecord = true,
            MissingFieldFound = null,
            BadDataFound = null
        };
        using var csv = new CsvReader(reader, config);
        return csv.GetRecords<EngineRecord>().ToList();
    }
}

internal sealed class AircraftSynonym {
    public string Orig { get; set; } = string.Empty;
    public string New { get; set; } = string.Empty;
}

public sealed class Aircraft {
    // Top-level scalar fields from YAML
    public string? AircraftName { get; set; }  // mapped from "aircraft"
    public double? Mtow { get; set; }
    public double? Mlw { get; set; }
    public double? Oew { get; set; }
    public double? Mfc { get; set; }
    public double? Vmo { get; set; }
    public double? Mmo { get; set; }
    public double? Ceiling { get; set; }

    public Wing? Wing { get; set; }
    public Cruise? Cruise { get; set; }
    public EngineInstallation? Engine { get; set; }
    public AircraftLimits? Limits { get; set; }
}

public sealed class Wing {
    public double Area { get; set; }
    public double Span { get; set; }
    public double Mac { get; set; }
    public double Sweep { get; set; }
    public double? T_C { get; set; } // maps from "t/c"
}

public sealed class Cruise {
    public double Height { get; set; }
    public double Mach { get; set; }
    public double Range { get; set; }
}

public sealed class EngineInstallation {
    public string? Type { get; set; }
    public string? Mount { get; set; }
    public int Number { get; set; }
    public string? Default { get; set; }
    // YAML provides either a mapping (variant -> engine) or a simple list; accept both.
    public FlexibleMapOrSequence? Options { get; set; }

    public string[] GetOptionValues() {
        var raw = Options?.Value;
        if (raw is null)
            return Array.Empty<string>();

        switch (raw) {
            case IDictionary dict:
                return dict.Values
                    .Cast<object>()
                    .Select(v => v?.ToString() ?? string.Empty)
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .ToArray();
            case IEnumerable<object> seq:
                return seq
                    .Select(v => v?.ToString() ?? string.Empty)
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .ToArray();
            default:
                var single = raw.ToString() ?? string.Empty;
                return string.IsNullOrWhiteSpace(single) ? Array.Empty<string>() : new[] { single };
        }
    }
}

/// <summary>
/// Helper that lets YamlDotNet accept either a mapping or a sequence for a node.
/// </summary>
public sealed class FlexibleMapOrSequence : IYamlConvertible {
    public object? Value { get; private set; }

    public void Read(YamlDotNet.Core.IParser parser, Type expectedType, ObjectDeserializer nestedObjectDeserializer) {
        Value = nestedObjectDeserializer(typeof(object));
    }

    public void Write(IEmitter emitter, ObjectSerializer nestedObjectSerializer) {
        nestedObjectSerializer(Value);
    }
}

public sealed class AircraftLimits {
    public double? MTOW { get; set; }
    public double? MLW { get; set; }
    public double? OEW { get; set; }
    public double? MFC { get; set; }
    public double? VMO { get; set; }
    public double? MMO { get; set; }
    public double? Ceiling { get; set; }
}

internal sealed class EngineRecord {
    // Core performance fields
    [Name("name")]
    public string? Name { get; set; }

    [Name("bpr")]
    public double Bpr { get; set; }

    [Name("max_thrust")]
    public double MaxThrust { get; set; }

    [Name("cruise_thrust")]
    public double? CruiseThrust { get; set; }

    [Name("cruise_sfc")]
    public double? CruiseSfc { get; set; }

    [Name("cruise_mach")]
    public double? CruiseMach { get; set; }

    [Name("cruise_alt")]
    public double? CruiseAlt { get; set; }

    // Fuel flow at LTO points (kg/s) per engine
    [Name("ff_to")]
    public double FfTo { get; set; }

    [Name("ff_co")]
    public double FfCo { get; set; }

    [Name("ff_app")]
    public double FfApp { get; set; }

    [Name("ff_idl")]
    public double FfIdl { get; set; }

    // Emission indices g/kg fuel at LTO points
    [Name("ei_nox_to")]
    public double EiNoxTo { get; set; }

    [Name("ei_nox_co")]
    public double EiNoxCo { get; set; }

    [Name("ei_nox_app")]
    public double EiNoxApp { get; set; }

    [Name("ei_nox_idl")]
    public double EiNoxIdl { get; set; }

    [Name("ei_co_to")]
    public double EiCoTo { get; set; }

    [Name("ei_co_co")]
    public double EiCoCo { get; set; }

    [Name("ei_co_app")]
    public double EiCoApp { get; set; }

    [Name("ei_co_idl")]
    public double EiCoIdl { get; set; }

    [Name("ei_hc_to")]
    public double EiHcTo { get; set; }

    [Name("ei_hc_co")]
    public double EiHcCo { get; set; }

    [Name("ei_hc_app")]
    public double EiHcApp { get; set; }

    [Name("ei_hc_idl")]
    public double EiHcIdl { get; set; }
}

public sealed class Engine {
    public string Name { get; set; } = string.Empty;
    public double Bpr { get; set; }
    public double MaxThrust { get; set; }
    public double CruiseMach { get; set; }
    public double CruiseThrust { get; set; }
    public double CruiseAlt { get; set; }
    public double FuelCh { get; set; }

    // Fuel flow and emission indices from the engine database (per engine).
    public double FfTo => _record.FfTo;
    public double FfCo => _record.FfCo;
    public double FfApp => _record.FfApp;
    public double FfIdl => _record.FfIdl;

    public double EiNoxTo => _record.EiNoxTo;
    public double EiNoxCo => _record.EiNoxCo;
    public double EiNoxApp => _record.EiNoxApp;
    public double EiNoxIdl => _record.EiNoxIdl;

    public double EiCoTo => _record.EiCoTo;
    public double EiCoCo => _record.EiCoCo;
    public double EiCoApp => _record.EiCoApp;
    public double EiCoIdl => _record.EiCoIdl;

    public double EiHcTo => _record.EiHcTo;
    public double EiHcCo => _record.EiHcCo;
    public double EiHcApp => _record.EiHcApp;
    public double EiHcIdl => _record.EiHcIdl;

    private readonly EngineRecord _record;

    internal Engine(EngineRecord record, string name, double fuelCh) {
        _record = record;
        Name = name;
        Bpr = record.Bpr;
        MaxThrust = record.MaxThrust;
        CruiseMach = record.CruiseMach ?? 0.0;
        CruiseThrust = record.CruiseThrust ?? 0.0;
        CruiseAlt = record.CruiseAlt ?? 0.0;
        FuelCh = fuelCh;
    }
}


