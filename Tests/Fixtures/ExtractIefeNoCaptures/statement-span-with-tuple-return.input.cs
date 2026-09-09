using System;
using System.Linq;

class Program
{
    static void Tally()
    {
        // begin selection
        var count = 0;
        var total = 0;
        foreach (var i in Enumerable.Range(0, 5))
        {
            count++;
            total += i;
        }
        // end selection
        Console.WriteLine($"{count} {total}");
    }
}
