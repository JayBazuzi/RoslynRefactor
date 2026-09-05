namespace Sample;

class Widget
{
    int _count;

    static void Increment(Widget widget, int by) => widget._count += by;

    void IncrementTwice()
    {
        Increment(this, 1);
        Increment(this, 1);
    }
}
