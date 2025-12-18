using System;
using NUnit.Framework;

namespace OpenApSharp.Test;

public class WrapTests
{
    [Test]
    public void PrintAllWrapParametersForA320()
    {
        var wrap = new Wrap("A320");

        // Enumerate all public properties of type WrapParameter and print them.
        var props = typeof(Wrap).GetProperties(
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public);

        foreach (var prop in props)
        {
            if (prop.PropertyType != typeof(Wrap.WrapParameter))
                continue;

            var value = (Wrap.WrapParameter)prop.GetValue(wrap)!;

            Console.WriteLine($"{prop.Name}:");
            Console.WriteLine($"  Default: {value.Default}");
            Console.WriteLine($"  Minimum: {value.Minimum}");
            Console.WriteLine($"  Maximum: {value.Maximum}");
            Console.WriteLine($"  Model:   {value.StatisticalModel}");
            Console.WriteLine($"  Params:  {string.Join("|", value.StatisticalModelParameters)}");
            Console.WriteLine();
        }

        Assert.Pass("WRAP parameters printed to test output.");
    }
}


