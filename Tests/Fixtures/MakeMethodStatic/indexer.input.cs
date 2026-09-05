namespace Sample;

class Widget
{
    int[] _items = new int[10];

    int this[int index]
    {
        get => _items[index];
        set => _items[index] = value;
    }

    void /*caret*/Zero(int index) => this[index] = 0;
}
