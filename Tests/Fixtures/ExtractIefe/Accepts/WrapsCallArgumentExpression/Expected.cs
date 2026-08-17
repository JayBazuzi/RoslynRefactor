using System;

class Program
{
    static void Main()
    {
        var greeting = "hello";
        Console.WriteLine(((Func<int>)(() => greeting.Length))());
    }
}
