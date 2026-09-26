using System;

class Widget
{
    public int Value { get; set; }
}

class WidgetFactory
{
    private readonly int _offset = 1;

    Widget Make(int input)
    {
        var widget = new Widget
        {
            Value = /*[*/input + _offset/*]*/
        };
        return widget;
    }
}
