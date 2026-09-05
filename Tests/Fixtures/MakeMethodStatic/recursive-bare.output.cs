namespace Sample;

class Widget
{
    int _count;

    static void Increment(Widget widget, int by)
    {
        if (by > 1)
        {
            Increment(widget, 1);
            widget._count += by - 1;
        }
        else
        {
            widget._count += by;
        }
    }
}
