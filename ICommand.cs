namespace RoslynRefactor;

interface ICommand
{
    static abstract string Name { get; }
    static abstract string Description { get; }
    static abstract Task<int> RunAsync(string[] args);
}
