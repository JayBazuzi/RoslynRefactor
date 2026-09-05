namespace Sample;

class Base
{
    protected int Count;
}

class Widget : Base
{
    static void Increment(Widget widget, int by) => widget.Count += by;
}
