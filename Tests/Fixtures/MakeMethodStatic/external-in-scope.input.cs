namespace Sample;

class Widget
{
    int _count;

    void /*caret*/Increment(int by) => _count += by;

    void IncrementTwice()
    {
        Increment(1);
        Increment(1);
    }
}
