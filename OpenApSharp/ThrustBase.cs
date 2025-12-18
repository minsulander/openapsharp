using System;

namespace OpenApSharp;

/// <summary>
/// Base class for thrust models, mirroring openap.base.ThrustBase.
/// </summary>
public abstract class ThrustBase
{
    public string AircraftCode { get; }

    protected ThrustBase(string ac)
    {
        AircraftCode = ac.ToUpperInvariant();
    }

    public abstract double Takeoff(double tasKnots, double altitudeFeet);

    public abstract double Climb(double tasKnots, double altitudeFeet, double rateOfClimbFpm);

    public virtual double Cruise(double tasKnots, double altitudeFeet)
        => Climb(tasKnots, altitudeFeet, 0.0);

    public virtual double DescentIdle(double tasKnots, double altitudeFeet)
        => 0.07 * Takeoff(tasKnots, altitudeFeet);
}


