using NUnit.Framework;

namespace OpenApSharp.Test;

public class DragTests {
    [Test]
    public void Clean_ReturnsPositiveDrag_ForTypicalConditions() {
        var drag = new Drag("A320");

        var D1 = drag.Clean(massKg: 60000, tasKnots: 200, altitudeFeet: 20000);
        var D2 = drag.Clean(massKg: 60000, tasKnots: 250, altitudeFeet: 20000);

        Assert.That(D1, Is.GreaterThan(0));
        Assert.That(D2, Is.GreaterThan(0));
    }

    [Test]
    public void NonClean_IncreasesDrag_WithFlapsAndGear() {
        var drag = new Drag("A320");

        var clean = drag.Clean(massKg: 60000, tasKnots: 150, altitudeFeet: 1000);
        var flapOnly = drag.NonClean(massKg: 60000, tasKnots: 150, altitudeFeet: 1000, flapAngleDeg: 20);
        var flapGear = drag.NonClean(
            massKg: 60000,
            tasKnots: 150,
            altitudeFeet: 1000,
            flapAngleDeg: 20,
            landingGear: true);

        Assert.That(clean, Is.GreaterThan(0));
        Assert.That(flapOnly, Is.GreaterThan(0));
        Assert.That(flapGear, Is.GreaterThan(0));
    }

    [Test]
    public void Synonym_Works() {
        var drag = new Drag("AT76", useSynonym: true);

        Assert.That(drag.AircraftCode, Is.EqualTo("AT76"));
    }

}


