using System;

class Program
{
    static void TypeOfUser<T>()
    {
        var t = ((Func<Type>)(static () => typeof(T)))();
        Console.WriteLine(t);
    }
}
