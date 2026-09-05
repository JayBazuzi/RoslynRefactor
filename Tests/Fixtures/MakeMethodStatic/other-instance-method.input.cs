using System;

namespace Sample;

class Widget
{
    int _count;

    void /*caret*/Reset()
    {
        Log();
        _count = 0;
    }

    void Log() => Console.WriteLine(_count);
}
