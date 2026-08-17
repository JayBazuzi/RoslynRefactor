using System;

class Program
{
    static void Main()
    {
        ((Action)(() => Console.WriteLine("hello")))();
    }
}
