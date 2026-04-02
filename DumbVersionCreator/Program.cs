using System.Diagnostics;
using LibDumbVersion;

namespace DumbVersionCreator;

internal class Program
{
    private static void Main(string[] args)
    {
        args = CommandLineUtility.SanitizeArgs(args);

        if (args.Length == 0 ||
            args.Contains("-h", StringComparer.OrdinalIgnoreCase) ||
            args.Contains("-?", StringComparer.OrdinalIgnoreCase) ||
            args.Contains("--help", StringComparer.OrdinalIgnoreCase))
        {
            PrintUsage();
            return;
        }

        if (args.Length == 1)
        {
            string baseIsoFile = args[0];
            string? targetFolder = Path.GetDirectoryName(baseIsoFile);
            if (string.IsNullOrEmpty(targetFolder))
            {
                targetFolder = ".";
            }

            string outputFolder = Path.Combine(targetFolder, "DVPs");

            RunBulkMode(baseIsoFile, targetFolder, outputFolder);
            EnterToExit();
            return;
        }

        if (args[0].Equals("-bulk", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("--bulk", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length < 3)
            {
                PrintUsage();
                return;
            }

            string baseIsoFile = args[1];
            string targetFolder = args[2];
            string outputFolder = args.Length >= 4 ? args[3] : targetFolder;

            RunBulkMode(baseIsoFile, targetFolder, outputFolder);
        }
        else
        {
            string baseIsoFile = args[0];
            string targetIsoFile = args[1];
            string patchFile;

            if (args.Length < 3)
            {
                string? dir = Path.GetDirectoryName(targetIsoFile);
                if (string.IsNullOrEmpty(dir)) dir = ".";

                patchFile = Path.Combine(dir, Path.GetFileNameWithoutExtension(targetIsoFile) + ".dvp");
            }
            else
            {
                patchFile = args[2];
            }

            try
            {
                DiffEngine.CreatePatch(baseIsoFile, targetIsoFile, patchFile);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{ex.Message}");
                if (File.Exists(patchFile))
                {
                    try { File.Delete(patchFile); } catch { /* ignored */ }
                }
            }
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Single file mode:");
        Console.WriteLine("  DumbVersionCreator <base_file> <target_file> [output.dvp]\n");
        Console.WriteLine("Bulk mode:");
        Console.WriteLine("  DumbVersionCreator -bulk/--bulk <base_file> <target_folder> [output_folder]\n");
        Console.WriteLine("Auto-bulk mode:");
        Console.WriteLine("  DumbVersionCreator <base_file>");
        Console.WriteLine("  (Creates patches for all files in the base file's folder, outputs to 'DVPs' subfolder)");
    }

    private static void EnterToExit()
    {
        if (Console.IsOutputRedirected || Console.IsInputRedirected) return;
        Console.WriteLine("Press Enter to exit.");
        Console.ReadLine();
    }

    private static void RunBulkMode(string baseIsoFile, string targetFolder, string outputFolder)
    {
        try
        {
            if (!File.Exists(baseIsoFile))
            {
                Console.WriteLine($"Base file not found: {baseIsoFile}");
                return;
            }

            if (!Directory.Exists(targetFolder))
            {
                Console.WriteLine($"Target folder does not exist: {targetFolder}");
                return;
            }

            if (!Directory.Exists(outputFolder))
            {
                Directory.CreateDirectory(outputFolder);
            }

            var pathComparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            var baseExt = Path.GetExtension(baseIsoFile);
            var targetFiles = Directory.EnumerateFiles(targetFolder)
                .Where(f => Path.GetExtension(f).Equals(baseExt, pathComparison))
                .ToList();

            if (targetFiles.Count == 0)
            {
                Console.WriteLine($"No files found matching extension {baseExt} in {targetFolder}");
                return;
            }

            Console.WriteLine("Indexing base...");
            Stopwatch stopwatch = Stopwatch.StartNew();
            using var baseIndex = new BaseFileIndex(baseIsoFile);
            Console.WriteLine($"Base file indexed: {baseIndex.RecordCount} unique chunks");
            Console.WriteLine($"Took {stopwatch.Elapsed.TotalSeconds:0.00}s\n");

            int processedCount = 0;

            foreach (var targetIsoFile in targetFiles)
            {
                if (Path.GetFullPath(targetIsoFile).Equals(Path.GetFullPath(baseIsoFile), pathComparison))
                {
                    continue;
                }

                string patchFile = Path.Combine(outputFolder, Path.GetFileNameWithoutExtension(targetIsoFile) + ".dvp");

                Console.WriteLine($"Processing {Path.GetFileName(targetIsoFile)}");
                try
                {
                    DiffEngine.CreatePatch(baseIndex, baseIsoFile, targetIsoFile, patchFile);
                    processedCount++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to process {targetIsoFile}: {ex.Message}");
                    if (File.Exists(patchFile))
                    {
                        try { File.Delete(patchFile); } catch { /* ignored */ }
                    }
                }

                Console.WriteLine();
            }

            Console.WriteLine(processedCount == 0
                ? "No patches were created. Make sure there are target files with the same extension in the folder."
                : $"Successfully created {processedCount} patch(es).");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during bulk processing: {ex.Message}");
        }
    }
}