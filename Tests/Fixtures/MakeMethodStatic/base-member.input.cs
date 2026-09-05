namespace Sample;

class Base
{
    protected int Count;
}

class Widget : Base
{
    void /*caret*/Increment(int by) => Count += by;
}
