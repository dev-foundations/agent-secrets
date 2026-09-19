// A tiny child process used by the tests. It reports facts ABOUT its environment and
// arguments but never prints an environment variable's value.
//
//   test-probe env-check NAME...          "NAME available: yes|no"; exit 0 only if all are set
//   test-probe env-matches NAME SHA256    exit 0 if SHA-256(value) matches, else 1; prints nothing secret
//   test-probe args ...                   prints each argument as a JSON string, one per line
//   test-probe exit N                     exits with code N
//   test-probe stdin-upper                copies stdin to stdout in upper case

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

if (args.Length == 0)
{
    return 64;
}

switch (args[0])
{
    case "env-check":
        bool all = true;
        foreach (string name in args[1..])
        {
            bool available = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name));
            all &= available;
            Console.WriteLine($"{name} available: {(available ? "yes" : "no")}");
        }

        return all ? 0 : 1;

    case "env-matches":
        string actual = Environment.GetEnvironmentVariable(args[1]) ?? "";
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(actual)));
        bool matches = hash.Equals(args[2], StringComparison.OrdinalIgnoreCase);
        Console.WriteLine($"{args[1]} matches expected hash: {(matches ? "yes" : "no")}");
        return matches ? 0 : 1;

    case "args":
        foreach (string arg in args[1..])
        {
            Console.WriteLine(JsonSerializer.Serialize(arg));
        }

        return 0;

    case "exit":
        return int.Parse(args[1]);

    case "stdin-upper":
        Console.Write(Console.In.ReadToEnd().ToUpperInvariant());
        return 0;

    default:
        return 64;
}
