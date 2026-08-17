using System;
using System.Threading.Tasks;

class Program
{
    static async Task Main()
    {
        await ((Func<Task>)(async () =>
        {
            await Task.Delay(1);
            Console.WriteLine("done");
        }))();
    }
}
