using Xunit.Sdk;
using Xunit.v3;

// Every test opens its own encrypted database, but Microsoft.Data.Sqlite keeps one set of connection pools for the
// whole process. TestApp, and the app's own erase, key-lost and upgrade steps, empty all of them to let go of their
// files (SqliteConnection.ClearAllPools), and that can close a connection another test is opening at that moment.
// Run side by side, a test then failed at random with "Cannot access a disposed object: SQLitePCL.sqlite3".
[assembly: Parallelization(Mode = ParallelMode.None)]
