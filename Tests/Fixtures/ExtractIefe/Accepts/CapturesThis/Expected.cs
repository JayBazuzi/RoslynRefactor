using System;

class Counter
{
    int count;

    public void Bump()
    {
        ((Action)(() =>
        {
            count++;
            Console.WriteLine(count);
        }))();
    }
}
