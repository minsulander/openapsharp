using System;
using System.Globalization;
using System.IO;
using System.Linq;
using CsvHelper;
using CsvHelper.Configuration.Attributes;

namespace OpenApSharp;

/// <summary>
/// C# translation of openap.fuel.FuelFlow, scalar-only (no ndarrayconvert).
/// </summary>
public sealed class FuelFlow {
    private readonly Aircraft _aircraft;
    private readonly Engine _engine;
    private readonly string _engineType;
    private readonly Thrust _thrust;
    private readonly Drag _drag;
    private readonly Func<double, double> _fuelModel; // maps thrust ratio -> kg/s

    public FuelFlow(string ac, string? eng = null, bool useSynonym = false) {
        _aircraft = Prop.Aircraft(ac, useSynonym);

        if (string.IsNullOrWhiteSpace(eng)) {
            eng = _aircraft.Engine?.Default
                  ?? throw new InvalidOperationException(
                      $"Default engine not specified for aircraft {ac}.");
        }

        _engineType = eng.ToUpperInvariant();
        _engine = Prop.Engine(eng);

        _thrust = new Thrust(ac, eng, forceEngine: false);
        _drag = new Drag(ac, waveDrag: false, useSynonym: useSynonym);

        _fuelModel = LoadFuelModel(ac, useSynonym);
    }

    private Func<double, double> LoadFuelModel(string ac, bool useSynonym) {
        var fuelFile = OpenApDataPathResolver.GetPath("fuel", "fuel_models.csv");
        using var reader = new StreamReader(fuelFile);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
        var records = csv.GetRecords<FuelModelRecord>().ToList();

        var lower = ac.ToLowerInvariant();
        var model = records.FirstOrDefault(r =>
                          string.Equals(r.TypeCode, lower, StringComparison.OrdinalIgnoreCase))
                    ?? records.First(r => string.Equals(r.TypeCode, "default",
                        StringComparison.OrdinalIgnoreCase));

        var c1 = model.C1;
        var c2 = model.C2;
        var c3 = model.C3;

        var scale = 1.0;

        if (string.Equals(model.TypeCode, "default", StringComparison.OrdinalIgnoreCase)) {
            scale = _engine.FfTo;
        } else if (!string.Equals(_engineType, model.EngineType, StringComparison.OrdinalIgnoreCase)) {
            var refEng = Prop.Engine(model.EngineType);
            scale = _engine.FfTo / refEng.FfTo;
        }

        // Python: lambda x: c1 - exp(-c2 * (x * exp(c3 * x) - log(c1) / c2)) * scale
        return x => {
            var inner = x * Math.Exp(c3 * x) - Math.Log(c1) / c2;
            var val = c1 - Math.Exp(-c2 * inner) * scale;
            return val;
        };
    }

    /// <summary>
    /// Fuel flow at a given total thrust (all engines), kg/s.
    /// </summary>
    public double AtThrust(double totalThrustNewton, bool limit = true) {
        var maxEngThrust = _engine.MaxThrust;
        var nEng = _aircraft.Engine?.Number ?? 1;

        var ratio = totalThrustNewton / (maxEngThrust * nEng);

        // Smooth lower limit to ~0.03 as in Python implementation.
        ratio = Math.Log(1 + Math.Exp(50 * (ratio - 0.03))) / 50.0 + 0.03;

        if (limit && ratio > 1.0)
            ratio = 1.0;

        return _fuelModel(ratio);
    }

    /// <summary>
    /// Fuel flow at takeoff, given TAS (kt), altitude (ft), and throttle (0-1), kg/s.
    /// </summary>
    public double Takeoff(double tasKnots, double altitudeFeet = 0, double throttle = 1.0) {
        var Tmax = _thrust.Takeoff(tasKnots, altitudeFeet);
        var fuelflow = throttle * AtThrust(Tmax);
        return fuelflow;
    }

    /// <summary>
    /// Fuel flow during climb/cruise/descent (scalar version of enroute).
    /// mass kg, TAS kt, altitude ft, VS ft/min, acc m/s^2.
    /// </summary>
    public double Enroute(
        double massKg,
        double tasKnots,
        double altitudeFeet,
        double verticalSpeedFpm = 0,
        double accelerationMs2 = 0,
        bool limit = true) {
        var D = _drag.Clean(massKg, tasKnots, altitudeFeet, verticalSpeedFpm);

        var gamma = Math.Atan2(verticalSpeedFpm * Aero.Fpm, tasKnots * Aero.Kts);

        if (limit) {
            // limit gamma to about +/-10 degrees (~0.175 rad, same as Python)
            gamma = Math.Clamp(gamma, -0.175, 0.175);

            // limit acceleration to +/-5 m/s^2
            accelerationMs2 = Math.Clamp(accelerationMs2, -5.0, 5.0);
        }

        var T = D + massKg * 9.81 * Math.Sin(gamma) + massKg * accelerationMs2;

        if (limit) {
            var TMax = _thrust.Climb(tasKnots, altitudeFeet, rateOfClimbFpm: 0);
            var TIdle = _thrust.DescentIdle(tasKnots, altitudeFeet);

            if (T < TIdle * 0.8)
                T = TIdle * 0.8;
            if (T > TMax * 1.2)
                T = TMax * 1.2;
        }

        var fuelflow = AtThrust(T, limit);
        return fuelflow;
    }
}

internal sealed class FuelModelRecord {
    [Name("typecode")]
    public string TypeCode { get; set; } = string.Empty;

    [Name("engine_type")]
    public string EngineType { get; set; } = string.Empty;

    [Name("c1")]
    public double C1 { get; set; }

    [Name("c2")]
    public double C2 { get; set; }

    [Name("c3")]
    public double C3 { get; set; }
}


