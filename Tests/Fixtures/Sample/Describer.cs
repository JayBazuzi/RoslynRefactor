using System;

namespace Sample;

class Describer
{
    public void Describe(string name, int age)
    {
        Console.WriteLine($"{name} is {age}");
    }

    public void Run()
    {
        Describe("Ada", 36);
    }
}
