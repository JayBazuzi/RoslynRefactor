namespace Sample;

class Widget
{
    int _count;

    public static void Increment(Widget widget, int by) => widget._count += by;
}

class Program
{
    static void Main()
    {
        var w = new Widget();
        Widget.Increment(w, 3);
    }
}
