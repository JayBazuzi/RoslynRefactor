namespace Sample;

class Widget
{
    int Value { get; set; }

    void /*caret*/Double()
    {
        Value = this.Value * 2;
    }
}
