using System;

class Widget
{
    int _count;

    public void Bump()
    {
        ((Action<Widget>)(static (widget) =>
        {
            var a = 1;
            var b = widget._count + a;
            Console.WriteLine(b);
        }))(this);
    }
}
