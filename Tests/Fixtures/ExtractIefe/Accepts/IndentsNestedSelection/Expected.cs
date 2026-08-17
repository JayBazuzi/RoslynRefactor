using System;

class Program
{
    static void Main(bool flag)
    {
        if (flag)
        {
            ((Action)(() =>
            {
                Console.WriteLine("nested");
            }))();
        }
    }
}
