using System;

class Program
{
    static void Main(bool flag)
    {
        if (((Func<bool>)(() => flag == true))())
        {
            Console.WriteLine("yes");
        }
    }
}
