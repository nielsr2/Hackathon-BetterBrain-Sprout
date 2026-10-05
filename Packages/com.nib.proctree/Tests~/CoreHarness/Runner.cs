using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace Nib.ProcTree.Harness
{
    /// <summary>Minimal reflection test runner: [Test], [TestCase], [SetUp]. Exit code = failures.
    /// Args: optional name filter (substring); "--render" runs the silhouette renderer instead.</summary>
    static class Runner
    {
        static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--render") return Render.Run(args.Skip(1).ToArray());

            string filter = args.FirstOrDefault();
            int passed = 0, failed = 0;
            var types = typeof(Runner).Assembly.GetTypes()
                .Where(t => t.GetCustomAttribute<TestFixtureAttribute>() != null
                         || t.GetMethods().Any(m => m.GetCustomAttribute<TestAttribute>() != null
                                                  || m.GetCustomAttributes<TestCaseAttribute>().Any()))
                .OrderBy(t => t.FullName);

            foreach (var type in types)
            {
                var setUp = type.GetMethods().FirstOrDefault(m => m.GetCustomAttribute<SetUpAttribute>() != null);
                foreach (var m in type.GetMethods().OrderBy(m => m.MetadataToken))
                {
                    var cases = m.GetCustomAttributes<TestCaseAttribute>().Select(c => c.Arguments).ToList();
                    if (m.GetCustomAttribute<TestAttribute>() != null && cases.Count == 0) cases.Add(Array.Empty<object>());
                    foreach (var argsForCase in cases)
                    {
                        string name = $"{type.Name}.{m.Name}" + (argsForCase.Length > 0 ? $"({string.Join(",", argsForCase)})" : "");
                        if (filter != null && !name.Contains(filter)) continue;
                        var sw = Stopwatch.StartNew();
                        try
                        {
                            object inst = m.IsStatic ? null : Activator.CreateInstance(type);
                            setUp?.Invoke(inst, null);
                            m.Invoke(inst, argsForCase);
                            passed++;
                            Console.WriteLine($"  PASS {name} ({sw.ElapsedMilliseconds} ms)");
                        }
                        catch (TargetInvocationException e) when (e.InnerException is SuccessException)
                        {
                            passed++;
                            Console.WriteLine($"  PASS {name}");
                        }
                        catch (TargetInvocationException e)
                        {
                            failed++;
                            var inner = e.InnerException ?? e;
                            Console.WriteLine($"  FAIL {name}\n       {inner.GetType().Name}: {inner.Message.Trim().Replace("\n", "\n       ")}");
                            if (!(inner is AssertionException)) Console.WriteLine(inner.StackTrace);
                        }
                    }
                }
            }
            Console.WriteLine($"\n{passed} passed, {failed} failed");
            return failed;
        }
    }
}
