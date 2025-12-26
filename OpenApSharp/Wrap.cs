using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using CsvHelper;
using CsvHelper.Configuration;

namespace OpenApSharp;

/// <summary>
/// C# translation of openap.kinematic.WRAP: provides access to the WRAP kinematic model.
/// </summary>
public sealed class Wrap {
    private readonly string _ac;
    private readonly List<WrapRow> _rows;

    public Wrap(string ac, bool useSynonym = true) {
        _ac = ac.ToLowerInvariant();

        var wrapDir = OpenApDataPathResolver.GetPath("wrap");
        var synonymPath = Path.Combine(wrapDir, "_synonym.csv");

        var available = Directory.GetFiles(wrapDir, "*.txt")
            .Select(f => Path.GetFileNameWithoutExtension(f)!.ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var code = _ac;

        if (!available.Contains(code)) {
            if (!useSynonym)
                throw new ArgumentException($"Kinematic model for {ac} not available.");

            if (!File.Exists(synonymPath))
                throw new ArgumentException($"Kinematic model for {ac} not available.");

            using var reader = new StreamReader(synonymPath);
            using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture) {
                PrepareHeaderForMatch = args => args.Header.ToLower()
            });
            var synonyms = csv.GetRecords<AircraftSynonym>().ToList();
            var match = synonyms.FirstOrDefault(s =>
                string.Equals(s.Orig, code, StringComparison.OrdinalIgnoreCase));

            if (match is null)
                throw new ArgumentException($"Kinematic model for {ac} not available.");

            code = match.New.ToLowerInvariant();
        }

        var path = Path.Combine(wrapDir, code + ".txt");
        _rows = ParseWrapFile(path);
    }

    private static List<WrapRow> ParseWrapFile(string path) {
        var lines = File.ReadAllLines(path);
        var result = new List<WrapRow>();

        // First line is header, subsequent lines are fixed-width data.
        // We know the layout from A320.txt: columns are separated by at least two spaces.
        for (var i = 1; i < lines.Length; i++) {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
                continue;

            // Split on 2+ spaces.
            var parts = System.Text.RegularExpressions.Regex
                .Split(line.Trim(), @"\s{2,}")
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToArray();

            if (parts.Length < 8)
                continue;

            var variable = parts[0].Trim();
            var flightPhase = parts[1].Trim();
            var name = parts[2].Trim();
            var opt = double.Parse(parts[3], CultureInfo.InvariantCulture);
            var min = double.Parse(parts[4], CultureInfo.InvariantCulture);
            var max = double.Parse(parts[5], CultureInfo.InvariantCulture);
            var model = parts[6].Trim();
            var paramStr = parts[7].Trim();
            var parameters = paramStr
                .Split('|')
                .Select(p => double.Parse(p, CultureInfo.InvariantCulture))
                .ToArray();

            result.Add(new WrapRow(
                variable,
                flightPhase,
                name,
                opt,
                min,
                max,
                model,
                parameters));
        }

        return result;
    }

    private WrapParameter Get(string variable) {
        var row = _rows.FirstOrDefault(r =>
            string.Equals(r.Variable, variable, StringComparison.OrdinalIgnoreCase));
        if (row.Variable == null)
            throw new ArgumentException($"Variable {variable} not found in WRAP data for {_ac}.");

        return new WrapParameter(
            @default: row.Optimum,
            minimum: row.Minimum,
            maximum: row.Maximum,
            statisticalModel: row.Model,
            statisticalModelParameters: row.Parameters);
    }

    public WrapParameter TakeoffSpeed => Get("to_v_lof");
    public WrapParameter TakeoffDistance => Get("to_d_tof");
    public WrapParameter TakeoffAcceleration => Get("to_acc_tof");

    public WrapParameter InitialClimbCalibratedAirspeed => Get("ic_va_avg");
    public WrapParameter InitialClimbVerticalSpeed => Get("ic_vs_avg");

    public WrapParameter ClimbRange => Get("cl_d_range");
    public WrapParameter ClimbConstantCasSpeed => Get("cl_v_cas_const");
    public WrapParameter ClimbConstantMachSpeed => Get("cl_v_mach_const");
    public WrapParameter ClimbCrossoverAltitudeConstantCas => Get("cl_h_cas_const");
    public WrapParameter ClimbCrossoverAltitudeConstantMach => Get("cl_h_mach_const");
    public WrapParameter ClimbVerticalSpeedPreConstantCas => Get("cl_vs_avg_pre_cas");
    public WrapParameter ClimbVerticalSpeedConstantCas => Get("cl_vs_avg_cas_const");
    public WrapParameter ClimbVerticalSpeedConstantMach => Get("cl_vs_avg_mach_const");

    public WrapParameter CruiseRange => Get("cr_d_range");
    public WrapParameter CruiseCalibratedAirspeedMean => Get("cr_v_cas_mean");
    public WrapParameter CruiseCalibratedAirspeedMax => Get("cr_v_cas_max");
    public WrapParameter CruiseMachMean => Get("cr_v_mach_mean");
    public WrapParameter CruiseMachMax => Get("cr_v_mach_max");
    public WrapParameter CruiseInitialAltitude => Get("cr_h_init");
    public WrapParameter CruiseMeanAltitude => Get("cr_h_mean");
    public WrapParameter CruiseMaxAltitude => Get("cr_h_max");

    public WrapParameter DescentRange => Get("de_d_range");
    public WrapParameter DescentConstantMachSpeed => Get("de_v_mach_const");
    public WrapParameter DescentConstantCasSpeed => Get("de_v_cas_const");
    public WrapParameter DescentCrossoverAltitudeConstantMach => Get("de_h_mach_const");
    public WrapParameter DescentCrossoverAltitudeConstantCas => Get("de_h_cas_const");
    public WrapParameter DescentVerticalSpeedConstantMach => Get("de_vs_avg_mach_const");
    public WrapParameter DescentVerticalSpeedConstantCas => Get("de_vs_avg_cas_const");
    public WrapParameter DescentVerticalSpeedAfterConstantCas => Get("de_vs_avg_after_cas");

    public WrapParameter FinalApproachCalibratedAirspeed => Get("fa_va_avg");
    public WrapParameter FinalApproachVerticalSpeed => Get("fa_vs_avg");
    public WrapParameter FinalApproachAngle => Get("fa_agl");

    public WrapParameter LandingApproachSpeed => Get("ld_v_app");
    public WrapParameter LandingBrakingDistance => Get("ld_d_brk");
    public WrapParameter LandingBrakingAcceleration => Get("ld_acc_brk");

}

