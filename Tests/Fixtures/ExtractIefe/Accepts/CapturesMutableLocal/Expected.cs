using System;

class Program
{
    static void Main()
    {
        var total = 0;
        ((Action)(() =>
        {
            total += 1;
            total *= 2;
        }))();
        Console.WriteLine(total);
    }
}
