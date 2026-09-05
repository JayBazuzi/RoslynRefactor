using System;

namespace Sample;

class Widget
{
    event Action? Changed;

    void /*caret*/Notify() => Changed?.Invoke();
}
