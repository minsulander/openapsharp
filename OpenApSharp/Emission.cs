using System;

namespace OpenApSharp;

/// <summary>
/// C# translation of openap.emission.Emission, scalar-only.
/// </summary>
public sealed class Emission {
    private readonly Aircraft _aircraft;
    private readonly Engine _engine;
    private readonly int _engineCount;

    public Emission(string ac, string? eng = null, bool useSynonym = false) {
        _aircraft = Prop.Aircraft(ac, useSynonym);
        _engineCount = _aircraft.Engine?.Number ?? 1;

        if (string.IsNullOrWhiteSpace(eng)) {
            eng = _aircraft.Engine?.Default
                  ?? throw new InvalidOperationException(
                      $"Default engine not specified for aircraft {ac}.");
        }

        _engine = Prop.Engine(eng);
    }

    private (double FfSeaLevelPerEngine, double Ratio) ToSeaLevelEquivalent(
        double ffacKgPerSecondAllEngines,
        double tasKnots,
        double altitudeFeet) {
        // Convert to Mach / ISA parameters
        var M = Aero.TasToMachFromKnots(tasKnots, altitudeFeet);
        var beta = Math.Exp(0.2 * (M * M));
        var theta = Aero.Temperature(altitudeFeet * Aero.Ft) / 288.15 / beta;
        var delta = Math.Pow(1 - 0.0019812 * altitudeFeet / 288.15, 5.255876)
                    / Math.Pow(beta, 3.5);
        var ratio = Math.Pow(theta, 3.3) / Math.Pow(delta, 1.02);

        // sea-level equivalent fuel flow per engine
        var ffSlPerEngine = (ffacKgPerSecondAllEngines / _engineCount)
                            * Math.Pow(theta, 3.8) / delta * beta;

        return (ffSlPerEngine, ratio);
    }

    public double Co2(double fuelFlowKgPerSecondAllEngines)
        => fuelFlowKgPerSecondAllEngines * 3160.0;

    public double H2o(double fuelFlowKgPerSecondAllEngines)
        => fuelFlowKgPerSecondAllEngines * 1230.0;

    public double Soot(double fuelFlowKgPerSecondAllEngines)
        => fuelFlowKgPerSecondAllEngines * 0.03;

    public double Sox(double fuelFlowKgPerSecondAllEngines)
        => fuelFlowKgPerSecondAllEngines * 1.2;

    public double Nox(double ffacKgPerSecondAllEngines, double tasKnots, double altitudeFeet = 0) {
        var (ffSlPerEngine, ratio) = ToSeaLevelEquivalent(ffacKgPerSecondAllEngines, tasKnots, altitudeFeet);

        var ff = new[]
        {
            _engine.FfIdl, _engine.FfApp, _engine.FfCo, _engine.FfTo
        };
        var ei = new[]
        {
            _engine.EiNoxIdl, _engine.EiNoxApp, _engine.EiNoxCo, _engine.EiNoxTo
        };

        var noxSl = Interp(ffSlPerEngine, ff, ei);

        var omega = 1e-3 * Math.Exp(-0.0001426 * (altitudeFeet - 12900.0));
        var noxFl = noxSl * Math.Sqrt(1.0 / ratio) * Math.Exp(-19.0 * (omega - 0.00634));

        // convert g/(kg fuel) to g/s for all engines
        var noxRate = noxFl * ffacKgPerSecondAllEngines;
        return noxRate;
    }

    public double Co(double ffacKgPerSecondAllEngines, double tasKnots, double altitudeFeet = 0) {
        var (ffSlPerEngine, ratio) = ToSeaLevelEquivalent(ffacKgPerSecondAllEngines, tasKnots, altitudeFeet);

        var ff = new[]
        {
            _engine.FfIdl, _engine.FfApp, _engine.FfCo, _engine.FfTo
        };
        var ei = new[]
        {
            _engine.EiCoIdl, _engine.EiCoApp, _engine.EiCoCo, _engine.EiCoTo
        };

        var coSl = Interp(ffSlPerEngine, ff, ei);

        // convert back to actual flight level (simple scaling)
        var coFl = coSl * ratio;
        var coRate = coFl * ffacKgPerSecondAllEngines;
        return coRate;
    }

    public double Hc(double ffacKgPerSecondAllEngines, double tasKnots, double altitudeFeet = 0) {
        var (ffSlPerEngine, ratio) = ToSeaLevelEquivalent(ffacKgPerSecondAllEngines, tasKnots, altitudeFeet);

        var ff = new[]
        {
            _engine.FfIdl, _engine.FfApp, _engine.FfCo, _engine.FfTo
        };
        var ei = new[]
        {
            _engine.EiHcIdl, _engine.EiHcApp, _engine.EiHcCo, _engine.EiHcTo
        };

        var hcSl = Interp(ffSlPerEngine, ff, ei);

        var hcFl = hcSl * ratio;
        var hcRate = hcFl * ffacKgPerSecondAllEngines;
        return hcRate;
    }

    /// <summary>
    /// 1D linear interpolation, equivalent to numpy.interp.
    /// xArray and yArray must be of equal length and sorted in ascending x.
    /// </summary>
    private static double Interp(double x, double[] xArray, double[] yArray) {
        if (xArray.Length != yArray.Length || xArray.Length == 0)
            throw new ArgumentException("Invalid interpolation arrays.");

        if (x <= xArray[0]) return yArray[0];
        if (x >= xArray[^1]) return yArray[^1];

        for (var i = 0; i < xArray.Length - 1; i++) {
            var x0 = xArray[i];
            var x1 = xArray[i + 1];
            if (x >= x0 && x <= x1) {
                var t = (x - x0) / (x1 - x0);
                return yArray[i] + t * (yArray[i + 1] - yArray[i]);
            }
        }

        // Fallback (should not reach here)
        return yArray[^1];
    }
}


