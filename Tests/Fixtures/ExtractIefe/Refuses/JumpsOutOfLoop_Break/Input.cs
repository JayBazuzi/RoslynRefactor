using System;

class Program
{
    static void Main()
    {
        for (var i = 0; i < 10; i++)
        {
            /*start*/if (i > 3)
            {
                break;
            }

            Console.WriteLine(i);/*end*/
        }
    }
}
