namespace Sample;

class Widget
{
    int Value { get; set; }

    static void Double(Widget widget)
    {
        widget.Value = widget.Value * 2;
    }
}
