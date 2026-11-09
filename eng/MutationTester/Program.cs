using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MutationTester;

/// <summary>
/// Lightweight Roslyn-based mutation testing tool for Beam.
///
/// For each source file in Beam.Core / Beam.App, applies small semantic
/// mutations (operator flips, literal changes, boolean negation) directly to
/// the source file, builds, runs the test suite, then restores the original.
///
/// A mutant that SURVIVES (tests still pass) indicates either:
///   * a weak test that doesn't catch the code change ("evergreen test"), or
///   * dead code that no test exercises.
///
/// A mutant that is KILLED (tests fail) means the test suite catches the change.
///
/// Usage: dotnet run --project eng/MutationTester -- <src-project.csproj> <test-project.csproj> [output.json]
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: dotnet run --project eng/MutationTester -- <src-project.csproj> <test-project.csproj> [output.json] [maxMutants]");
            return 2;
        }

        string srcProj = Path.GetFullPath(args[0]);
        string testProj = Path.GetFullPath(args[1]);
        string? outputPath = args.Length > 2 && !string.IsNullOrEmpty(args[2]) ? args[2] : null;
        int maxMutants = args.Length > 3 ? int.Parse(args[3]) : int.MaxValue;

        var runner = new MutationRunner(srcProj, testProj, maxMutants);
        var report = runner.Run();

        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        if (outputPath is not null)
        {
            File.WriteAllText(outputPath, json);
            Console.WriteLine($"Report written to {outputPath}");
        }
        else
        {
            Console.WriteLine(json);
        }

        Console.WriteLine();
        Console.WriteLine($"Mutation score: {report.MutationScore:P1} ({report.KilledMutants}/{report.TotalMutants})");
        Console.WriteLine($"Surviving mutants: {report.SurvivingMutants.Count}");

        return report.SurvivingMutants.Count > 0 ? 1 : 0;
    }
}

/// <summary>
/// Result of running mutation testing against one source project.
/// </summary>
internal sealed class MutationReport
{
    public string SourceProject { get; init; } = string.Empty;
    public string TestProject { get; init; } = string.Empty;
    public int TotalMutants { get; init; }
    public int KilledMutants { get; init; }
    public int SurvivingCount => TotalMutants - KilledMutants;
    public double MutationScore => TotalMutants == 0 ? 0.0 : (double)KilledMutants / TotalMutants;
    public List<MutantResult> SurvivingMutants { get; init; } = new();
    public List<MutantResult> KilledMutantList { get; init; } = new();
    public TimeSpan Elapsed { get; init; }
}

/// <summary>
/// Result for one individual mutant.
/// </summary>
internal sealed class MutantResult
{
    public string File { get; init; } = string.Empty;
    public int Line { get; init; }
    public string Mutation { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public bool Killed { get; init; }
    public string? TestOutput { get; init; }
}

/// <summary>
/// Orchestrates mutation testing: enumerate files, apply mutations, build, test, report.
/// </summary>
internal sealed class MutationRunner
{
    private readonly string _srcProj;
    private readonly string _testProj;
    private readonly string _srcDir;
    private readonly string _repoRoot;
    private int _maxMutants = int.MaxValue;
    private bool _testBuilt = false;

    public MutationRunner(string srcProj, string testProj, int maxMutants = int.MaxValue)
    {
        _srcProj = srcProj;
        _testProj = testProj;
        _maxMutants = maxMutants;
        _srcDir = Path.GetDirectoryName(_srcProj)!;
        _repoRoot = Path.GetFullPath(Path.Combine(_srcDir, "..", "..", ".."));
    }

