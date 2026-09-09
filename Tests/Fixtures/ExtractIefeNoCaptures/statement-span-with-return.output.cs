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

        var items = ((Func<int, List<string>>)(static (multiplier) =>
        {
            var items = new List<string>();
            foreach (var x in Enumerable.Range(0, 10))
            {
                items.Add((x * multiplier).ToString());
            }

            return items;
        }))(multiplier);
        Console.WriteLine(string.Join(",", items));
    }
}
