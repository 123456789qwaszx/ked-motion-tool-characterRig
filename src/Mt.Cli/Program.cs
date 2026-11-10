namespace Mt.Cli;

internal static class Program
{
    private static int Main(string[] args) => MtCli.Run(args, Console.Out, Console.Error);
}