    public MutationReport Run()
    {
        var sw = Stopwatch.StartNew();

        // Pre-build the test project so we can use --no-build for subsequent runs
        Console.WriteLine($"Pre-building test project {Path.GetFileName(_testProj)}...");
        var testBuild = BuildProject(_testProj);
        if (!testBuild.Success)
        {
            Console.Error.WriteLine($"Failed to build test project: {testBuild.Output}");
        }
        _testBuilt = true;

        var csFiles = Directory.GetFiles(_srcDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                     && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
            .ToList();

        Console.WriteLine($"Found {csFiles.Count} source files in {Path.GetFileName(_srcProj)}");

        int mutantsRun = 0;
        foreach (var file in csFiles)
        {
            if (mutantsRun >= _maxMutants) break;
            var relFile = Path.GetRelativePath(_srcDir, file);
            Console.WriteLine($"  Mutating {relFile}...");
            mutantsRun += MutateFile(file, relFile);
        }

        sw.Stop();

        var killed = _results.Count(r => r.Killed);
        var report = new MutationReport
        {
            SourceProject = _srcProj,
            TestProject = _testProj,
            TotalMutants = _results.Count,
            KilledMutants = killed,
            SurvivingMutants = _results.Where(r => !r.Killed).ToList(),
            KilledMutantList = _results.Where(r => r.Killed).ToList(),
            Elapsed = sw.Elapsed,
        };

        return report;
    }

    private readonly List<MutantResult> _results = new();

    private int MutateFile(string filePath, string relFile)
    {
        var originalSource = File.ReadAllText(filePath);
        SyntaxTree tree;
        try
        {
            tree = CSharpSyntaxTree.ParseText(originalSource, path: filePath);
        }
        catch
        {
            // Can't parse this file, skip it.
            return 0;
        }

        var root = tree.GetRoot();
        var mutations = EnumerateMutations(root, tree, relFile).ToList();
        int runCount = 0;

        foreach (var mutation in mutations)
        {
            if (_results.Count >= _maxMutants) break;

            // Apply mutation directly to the source file
            var mutatedRoot = root.ReplaceNode(mutation.Node, mutation.Replacement);
            var mutatedTree = tree.WithRootAndOptions(mutatedRoot, tree.Options);
            var mutatedSource = mutatedTree.GetText().ToString();

            File.WriteAllText(filePath, mutatedSource);

            try
            {
                // Build the source project (mutated)
                var buildResult = BuildProject(_srcProj);
                if (!buildResult.Success)
                {
                    // Compilation failure = mutant killed
                    _results.Add(new MutantResult
                    {
                        File = relFile,
                        Line = mutation.Line,
                        Mutation = mutation.Kind,
                        Description = mutation.Description,
                        Killed = true,
                        TestOutput = buildResult.Output,
                    });
                    runCount++;
                    continue;
                }

                // Run the test project against the mutated source
                var testResult = RunTests();
                _results.Add(new MutantResult
                {
                    File = relFile,
                    Line = mutation.Line,
                    Mutation = mutation.Kind,
                    Description = mutation.Description,
                    Killed = testResult.Failed,
                    TestOutput = testResult.Output,
                });
                runCount++;
            }
            finally
            {
                // Always restore the original source
                File.WriteAllText(filePath, originalSource);
            }
        }

        return runCount;
    }

    private IEnumerable<Mutation> EnumerateMutations(SyntaxNode root, SyntaxTree tree, string relFile)
    {
        foreach (var node in root.DescendantNodes())
        {
            int line = tree.GetLineSpan(node.Span).StartLinePosition.Line + 1;

            switch (node)
            {
                case BinaryExpressionSyntax be:
                {
                    string op = be.OperatorToken.Text;

                    // Arithmetic operators
                    if (op == "+")
                        yield return new Mutation(be, be.WithOperatorToken(SyntaxFactory.Token(SyntaxKind.MinusToken)),
                            relFile, line, "Arithmetic", "replace '+' with '-'");
                    if (op == "-")
                        yield return new Mutation(be, be.WithOperatorToken(SyntaxFactory.Token(SyntaxKind.PlusToken)),
                            relFile, line, "Arithmetic", "replace '-' with '+'");
                    if (op == "*")
                        yield return new Mutation(be, be.WithOperatorToken(SyntaxFactory.Token(SyntaxKind.SlashToken)),
                            relFile, line, "Arithmetic", "replace '*' with '/'");
                    if (op == "/")
                        yield return new Mutation(be, be.WithOperatorToken(SyntaxFactory.Token(SyntaxKind.AsteriskToken)),
                            relFile, line, "Arithmetic", "replace '/' with '*'");

                    // Comparison operators
                    if (op == "==")
                        yield return new Mutation(be, be.WithOperatorToken(SyntaxFactory.ParseToken("!=")),
                            relFile, line, "Comparison", "replace '==' with '!='");
                    if (op == "!=")
                        yield return new Mutation(be, be.WithOperatorToken(SyntaxFactory.ParseToken("==")),
                            relFile, line, "Comparison", "replace '!=' with '=='");
                    if (op == "<")
                        yield return new Mutation(be, be.WithOperatorToken(SyntaxFactory.ParseToken(">")),
                            relFile, line, "Comparison", "replace '<' with '>'");
                    if (op == ">")
                        yield return new Mutation(be, be.WithOperatorToken(SyntaxFactory.ParseToken("<")),
                            relFile, line, "Comparison", "replace '>' with '<'");
                    if (op == "<=")
                        yield return new Mutation(be, be.WithOperatorToken(SyntaxFactory.ParseToken(">=")),
                            relFile, line, "Comparison", "replace '<=' with '>='");
                    if (op == ">=")
                        yield return new Mutation(be, be.WithOperatorToken(SyntaxFactory.ParseToken("<=")),
                            relFile, line, "Comparison", "replace '>=' with '<='");

                    // Logical operators
                    if (op == "&&")
                        yield return new Mutation(be, be.WithOperatorToken(SyntaxFactory.ParseToken("||")),
                            relFile, line, "Logical", "replace '&&' with '||'");
                    if (op == "||")
                        yield return new Mutation(be, be.WithOperatorToken(SyntaxFactory.ParseToken("&&")),
                            relFile, line, "Logical", "replace '||' with '&&'");
                    break;
                }

                case PrefixUnaryExpressionSyntax pue:
                {
                    string op = pue.OperatorToken.Text;
                    if (op == "!")
                        // Remove the '!' by replacing the whole expression with its operand
                        yield return new Mutation(pue, pue.Operand,
                            relFile, line, "Logical", "remove '!' (boolean negation)");
                    if (op == "-")
                        yield return new Mutation(pue, pue.WithOperatorToken(SyntaxFactory.Token(SyntaxKind.PlusToken)),
                            relFile, line, "Arithmetic", "replace unary '-' with '+'");
                    break;
                }

                case LiteralExpressionSyntax lit:
                {
                    if (lit.Token.IsKind(SyntaxKind.NumericLiteralToken))
                    {
                        var text = lit.Token.Text;
                        var mutated = text == "0" ? "1" : "0";
                        // ParseToken correctly creates a numeric literal token from text
                        var newToken = SyntaxFactory.ParseToken(mutated);
                        yield return new Mutation(lit, lit.WithToken(newToken),
                            relFile, line, "Literal", $"replace numeric literal '{text}' with '{mutated}'");
                    }

                    if (lit.Token.IsKind(SyntaxKind.TrueKeyword))
                        yield return new Mutation(lit, lit.WithToken(SyntaxFactory.Token(SyntaxKind.FalseKeyword)),
                            relFile, line, "Literal", "replace 'true' with 'false'");
                    if (lit.Token.IsKind(SyntaxKind.FalseKeyword))
                        yield return new Mutation(lit, lit.WithToken(SyntaxFactory.Token(SyntaxKind.TrueKeyword)),
                            relFile, line, "Literal", "replace 'false' with 'true'");
                    break;
                }

                case AssignmentExpressionSyntax ae:
                {
                    if (ae.OperatorToken.IsKind(SyntaxKind.PlusEqualsToken))
                        yield return new Mutation(ae, ae.WithOperatorToken(SyntaxFactory.Token(SyntaxKind.MinusEqualsToken)),
                            relFile, line, "Assignment", "replace '+=' with '-='");
                    if (ae.OperatorToken.IsKind(SyntaxKind.MinusEqualsToken))
                        yield return new Mutation(ae, ae.WithOperatorToken(SyntaxFactory.Token(SyntaxKind.PlusEqualsToken)),
                            relFile, line, "Assignment", "replace '-=' with '+='");
                    break;
                }
            }
        }
    }

    private BuildResult BuildProject(string projPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"build \"{projPath}\" -c Release --nologo",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = _repoRoot,
        };
        var proc = Process.Start(psi)!;
        var output = proc.StandardOutput.ReadToEnd() + proc.StandardError.ReadToEnd();
        proc.WaitForExit((int)TimeSpan.FromMinutes(3).TotalMilliseconds);
        return new BuildResult(proc.ExitCode == 0, output);
    }

    private TestResult RunTests()
    {
        var args = _testBuilt ? $"test \"{_testProj}\" -c Release --nologo --no-build -v q" : $"test \"{_testProj}\" -c Release --nologo -v q";
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = _repoRoot,
        };
        var proc = Process.Start(psi)!;
        var output = proc.StandardOutput.ReadToEnd() + proc.StandardError.ReadToEnd();
        proc.WaitForExit((int)TimeSpan.FromMinutes(5).TotalMilliseconds);
        return new TestResult(proc.ExitCode != 0, output);
    }
}

internal sealed record Mutation(SyntaxNode Node, SyntaxNode Replacement, string File, int Line, string Kind, string Description);

internal sealed record BuildResult(bool Success, string Output);

internal sealed record TestResult(bool Failed, string Output);
