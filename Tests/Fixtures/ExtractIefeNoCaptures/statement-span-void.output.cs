using System;

class Program
{
    static void Main()
    {
        Console.WriteLine("start");
        ((Action)(static () =>
        {
            for (var i = 0; i < 3; i++)
            {
                Console.WriteLine(i);
            }
        }))();
        Console.WriteLine("end");
    }
}
