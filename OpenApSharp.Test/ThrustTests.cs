using NUnit.Framework;

namespace OpenApSharp.Test;

public class ThrustTests {
    [Test]
    public void Takeoff_ReturnsPositiveThrust_ForTypicalConditions() {
        var thrust = new Thrust("A320", "CFM56-5B4");

        var F = thrust.Takeoff(100, 0);

        Assert.That(F, Is.GreaterThan(0).And.LessThan(1e7));
    }

    [Test]
    public void Climb_And_Cruise_AreFinite_ForTypicalConditions() {
        var thrust = new Thrust("A320", "CFM56-5B4");

        var climb = thrust.Climb(200, 20000, 1000);
        var cruise = thrust.Cruise(230, 32000);

        Assert.That(climb, Is.GreaterThan(0).And.LessThan(1e7));
        Assert.That(cruise, Is.GreaterThan(0).And.LessThan(1e7));
    }

    [Test]
    public void Synonym_Works() {
        var thrust = new Thrust("AT76", useSynonym: true);

        Assert.That(thrust.AircraftCode, Is.EqualTo("AT76"));
    }

}


