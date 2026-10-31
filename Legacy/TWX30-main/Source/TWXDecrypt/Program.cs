using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using TWXD;
using TWXProxy.Core;

namespace TWXDecrypt;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (!TryParseArgs(args, out string? inputPath, out string? outputPath, out bool compactWhitespace))
        {
            PrintUsage();
            return 2;
        }

        string input = Path.GetFullPath(inputPath!);
        string output = Path.GetFullPath(outputPath!);
        if (!File.Exists(input))
        {
            Console.Error.WriteLine($"Input file not found: {input}");
            return 1;
        }

        if (File.Exists(output))
        {
            Console.Error.WriteLine($"Refusing to overwrite existing output: {output}");
            return 1;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            string extension = Path.GetExtension(input);
            IReadOnlyList<string> generated;
            if (extension.Equals(".inc", StringComparison.OrdinalIgnoreCase))
            {
                string source = LegacyScriptEncryption.DecryptToString(File.ReadAllBytes(input));
                File.WriteAllText(output, source, Encoding.Latin1);
                generated = new[] { output };
            }
            else if (extension.Equals(".cts", StringComparison.OrdinalIgnoreCase))
            {
                var decompiler = new ScriptDecompiler(new ScriptRef())
                {
                    CompactWhitespace = compactWhitespace,
                    BackupExisting = false,
                    OverwriteExisting = false
                };
                decompiler.LoadFromFile(input);
                generated = decompiler.DecompileToFile(output);
            }
            else
            {
                Console.Error.WriteLine("Input must have an .inc or .cts extension.");
                return 2;
            }

            Console.WriteLine($"Recovered: {output}");
            foreach (string file in generated.Skip(1))
                Console.WriteLine($"Recovered include: {file}");
            Console.WriteLine($"Input SHA256: {ComputeSha256(input)}");
            foreach (string file in generated)
                Console.WriteLine($"Output SHA256 ({file}): {ComputeSha256(file)}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Recovery failed: {ex.Message}");
            return 1;
        }
    }

    private static bool TryParseArgs(
        string[] args,
        out string? input,
        out string? output,
        out bool compactWhitespace)
    {
        input = null;
        output = null;
        compactWhitespace = false;

        for (int index = 0; index < args.Length; index++)
        {
            switch (args[index].ToLowerInvariant())
            {
                case "--input" when index + 1 < args.Length:
                    input = args[++index];
                    break;
                case "--output" when index + 1 < args.Length:
                    output = args[++index];
                    break;
                case "--compact-whitespace":
                    compactWhitespace = true;
                    break;
                default:
                    return false;
            }
        }

        return !string.IsNullOrWhiteSpace(input) && !string.IsNullOrWhiteSpace(output);
    }

    private static string ComputeSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void PrintUsage()
    {
        Console.WriteLine("TWXDecrypt - recover legacy and compiled TWX scripts");
        Console.WriteLine("Usage: twxdecrypt --input <script.inc|script.cts> --output <script.ts> [--compact-whitespace]");
        Console.WriteLine(".inc files use the legacy TWX decryption algorithm.");
        Console.WriteLine(".cts files use the TWXD bytecode reader and decompiler.");
        Console.WriteLine("Existing outputs are never overwritten.");
    }
}