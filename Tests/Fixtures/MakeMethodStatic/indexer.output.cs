namespace Sample;

class Widget
{
    int[] _items = new int[10];

    int this[int index]
    {
        get => _items[index];
        set => _items[index] = value;
    }

    static void Zero(Widget widget, int index) => widget[index] = 0;
}
