using System;

class Program
{
    static void Main()
    {
        ((Action)(() =>
        {
            var doubled = 21 * 2;
            Console.WriteLine(doubled);
        }))();
    }
}
