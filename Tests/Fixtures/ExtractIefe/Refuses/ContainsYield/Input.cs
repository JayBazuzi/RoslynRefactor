using System.Collections.Generic;

class Program
{
    static IEnumerable<int> Numbers()
    {
        /*start*/yield return 1;
        yield return 2;/*end*/
    }
}
