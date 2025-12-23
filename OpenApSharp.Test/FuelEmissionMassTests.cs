using NUnit.Framework;

namespace OpenApSharp.Test;

public class FuelEmissionMassTests {
    [Test]
    public void FuelFlow_Takeoff_And_Enroute_ArePositive() {
        var ff = new FuelFlow("A320", "CFM56-5B4");

        var to = ff.Takeoff(tasKnots: 140, altitudeFeet: 0);
        var enroute = ff.Enroute(
            massKg: 65000,
            tasKnots: 250,
            altitudeFeet: 30000,
            verticalSpeedFpm: 0,
            accelerationMs2: 0);

        Assert.That(to, Is.GreaterThan(0));
        Assert.That(enroute, Is.GreaterThan(0));
    }

    [Test]
    public void Emission_ComputesReasonableCoefficients() {
        var em = new Emission("A320", "CFM56-5B4");

        var ff = 1.5; // kg/s for all engines
        var co2 = em.Co2(ff);
        var h2o = em.H2o(ff);
        var soot = em.Soot(ff);
        var sox = em.Sox(ff);

        Assert.Multiple(() => {
            Assert.That(co2, Is.GreaterThan(h2o));
            Assert.That(h2o, Is.GreaterThan(0));
            Assert.That(soot, Is.GreaterThan(0));
            Assert.That(sox, Is.GreaterThan(0));
        });
    }

    [Test]
    public void Mass_FromRange_ReturnsFractionWithinBounds() {
        var fraction = Mass.FromRange("A320", distanceNm: 1000, loadFactor: 0.8, returnFraction: true);

        Assert.That(fraction, Is.InRange(0.2, 1.0));
    }
}