internal readonly struct WrapRow {
    public string Variable { get; }
    public string FlightPhase { get; }
    public string Name { get; }
    public double Optimum { get; }
    public double Minimum { get; }
    public double Maximum { get; }
    public string Model { get; }
    public double[] Parameters { get; }

    public WrapRow(
        string variable,
        string flightPhase,
        string name,
        double optimum,
        double minimum,
        double maximum,
        string model,
        double[] parameters) {
        Variable = variable;
        FlightPhase = flightPhase;
        Name = name;
        Optimum = optimum;
        Minimum = minimum;
        Maximum = maximum;
        Model = model;
        Parameters = parameters;
    }
}

/// <summary>
/// Strongly-typed equivalent of the Python dict returned from WRAP._get_var.
/// </summary>
public readonly struct WrapParameter {
    public double Default { get; }
    public double Minimum { get; }
    public double Maximum { get; }
    public string StatisticalModel { get; }
    public IReadOnlyList<double> StatisticalModelParameters { get; }

    public WrapParameter(
        double @default,
        double minimum,
        double maximum,
        string statisticalModel,
        IReadOnlyList<double> statisticalModelParameters) {
        Default = @default;
        Minimum = minimum;
        Maximum = maximum;
        StatisticalModel = statisticalModel;
        StatisticalModelParameters = statisticalModelParameters;
    }

    public override string ToString() {
        return $"Default: {Default}\n" +
               $"  Minimum: {Minimum}\n" +
               $"  Maximum: {Maximum}\n" +
               $"  Model:   {StatisticalModel}\n" +
               $"  Params:  {string.Join("|", StatisticalModelParameters)}";
    }
}

