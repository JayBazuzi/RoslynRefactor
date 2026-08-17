using System;

class Program
{
    static void Main()
    {
        var greeting = "hello";
        ((Action)(() =>
        {
            Console.WriteLine(greeting);
            Console.WriteLine(greeting.Length);
        }))();
    }
}
