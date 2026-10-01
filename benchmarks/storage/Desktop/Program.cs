var root = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "maplebench");
var sizes = args.Length > 1 ? args[1].Split(',').Select(int.Parse).ToArray() : [1000, 10000, 50000];
if (args.Length < 3 || args[2] != "ef") MapleBench.Bench.Run(root, sizes, Console.WriteLine);
foreach (var n in sizes) MapleBench.EfBench.Run(Path.Combine(root, $"sqlite-sqlcipher-{n}", "maple.db"), n, Console.WriteLine);
