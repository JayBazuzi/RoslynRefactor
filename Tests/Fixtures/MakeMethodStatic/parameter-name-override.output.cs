namespace Sample;

class Widget
{
    int _count;

    static void Increment(Widget instance, int by) => instance._count += by;
}
