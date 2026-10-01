using Android.Widget;

namespace Droid;

[Activity(Label = "MapleBench", MainLauncher = true)]
public class MainActivity : Activity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        var text = new TextView(this) { Text = "Running…" };
        SetContentView(text);
        var root = Path.Combine(FilesDir!.AbsolutePath, "bench");
        var sizes = (Intent?.GetStringExtra("sizes") ?? "1000,10000,50000").Split(',').Select(int.Parse).ToArray();
        new Thread(() =>
        {
            try
            {
                MapleBench.Bench.Run(root, sizes, line => Android.Util.Log.Info("MAPLEBENCH", line));
                foreach (var n in sizes) MapleBench.EfBench.Run(Path.Combine(root, $"sqlite-sqlcipher-{n}", "maple.db"), n, line => Android.Util.Log.Info("MAPLEBENCH", line));
                Android.Util.Log.Info("MAPLEBENCH", "MAPLEBENCH|finished");
            }
            catch (Exception ex)
            {
                Android.Util.Log.Error("MAPLEBENCH", "MAPLEBENCH|error|" + ex);
            }
            RunOnUiThread(() => text.Text = "Done");
        }, 64 * 1024 * 1024).Start();
    }
}
