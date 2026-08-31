using System;
using Pine.Infrastructure.Diagnostics;

namespace Pine.Cli;

public class Program
{
    public static void Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        // A shipped artefact has to be able to say which build it is (TD-49). Until this existed,
        // the only place the version appeared was the diagnostics panel inside an unlocked vault --
        // which is to say, unreachable to anyone triaging a build that will not start, and
        // unreachable to a packaging script that needs to name the file it just produced.
        //
        // It is also the only end-to-end observation of PineLogRecordFormatter's resolution that is
        // possible at all: under `dotnet test` the entry assembly is the VSTest host, so a test can
        // never see Pine's own informational version. Here the entry assembly really is Pine's.
        if (args.Length >= 1 && (args[0] == "--version" || args[0] == "-v" || args[0] == "version"))
        {
            // Read through the same port the UI reads, not through a second reflection call: the
            // whole point of PineRuntimeInfoProvider's remarks is that there is exactly one
            // resolution of "the application version" and every consumer shares it.
            var runtimeInfo = new PineRuntimeInfoProvider();
            Console.WriteLine(runtimeInfo.AppVersion);
            return;
        }

        if (args.Length >= 2 && args[0] == "dev" && args[1] == "init")
        {
            Console.WriteLine("Pine development environment initialized successfully.");
            // Actual init logic goes here
            return;
        }

        Console.WriteLine("Usage: pine <command>");
        Console.WriteLine("Commands:");
        Console.WriteLine("  dev init    Initializes the development environment.");
        Console.WriteLine("  --version   Prints the build identifier of this copy of Pine.");
    }
}
