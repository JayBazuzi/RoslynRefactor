using System;

class Program
{
    static void Render()
    {
        ((Action)(static () => Console.WriteLine("hi")))();
    }
}
