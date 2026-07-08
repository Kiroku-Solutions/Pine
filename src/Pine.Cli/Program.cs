using System;

namespace Pine.Cli;

public class Program
{
    public static void Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "dev" && args[1] == "init")
        {
            Console.WriteLine("Pine development environment initialized successfully.");
            // Actual init logic goes here
            return;
        }

        Console.WriteLine("Usage: pine <command>");
        Console.WriteLine("Commands:");
        Console.WriteLine("  dev init    Initializes the development environment.");
    }
}
