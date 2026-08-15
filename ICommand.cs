using System.CommandLine;

namespace RoslynRefactor;

interface ICommand
{
    static abstract Command Build();
}
