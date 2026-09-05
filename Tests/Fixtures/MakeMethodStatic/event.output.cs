using System;

namespace Sample;

class Widget
{
    event Action? Changed;

    static void Notify(Widget widget) => widget.Changed?.Invoke();
}
