using System;
using System.Collections.Generic;
using System.Linq;

class Program
{
    static void Render()
    {
        var header = "H";
        var multiplier = 2;
        Console.WriteLine(header);

        // begin selection
        var items = new List<string>();
        foreach (var x in Enumerable.Range(0, 10))
        {
            items.Add((x * multiplier).ToString());
        }
        // end selection
        Console.WriteLine(string.Join(",", items));
    }
}
