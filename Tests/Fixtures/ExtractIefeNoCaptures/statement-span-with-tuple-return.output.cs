using System;
using System.Linq;

class Program
{
    static void Tally()
    {
        var (count, total) = ((Func<(int count, int total)>)(static () =>
        {
            var count = 0;
            var total = 0;
            foreach (var i in Enumerable.Range(0, 5))
            {
                count++;
                total += i;
            }

            return (count, total);
        }))();
        Console.WriteLine($"{count} {total}");
    }
}
