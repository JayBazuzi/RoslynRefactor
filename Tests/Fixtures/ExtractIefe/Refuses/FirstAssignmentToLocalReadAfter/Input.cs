using System;

class Program
{
    static void Main()
    {
        int total;
        [|total = 21 * 2;|]
        Console.WriteLine(total);
    }
}
