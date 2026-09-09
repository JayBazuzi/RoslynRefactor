using System;
using System.Collections.Generic;
using System.Linq;

namespace Sample;

class IefeSample
{
    static void Render()
    {
        var header = "Items";
        Console.WriteLine(header);

        var items = new List<string>();
        foreach (var x in Enumerable.Range(0, 3))
        {
            items.Add(x.ToString());
        }
        Console.WriteLine(string.Join(",", items));
    }
}
