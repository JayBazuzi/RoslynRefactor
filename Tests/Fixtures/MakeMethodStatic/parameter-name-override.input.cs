// parameter-name: instance
namespace Sample;

class Widget
{
    int _count;

    void /*caret*/Increment(int by) => _count += by;
}
