namespace OpenApSharp;

/// <summary>
/// Base class for drag models, mirroring openap.base.DragBase.
/// </summary>
public abstract class DragBase
{
    public string AircraftCode { get; }

    protected DragBase(string ac)
    {
        AircraftCode = ac.ToUpperInvariant();
    }

    /// <summary>
    /// Drag in clean configuration.
    /// </summary>
    public abstract double Clean(double massKg, double tasKnots, double altitudeFeet, double verticalSpeedFpm = 0);

    /// <summary>
    /// Drag in non-clean configuration (flaps / gear).
    /// </summary>
    public abstract double NonClean(
        double massKg,
        double tasKnots,
        double altitudeFeet,
        double flapAngleDeg,
        double verticalSpeedFpm = 0,
        bool landingGear = false);
}


