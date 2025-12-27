using System;

namespace OpenApSharp;

/// <summary>
/// Aeronautical constants and ISA atmosphere / speed conversion utilities,
/// ported from openap.extra.aero.
/// All altitudes are in meters, speeds in m/s unless stated otherwise.
/// </summary>
public static class Aero {
    // Unit conversion factors
    public const double Kts = 0.514444;         // knot -> m/s
    public const double Ft = 0.3048;            // ft -> m
    public const double Fpm = 0.00508;          // ft/min -> m/s

    // Constants
    public const double G0 = 9.80665;           // m/s2
    public const double R = 287.05287;          // gas constant
    public const double P0 = 101325.0;          // sea-level pressure (Pa)
    public const double Rho0 = 1.225;           // sea-level density (kg/m3)
    public const double T0 = 288.15;            // sea-level temperature (K)
    public const double Gamma = 1.40;           // cp/cv for air
    public const double Beta = -0.0065;         // ISA temp gradient below tropopause
    public const double Rearth = 6371000.0;     // m, average earth radius
    public const double A0 = 340.293988;        // sea-level speed of sound

    /// <summary>
    /// Returns (pressure, density, temperature) at altitude h (meters) with optional delta-T.
    /// </summary>
    public static (double Pressure, double Density, double Temperature) Atmos(double h, double dT = 0) {
        dT = Math.Max(-15, Math.Min(15, dT));
        var t0Shift = T0 + dT;

        var T = Math.Max(t0Shift + Beta * h, 216.65 + dT);
        var rhotrop = Rho0 * Math.Pow(T / t0Shift, 4.256848030018761);
        var dhstrat = Math.Max(0.0, h - 11000.0);
        var rho = rhotrop * Math.Exp(-dhstrat / 6341.552161);
        var p = rho * R * T;
        return (p, rho, T);
    }

    public static double Pressure(double h, double dT = 0) => Atmos(h, dT).Pressure;

    public static double Density(double h, double dT = 0) => Atmos(h, dT).Density;

    public static double Temperature(double h, double dT = 0) => Atmos(h, dT).Temperature;

    public static double SoundSpeed(double h, double dT = 0) {
        var T = Temperature(h, dT);
        return Math.Sqrt(Gamma * R * T);
    }

    public static double TasToMach(double vTas, double h, double dT = 0) {
        var a = SoundSpeed(h, dT);
        return vTas / a;
    }

    public static double MachToTas(double mach, double h, double dT = 0) {
        var a = SoundSpeed(h, dT);
        return mach * a;
    }
    public static double CasToTas(double vCas, double h, double dT = 0) {
        var (p, rho, _) = Atmos(h, dT);
        var qdyn = P0 * (Math.Pow(1.0 + Rho0 * vCas * vCas / (7.0 * P0), 3.5) - 1.0);
        var vTas = Math.Sqrt(7.0 * p / rho * (Math.Pow(1.0 + qdyn / p, 2.0 / 7.0) - 1.0));
        return vTas;
    }
    
    public static double CasToTasFromKnots(double casKnots, double altitudeFeet)
        => CasToTas(casKnots * Kts, altitudeFeet * Ft);

    public static double TasToCas(double vTas, double h, double dT = 0) {
        var (p, rho, _) = Atmos(h, dT);
        var qdyn = p * (Math.Pow(1.0 + rho * vTas * vTas / (7.0 * p), 3.5) - 1.0);
        var vCas = Math.Sqrt(7.0 * P0 / Rho0 * (Math.Pow(qdyn / P0 + 1.0, 2.0 / 7.0) - 1.0));
        return vCas;
    }

    public static double TasToMachFromKnots(double tasKnots, double altitudeFeet)
        => TasToMach(tasKnots * Kts, altitudeFeet * Ft);

    public static double TasToCasFromKnots(double tasKnots, double altitudeFeet)
        => TasToCas(tasKnots * Kts, altitudeFeet * Ft);

    public static double MachToCas(double mach, double h, double dT = 0) {
        var vTas = MachToTas(mach, h, dT);
        return TasToCas(vTas, h, dT);
    }
}


