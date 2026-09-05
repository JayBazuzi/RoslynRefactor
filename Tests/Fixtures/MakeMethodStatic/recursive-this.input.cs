namespace Sample;

class Widget
{
    int _count;

    void /*caret*/Increment(int by)
    {
        if (by > 1)
        {
            this.Increment(1);
            _count += by - 1;
        }
        else
        {
            _count += by;
        }
    }
}
