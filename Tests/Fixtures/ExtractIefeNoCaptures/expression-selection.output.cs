using System;
using System.Linq;

class Program
{
    static void Render()
    {
        var averagePlusOne = ((Func<int>)(static () => Enumerable.Range(0, 5).Sum() / Enumerable.Range(0, 5).Count()))() + 1;
        Console.WriteLine(averagePlusOne);
    }
}
