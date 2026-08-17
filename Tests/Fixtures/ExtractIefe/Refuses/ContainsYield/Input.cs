using System.Collections.Generic;

class Program
{
    static IEnumerable<int> Numbers()
    {
        /*[*/yield return 1;
        yield return 2;/*]*/
    }
}
