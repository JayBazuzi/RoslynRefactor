namespace Sample;

class Widget
{
    int _count;

    public void /*caret*/Increment(int by) => _count += by;
}

class Program
{
    static void Main()
    {
        var w = new Widget();
        w.Increment(3);
    }
}
