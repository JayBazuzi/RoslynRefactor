using System;

class Program
{
    static void Main()
    {
        var total = 0;
        [|total += 1;
        total *= 2;|]
        Console.WriteLine(total);
    }
}
