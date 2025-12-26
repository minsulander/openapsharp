using NUnit.Framework;

namespace OpenApSharp.Test;

public class AeroTests {
    [Test]
    public void TestCasToTasAndBack() {
        var tas = Aero.CasToTas(300, 10000);
        Assert.That(tas, Is.EqualTo(440).Within(1));
        var cas = Aero.TasToCas(tas, 10000);
        Assert.That(cas, Is.EqualTo(300).Within(0.01));
    }
}
