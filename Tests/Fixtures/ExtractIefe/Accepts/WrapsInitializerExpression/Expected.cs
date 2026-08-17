using System;

class Program
{
    static void Main()
    {
        var doubled = ((Func<int>)(() => 21 * 2))();
        Console.WriteLine(doubled);
    }
}
