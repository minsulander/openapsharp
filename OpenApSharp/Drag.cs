using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace OpenApSharp;

/// <summary>
/// Drag model for an aircraft, ported from openap.drag.Drag.
/// Inputs: mass (kg), TAS (kt), altitude (ft), vertical speed (ft/min), flap angle (deg).
/// Output: total drag (N).
/// </summary>
public sealed class Drag : DragBase {
    private static readonly IDeserializer YamlDeserializer =
        new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

    private readonly Aircraft _aircraft;
    private readonly DragPolar _polar;
    private readonly bool _waveDrag;

    public Drag(string ac, bool waveDrag = false, bool useSynonym = false)
        : base(ac) {
        _aircraft = Prop.Aircraft(ac, useSynonym);
        _polar = LoadDragModel(ac, useSynonym);
        _waveDrag = waveDrag;
    }

    private static DragPolar LoadDragModel(string ac, bool useSynonym) {
        ac = ac.ToLowerInvariant();

        var dragDir = OpenApDataPathResolver.GetPath("dragpolar");
        var synonymFile = OpenApDataPathResolver.GetPath("dragpolar", "_synonym.csv");

        string? selectedCode = null;

        // Check direct availability
        if (File.Exists(Path.Combine(dragDir, $"{ac}.yml"))) {
            selectedCode = ac;
        } else {
            if (!useSynonym || !File.Exists(synonymFile))
                throw new InvalidOperationException(
                    $"Drag polar for {ac} not available. " +
                    "Enable 'useSynonym' to search synonyms.");

            using var reader = new StreamReader(synonymFile);
            using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture) {
                PrepareHeaderForMatch = args => args.Header.ToLower()
            });
            var synonyms = csv.GetRecords<AircraftSynonym>().ToList();
            var match = synonyms.FirstOrDefault(s =>
                string.Equals(s.Orig, ac, StringComparison.OrdinalIgnoreCase));

            if (match is null)
                throw new InvalidOperationException(
                    $"Drag polar for {ac} not available, and no synonym found.");

            selectedCode = match.New.ToLowerInvariant();
        }

        var yamlFile = Path.Combine(dragDir, $"{selectedCode}.yml");
        var yaml = File.ReadAllText(yamlFile);
        return YamlDeserializer.Deserialize<DragPolar>(yaml);
    }

    public override double Clean(double massKg, double tasKnots, double altitudeFeet, double verticalSpeedFpm = 0) {
        var cd0 = _polar.Clean.Cd0;
        var k = _polar.Clean.K;

        double dCdw = 0.0;

        if (_waveDrag) {
            var mach = Aero.TasToMachFromKnots(tasKnots, altitudeFeet);
            var (cl, qS) = ComputeLiftCoefficient(massKg, tasKnots, altitudeFeet, verticalSpeedFpm);

            var sweepRad = (_aircraft.Wing?.Sweep ?? 0.0) * Math.PI / 180.0;
            var tc = _aircraft.Wing?.T_C ?? 0.12; // default t/c

            var cosSweep = Math.Cos(sweepRad);
            const double kappa = 0.95; // assume supercritical airfoils

            var machCrit =
                kappa / cosSweep
                - tc / (cosSweep * cosSweep)
                - 0.1 * cl / Math.Pow(cosSweep, 3)
                - 0.108;

            var dMach = Math.Max(mach - machCrit, 0.0);
            dCdw = 20.0 * Math.Pow(dMach, 4); // Eq. 15 Gur et al.
        }

        cd0 += dCdw;
        return CalcDrag(massKg, tasKnots, altitudeFeet, cd0, k, verticalSpeedFpm);
    }

    public override double NonClean(
        double massKg,
        double tasKnots,
        double altitudeFeet,
        double flapAngleDeg,
        double verticalSpeedFpm = 0,
        bool landingGear = false) {
        var cd0 = _polar.Clean.Cd0;
        var k = _polar.Clean.K;

        // New CD0
        var flaps = _polar.Flaps;
        var lambdaF = flaps.LambdaF;
        var cfc = flaps.Cf_C;
        var sfS = flaps.Sf_S;

        var flapRad = flapAngleDeg * Math.PI / 180.0;
        var deltaCdFlap = lambdaF * Math.Pow(cfc, 1.38) * sfS * Math.Pow(Math.Sin(flapRad), 2.0);

        double deltaCdGear;
        if (landingGear) {
            var mtow = _aircraft.Limits?.MTOW ?? 0.0;
            var wingArea = _aircraft.Wing?.Area ?? 1.0;
            deltaCdGear = mtow * Aero.G0 / wingArea * 3.16e-5 * Math.Pow(mtow, -0.215);
        } else {
            deltaCdGear = 0.0;
        }

        var cd0Total = cd0 + deltaCdFlap + deltaCdGear;

        // New k
        var engineInstall = _aircraft.Engine
                             ?? throw new InvalidOperationException(
                                 $"Aircraft {AircraftCode} has no engine installation data.");

        double deltaEFlap;
        if (string.Equals(engineInstall.Mount, "rear", StringComparison.OrdinalIgnoreCase)) {
            deltaEFlap = 0.0046 * flapAngleDeg;
        } else {
            deltaEFlap = 0.0026 * flapAngleDeg;
        }

        var span = _aircraft.Wing?.Span ?? 0.0;
        var area = _aircraft.Wing?.Area ?? 1.0;
        var ar = span * span / area;
        var kTotal = 1.0 / (1.0 / k + Math.PI * ar * deltaEFlap);

        return CalcDrag(massKg, tasKnots, altitudeFeet, cd0Total, kTotal, verticalSpeedFpm);
    }

    private (double Cl, double QS) ComputeLiftCoefficient(
        double massKg,
        double tasKnots,
        double altitudeFeet,
        double verticalSpeedFpm = 0) {
        var v = tasKnots * Aero.Kts;
        var h = altitudeFeet * Aero.Ft;
        var vs = verticalSpeedFpm * Aero.Fpm;
        var gamma = Math.Atan2(vs, v);
        var S = _aircraft.Wing?.Area ?? 0.0;
        var rho = Aero.Density(h);
        var qS = 0.5 * rho * v * v * S;
        var L = massKg * Aero.G0 * Math.Cos(gamma);
        qS = Math.Max(qS, 1e-3); // avoid zero division
        var cl = L / qS;
        return (cl, qS);
    }

    private double CalcDrag(
        double massKg,
        double tasKnots,
        double altitudeFeet,
        double cd0,
        double k,
        double verticalSpeedFpm) {
        var (cl, qS) = ComputeLiftCoefficient(massKg, tasKnots, altitudeFeet, verticalSpeedFpm);
        var cd = cd0 + k * cl * cl;
        var D = cd * qS;
        return D;
    }
}

public sealed class DragPolar {
    public DragPolarClean Clean { get; set; } = new();
    public DragPolarFlaps Flaps { get; set; } = new();
}

public sealed class DragPolarClean {
    public double Cd0 { get; set; }
    public double K { get; set; }
    public double E { get; set; }
}

public sealed class DragPolarFlaps {
    public double LambdaF { get; set; }

    // Mapped from "cf/c"
    public double Cf_C { get; set; }

    // Mapped from "Sf/S"
    public double Sf_S { get; set; }
}


