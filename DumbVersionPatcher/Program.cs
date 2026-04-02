using LibDumbVersion;

namespace DumbVersionPatcher;

internal class Program
{
    private static void Main(string[] args)
    {
        args = CommandLineUtility.SanitizeArgs(args);

        Console.Title = "DumbVersion Patcher";
        Console.WriteLine("DumbVersion Patcher");
        Console.WriteLine(@" /\_/\  ");
        Console.WriteLine(@"( o.o ) ");
        Console.WriteLine(@" > ^ <");

        if (args.Contains("-h", StringComparer.OrdinalIgnoreCase) ||
            args.Contains("-?", StringComparer.OrdinalIgnoreCase) ||
            args.Contains("--help", StringComparer.OrdinalIgnoreCase))
        {
            PrintHelp();
            return;
        }

        List<string> fileArgs = [];
        string outputDest = "";
        bool isBulk = false;
        bool isInfo = false;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("-bulk", StringComparison.OrdinalIgnoreCase) || args[i].Equals("--bulk", StringComparison.OrdinalIgnoreCase))
            {
                isBulk = true;
            }
            else if (args[i].Equals("-info", StringComparison.OrdinalIgnoreCase) || args[i].Equals("--info", StringComparison.OrdinalIgnoreCase) || args[i].Equals("-i", StringComparison.OrdinalIgnoreCase))
            {
                isInfo = true;
            }
            else if (args[i].Equals("-o", StringComparison.OrdinalIgnoreCase) ||
                     args[i].Equals("--output", StringComparison.OrdinalIgnoreCase))
            {
                if (i < args.Length - 1)
                {
                    outputDest = args[++i];
                }
                else
                {
                    Console.WriteLine("Error: -o/--output requires a path argument.");
                    return;
                }
            }
            else
            {
                fileArgs.Add(args[i]);
            }
        }

        if (isInfo)
        {
            if (fileArgs.Count == 0)
            {
                var dir = AppContext.BaseDirectory;
                var patchFiles = Directory.EnumerateFiles(dir, "*.dvp").ToList();

                if (patchFiles.Count == 0)
                {
                    Console.WriteLine("No patch files found in the current directory.");
                }
                else
                {
                    foreach (var pf in patchFiles)
                    {
                        PrintPatchInfo(pf);
                    }
                }
            }
            else
            {
                foreach (var arg in fileArgs)
                {
                    if (Directory.Exists(arg))
                    {
                        var files = Directory.EnumerateFiles(arg, "*.dvp").ToList();
                        if (files.Count == 0)
                            Console.WriteLine($"No patch files found in directory {arg}");
                        else
                            foreach (var f in files) PrintPatchInfo(f);
                    }
                    else if (File.Exists(arg))
                    {
                        PrintPatchInfo(arg);
                    }
                    else
                    {
                        Console.WriteLine($"Path not found: {arg}");
                    }
                }
            }

            return;
        }

        if (isBulk)
        {
            if (fileArgs.Count == 0)
            {
                PrintHelp();
                return;
            }

            string patchFolder = fileArgs[0];
            string baseSrc = fileArgs.Count > 1 ? fileArgs[1] : "";

            RunBulkMode(patchFolder, baseSrc, outputDest);
            EnterToExit();
            return;
        }

        if (fileArgs.Count == 0)
        {
            var dir = AppContext.BaseDirectory;
            var patchFiles = Directory.EnumerateFiles(dir, "*.dvp").ToList();

            if (patchFiles.Count == 0)
            {
                Console.WriteLine("No patch files found in the current directory.");
            }
            else
            {
                if (patchFiles.Count > 1 && !Console.IsOutputRedirected && !Console.IsInputRedirected)
                {
                    bool tuiActive = true;
                    while (tuiActive)
                    {
                        Console.WriteLine("Multiple patch files found. Which one do you want to apply?");
                        Console.WriteLine("[0] All patches in this directory");

                        for (int i = 0; i < patchFiles.Count; i++)
                            Console.WriteLine($"[{i + 1}] {Path.GetFileName(patchFiles[i])}");

                        Console.WriteLine("\nType 'info <number>' to view details about a patch.");
                        Console.Write("> ");

                        string? input = Console.ReadLine()?.Trim();
                        if (string.IsNullOrEmpty(input)) continue;

                        if (input.Equals("info", StringComparison.OrdinalIgnoreCase) ||
                            input.StartsWith("info ", StringComparison.OrdinalIgnoreCase))
                        {
                            string numStr = input.Length > 4 ? input[4..].Trim() : "";
                            if (int.TryParse(numStr, out int infoChoice) && infoChoice > 0 &&
                                infoChoice <= patchFiles.Count)
                            {
                                Console.WriteLine();
                                PrintPatchInfo(patchFiles[infoChoice - 1]);
                                Console.WriteLine();
                            }
                            else
                            {
                                Console.WriteLine("Please specify a valid patch number.\n");
                            }
                            continue;
                        }

                        if (int.TryParse(input, out int choice) && choice >= 0 && choice <= patchFiles.Count)
                        {
                            tuiActive = false;
                            if (choice == 0)
                            {
                                RunBulkMode(dir, "", outputDest);
                                EnterToExit();
                                return;
                            }

                            string selectedPatch = patchFiles[choice - 1];
                            ProcessPatch(selectedPatch, "", outputDest, destAsFile: false);
                        }
                        else
                        {
                            Console.WriteLine("Invalid selection\n");
                        }
                    }
                }
                else if (patchFiles.Count == 1)
                {
                    ProcessPatch(patchFiles[0], "", outputDest, destAsFile: false);
                }
                else
                {
                    Console.WriteLine("Multiple patch files found. Please specify the patch file to be used as a command-line argument, or use -bulk.");
                }
            }
        }
        else
        {
            var patchArgs = fileArgs.Where(x => x.EndsWith(".dvp", StringComparison.OrdinalIgnoreCase)).ToList();
            var baseArgs = fileArgs.Where(x => !x.EndsWith(".dvp", StringComparison.OrdinalIgnoreCase)).ToList();

            string isoSrc = baseArgs.Count > 0 ? baseArgs[0] : "";

            if (baseArgs.Count > 1)
            {
                Console.WriteLine($"Warning: Multiple non-patch files provided. Using {isoSrc} as base file and ignoring the rest.");
            }

            if (!patchArgs.Any())
            {
                Console.WriteLine("No patch files given.");
            }
            else
            {
                bool destAsFile = patchArgs.Count == 1;

                foreach (var patchFile in patchArgs)
                {
                    Console.WriteLine($"Processing {patchFile}...");
                    ProcessPatch(patchFile, isoSrc, outputDest, destAsFile);
                }
            }
        }

        EnterToExit();
    }

    private static void RunBulkMode(string patchFolder, string baseSrc, string outputDest)
    {
        if (!Directory.Exists(patchFolder))
        {
            Console.WriteLine($"Patch directory does not exist: {patchFolder}");
            return;
        }

        var patchFiles = Directory.EnumerateFiles(patchFolder, "*.dvp").ToList();
        if (patchFiles.Count == 0)
        {
            Console.WriteLine($"No .dvp files found in {patchFolder}");
            return;
        }

        bool hasOutputDest = !string.IsNullOrEmpty(outputDest);
        if (hasOutputDest && !Directory.Exists(outputDest))
        {
            Directory.CreateDirectory(outputDest);
        }

        Console.WriteLine($"\nFound {patchFiles.Count} patch files. Applying all...\n");

        var pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        Dictionary<string, byte[]> baseHashCache = new(pathComparer);

        foreach (var patchFile in patchFiles)
        {
            Console.WriteLine(new string('-', 60));
            Console.WriteLine($"Processing Patch: {Path.GetFileName(patchFile)}");

            ProcessPatch(patchFile, baseSrc, outputDest, destAsFile: false, isBulk: true, baseHashCache);
        }

        Console.WriteLine(new string('-', 60));
        Console.WriteLine("\nBulk patching completed.");
    }

    private static void ProcessPatch(string patchFile, string isoSrc, string outputDest, bool destAsFile, bool isBulk = false, Dictionary<string, byte[]>? hashCache = null)
    {
        var patchDir = Path.GetDirectoryName(Path.GetFullPath(patchFile));
        if (string.IsNullOrEmpty(patchDir)) patchDir = AppContext.BaseDirectory;

        string targetExt;
        string baseIsoPath = "";

        using (var patch = new PatchFile(patchFile, write: false))
        {
            targetExt = Path.GetExtension(patch.BaseFileName);

            if (string.IsNullOrEmpty(isoSrc))
            {
                string exactPath = Path.Combine(patchDir, patch.BaseFileName);

                if (File.Exists(exactPath))
                {
                    baseIsoPath = exactPath;
                }
                else
                {
                    var isoFiles = Directory.EnumerateFiles(patchDir, "*" + targetExt).ToList();
                    if (isoFiles.Count == 1)
                    {
                        baseIsoPath = isoFiles[0];
                    }
                    else if (isoFiles.Count > 1)
                    {
                        if (isBulk)
                        {
                            Console.WriteLine(
                                $"Error: Multiple potential base files found in {patchDir}. Cannot implicitly resolve base for {Path.GetFileName(patchFile)}.");
                            return;
                        }

                        if (!Console.IsOutputRedirected && !Console.IsInputRedirected)
                        {
                            Console.WriteLine("\nMultiple base files found. Which one do you want to use?");
                            for (int i = 0; i < isoFiles.Count; i++)
                                Console.WriteLine($"[{i + 1}] {Path.GetFileName(isoFiles[i])}");

                            Console.Write("> ");
                            if (int.TryParse(Console.ReadLine(), out int choice) && choice > 0 && choice <= isoFiles.Count)
                                baseIsoPath = isoFiles[choice - 1];
                            else
                            {
                                Console.WriteLine("Invalid selection.");
                                return;
                            }
                        }
                        else
                        {
                            Console.WriteLine("Multiple base files found. Please specify the base file explicitly.");
                            return;
                        }
                    }
                }
            }
            else if (Directory.Exists(isoSrc))
            {
                string exactPath = Path.Combine(isoSrc, patch.BaseFileName);
                if (File.Exists(exactPath))
                {
                    baseIsoPath = exactPath;
                }
                else
                {
                    var isoFiles = Directory.EnumerateFiles(isoSrc, "*" + targetExt).ToList();
                    if (isoFiles.Count == 1)
                    {
                        baseIsoPath = isoFiles[0];
                    }
                    else
                    {
                        Console.WriteLine($"Error: Base file '{patch.BaseFileName}' not found in '{isoSrc}'.");
                        return;
                    }
                }
            }
            else
            {
                baseIsoPath = isoSrc;
            }
        }

        if (string.IsNullOrEmpty(baseIsoPath) || !File.Exists(baseIsoPath))
        {
            Console.WriteLine($"Error: Required base file could not be resolved for {Path.GetFileName(patchFile)}.");
            return;
        }

        bool hasOutputDest = !string.IsNullOrEmpty(outputDest);
        bool isOutputDir = hasOutputDest && (!destAsFile || Directory.Exists(outputDest) ||
                                             outputDest.EndsWith(Path.DirectorySeparatorChar) ||
                                             outputDest.EndsWith(Path.AltDirectorySeparatorChar));

        string targetIsoName = Path.GetFileNameWithoutExtension(patchFile) + targetExt;
        string targetDir = isOutputDir ? outputDest : patchDir;

        switch (isOutputDir)
        {
            case true when !Directory.Exists(targetDir):
                Directory.CreateDirectory(targetDir);
                break;
            case false when !Directory.Exists(targetDir):
                Console.WriteLine($"Output directory {targetDir} does not exist.\n");
                return;
        }

        string targetIsoPath = (hasOutputDest && !isOutputDir) ? outputDest : Path.Combine(targetDir, targetIsoName);

        var pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (targetIsoPath.Equals(baseIsoPath, pathComparison))
        {
            string dir = Path.GetDirectoryName(targetIsoPath) ?? "";
            string fName = Path.GetFileNameWithoutExtension(targetIsoPath);
            targetIsoPath = Path.Combine(dir, fName + "_patched" + targetExt);
        }

        if (File.Exists(targetIsoPath))
        {
            if (isBulk)
            {
                Console.WriteLine($"File {targetIsoPath} already exists. Skipping.");
                return;
            }

            if (Console.IsInputRedirected)
            {
                Console.WriteLine($"File {targetIsoPath} already exists.");
                return;
            }

            Console.Write($"File {targetIsoPath} already exists. Overwrite this file? [Y/N] ");
            var key = Console.ReadKey();
            if (key.Key != ConsoleKey.Y)
            {
                Console.WriteLine("\nSkipping this file.\n");
                return;
            }

            Console.WriteLine();
        }

        try
        {
            byte[]? knownHash = null;
            if (hashCache != null && hashCache.TryGetValue(baseIsoPath, out var cached))
            {
                knownHash = cached;
            }

            _lastProgress = -1;
            DiffEngine.ApplyPatch(baseIsoPath, patchFile, targetIsoPath, DrawProgressBar, ref knownHash);

            if (hashCache != null && knownHash != null)
            {
                hashCache[baseIsoPath] = knownHash;
            }

            Console.WriteLine("\n\nFile patched successfully.\n");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nError: {ex.Message}\n");

            if (File.Exists(targetIsoPath))
            {
                Console.WriteLine("Cleaning up incomplete output file...");
                try { File.Delete(targetIsoPath); } catch { /* ignored */ }
            }
        }
    }

    private static void PrintPatchInfo(string patchFile)
    {
        try
        {
            using var patch = new PatchFile(patchFile, write: false);
            Console.WriteLine(new string('-', 60));
            Console.WriteLine($"Patch File:   {Path.GetFileName(patchFile)}");
            Console.WriteLine($"Base File:    {patch.BaseFileName}");
            Console.WriteLine($"Target Size:  {patch.TargetSize} bytes");
            Console.WriteLine($"Base Hash:    {Convert.ToHexString(patch.ExpectedBaseHash)}");
            Console.WriteLine($"Target Hash:  {Convert.ToHexString(patch.ExpectedTargetHash)}");
            Console.WriteLine(new string('-', 60));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reading details from {Path.GetFileName(patchFile)}: {ex.Message}");
        }
    }

    private static void PrintHelp()
    {
        string progFn = Path.GetFileName(Environment.GetCommandLineArgs()[0]);
        Console.WriteLine("Usage:");
        Console.WriteLine($"{progFn} [-o/--output output_path] [base_file] [patch1.dvp, patch2.dvp ...]");
        Console.WriteLine($"{progFn} -bulk/--bulk <patch_folder> [base_file_or_folder] [-o/--output output_folder]");
        Console.WriteLine($"{progFn} -info <patch_file_or_folder>");
        Console.WriteLine("\nOptions:");
        Console.WriteLine("-o/--output        Output filename for single patch file, output directory for multiple patch files");
        Console.WriteLine("-bulk/--bulk       Apply all patches in a folder automatically (disables interactive prompts).");
        Console.WriteLine("-info / -i         Display patch file details (base hash, target hash, base filename) and exit.");
        Console.WriteLine("\nNotes:");
        Console.WriteLine("If no arguments are given, .dvp files will be searched for in the folder this program is located in.");
        Console.WriteLine("If base file is not given, it will be searched for in the same directory as the .dvp file.");
        Console.WriteLine("If multiple applicable .dvp or base files are found, a menu will be shown to select the correct files.");
    }

    private static int _lastProgress = -1;

    private static void DrawProgressBar(int progress)
    {
        if (Console.IsOutputRedirected) return;
        if (progress == _lastProgress) return;
        _lastProgress = progress;

        Console.CursorLeft = 0;

        Span<char> buffer = stackalloc char[64];
        buffer[0] = '[';
        int filled = progress / 2;

        for (int i = 0; i < filled; i++) buffer[1 + i] = '█';
        for (int i = filled; i < 50; i++) buffer[1 + i] = '░';

        buffer[51] = ']';
        buffer[52] = ' ';

        if (!progress.TryFormat(buffer[53..], out int charsWritten)) return;
        buffer[53 + charsWritten] = '%';
        buffer[54 + charsWritten] = ' ';

        Console.Out.Write(buffer[..(55 + charsWritten)]);
    }

    private static void EnterToExit()
    {
        if (Console.IsOutputRedirected || Console.IsInputRedirected) return;
        Console.WriteLine("Press Enter to exit.");
        Console.ReadLine();
    }
}