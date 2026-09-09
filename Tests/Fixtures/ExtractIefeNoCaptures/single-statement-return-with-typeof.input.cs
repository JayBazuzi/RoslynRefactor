using System;

class Program
{
    static void TypeOfUser<T>()
    {
        // begin selection
        var t = typeof(T);
        // end selection
        Console.WriteLine(t);
    }
}
