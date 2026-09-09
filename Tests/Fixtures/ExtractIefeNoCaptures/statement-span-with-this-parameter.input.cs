using System;

class Widget
{
    int _count;

    public void Bump()
    {
        // begin selection
        var a = 1;
        var b = _count + a;
        Console.WriteLine(b);
        // end selection
    }
}
