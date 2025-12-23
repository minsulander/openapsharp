using System;

namespace OpenApSharp;

/// <summary>
/// Simplified two-shaft turbofan thrust model, ported from openap.thrust.Thrust.
/// Inputs: TAS in knots, altitude in feet, ROC in ft/min. Output: total thrust (N).
/// </summary>
public sealed class Thrust : ThrustBase {
    private readonly double _cruiseAltFeet;
    private readonly double _engBpr;
    private readonly double _engMaxThrust;
    private readonly int _engNumber;
    private readonly double _cruiseMach;
    private readonly double _engCruiseThrust;

    public Thrust(string ac, string? eng = null, bool forceEngine = false, bool useSynonym = false)
        : base(ac) {
        var aircraft = Prop.Aircraft(ac, useSynonym);
        var engineInstall = aircraft.Engine
                             ?? throw new InvalidOperationException(
                                 $"Aircraft {ac} has no engine installation data.");

        if (string.IsNullOrWhiteSpace(eng)) {
            eng = engineInstall.Default
                   ?? throw new InvalidOperationException(
                       $"Default engine not specified for aircraft {ac}.");
        }

        var engine = Prop.Engine(eng.ToUpperInvariant());

        // Validate engine options similar to Python implementation.
        var engOptions = engineInstall.GetOptionValues();

        if (!forceEngine && engOptions.Length > 0) {
            var match = Array.Exists(
                engOptions,
                opt => engine.Name.Contains(opt, StringComparison.OrdinalIgnoreCase));

            if (!match) {
                throw new ArgumentException(
                    $"Engine {eng} and aircraft {ac} mismatch. " +
                    $"Available engines for {ac} are [{string.Join(", ", engOptions)}]");
            }
        }

        var cruiseHeightMeters = aircraft.Cruise?.Height
                                 ?? throw new InvalidOperationException(
                                     $"Cruise height not specified for aircraft {ac}.");
        // Python: cruise_alt stored in feet = aircraft['cruise']['height'] / aero.ft
        _cruiseAltFeet = cruiseHeightMeters / Aero.Ft;

        _engBpr = engine.Bpr;
        _engMaxThrust = engine.MaxThrust;
        _engNumber = engineInstall.Number;

        if (engine.CruiseMach > 0) {
            _cruiseMach = engine.CruiseMach;
            _engCruiseThrust = engine.CruiseThrust;
        } else {
            _cruiseMach = aircraft.Cruise?.Mach
                          ?? throw new InvalidOperationException(
                              $"Cruise Mach not specified for aircraft {ac}.");
            _engCruiseThrust = 0.2 * _engMaxThrust + 890.0;
        }
    }

    public override double Takeoff(double tasKnots, double altitudeFeet) {
        // Flight Mach number at sea level
        var mach = Aero.TasToMach(tasKnots * Aero.Kts, 0.0);

        // Engine bypass ratio
        var engBpr = _engBpr;

        // Gas generator function (fit to Fig. 5 in Bartel and Young)
        var G0 = 0.0606 * engBpr + 0.6337;

        var p = Aero.Pressure(altitudeFeet * Aero.Ft);
        var dP = p / Aero.P0;

        // Equations 12–14 in Bartel and Young
        var A = -0.4327 * dP * dP + 1.3855 * dP + 0.0472;
        var Z = 0.9106 * Math.Pow(dP, 3) - 1.7736 * dP * dP + 1.8697 * dP;
        var X = 0.1377 * Math.Pow(dP, 3) - 0.4374 * dP * dP + 1.3003 * dP;

        var ratio =
            A
            - 0.377 * (1 + engBpr) /
            Math.Sqrt((1 + 0.82 * engBpr) * G0) *
            Z * mach
            + (0.23 + 0.19 * Math.Sqrt(engBpr)) * X * mach * mach;

        var F = ratio * _engMaxThrust * _engNumber;
        return F;
    }

    public override double Climb(double tasKnots, double altitudeFeet, double rateOfClimbFpm) {
        var roc = Math.Abs(rateOfClimbFpm);
        var h = altitudeFeet * Aero.Ft;
        var tas = Math.Max(10.0, tasKnots);

        var mach = Aero.TasToMach(tas * Aero.Kts, h);
        var vcas = Aero.TasToCas(tas * Aero.Kts, h);

        var p = Aero.Pressure(h);
        var p10 = Aero.Pressure(10000.0 * Aero.Ft);
        var pcr = Aero.Pressure(_cruiseAltFeet * Aero.Ft);

        // Approximate thrust at top of climb
        var Fcr = _engCruiseThrust * _engNumber;
        var vcasRef = Aero.MachToCas(_cruiseMach, _cruiseAltFeet * Aero.Ft);

        // Segment 3: alt > 30000 ft
        var d = DFunc(mach / _cruiseMach);
        var b = Math.Pow(mach / _cruiseMach, -0.11); // Eq. 16
        var ratioSeg3 = d * Math.Log(p / pcr) + b;   // Eq. 15

        // Segment 2: 10000 < alt <= 30000
        var a = Math.Pow(vcas / vcasRef, -0.1);      // Eq. 18
        var n = NFunc(roc);
        var ratioSeg2 = a * Math.Pow(p / pcr, -0.355 * (vcas / vcasRef) + n); // Eq. 17

        // Segment 1: alt <= 10000
        var F10 = Fcr * a * Math.Pow(p10 / pcr, -0.355 * (vcas / vcasRef) + n);
        var m = MFunc(vcas / vcasRef, roc);
        var ratioSeg1 = m * (p / pcr) + (F10 / Fcr - m * (p10 / pcr)); // Eq. 19

        double ratio;
        if (altitudeFeet > 30000.0) {
            ratio = ratioSeg3;
        } else if (altitudeFeet > 10000.0) {
            ratio = ratioSeg2;
        } else {
            ratio = ratioSeg1;
        }

        var F = ratio * Fcr;
        return F;
    }

    private static double DFunc(double mratio)
        => -0.4204 * mratio + 1.0824;

    private static double NFunc(double rocFpm)
        => 2.667e-05 * rocFpm + 0.8633;

    private static double MFunc(double vratio, double rocFpm)
        => -1.2043e-1 * vratio
           - 8.8889e-9 * rocFpm * rocFpm
           + 2.4444e-5 * rocFpm
           + 4.7379e-1;
}


