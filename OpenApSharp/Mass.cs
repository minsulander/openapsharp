using System;

namespace OpenApSharp;

/// <summary>
/// C# translation of openap.mass.from_range, scalar-only.
/// </summary>
public static class Mass
{
    /// <summary>
    /// Compute aircraft mass based on range, load factor, and fraction settings.
    /// distanceNm in nautical miles. Returns mass in kg or MTOW fraction.
    /// </summary>
    public static double FromRange(
        string typeCode,
        double distanceNm,
        double loadFactor = 0.8,
        bool returnFraction = false,
        bool useSynonym = false)
    {
        var ac = Prop.Aircraft(typeCode, useSynonym);

        var cruiseRangeNm = ac.Cruise?.Range ?? throw new InvalidOperationException(
            $"Cruise range not defined for aircraft {typeCode}.");

        var rangeFraction = distanceNm / cruiseRangeNm;
        rangeFraction = Math.Clamp(rangeFraction, 0.2, 1.0);

        // mfc is fuel volume in liters; factor 0.8025 converts to kg (as in Python comment).
        var maxFuelWeight = (ac.Mfc ?? 0.0) * 0.8025;
        var fuelWeight = rangeFraction * maxFuelWeight;

        var mtow = ac.Mtow ?? throw new InvalidOperationException(
            $"MTOW not defined for aircraft {typeCode}.");
        var oew = ac.Oew ?? throw new InvalidOperationException(
            $"OEW not defined for aircraft {typeCode}.");

        var payloadWeight = (mtow - maxFuelWeight - oew) * loadFactor;

        var mass = oew + fuelWeight + payloadWeight;

        return returnFraction ? mass / mtow : mass;
    }
}


