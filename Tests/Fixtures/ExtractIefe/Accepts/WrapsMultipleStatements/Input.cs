using System;

class Program
{
    static void Main()
    {
        var greeting = "hello";
        [|Console.WriteLine(greeting);
        Console.WriteLine(greeting.Length);|]
    }
}
