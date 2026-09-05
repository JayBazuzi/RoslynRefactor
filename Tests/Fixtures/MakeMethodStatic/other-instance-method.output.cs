using System;

namespace Sample;

class Widget
{
    int _count;

    static void Reset(Widget widget)
    {
        widget.Log();
        widget._count = 0;
    }

    void Log() => Console.WriteLine(_count);
}
