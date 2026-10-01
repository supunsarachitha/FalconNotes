# Storage benchmark

Measures encrypted SQLite (SQLite3MC: SQLCipher format, ChaCha20, AEGIS), plain SQLite, a folder of `.md` files, and EF
Core's start-up cost, with the queries the app's screens make. The results and the decision they led to are in
[docs/13-storage-benchmark.md](../../docs/13-storage-benchmark.md).

| Path | What |
|---|---|
| `Shared/Bench.cs` | Data generator and every measurement |
| `Shared/EfBench.cs` | EF Core's first-query cost |
| `Desktop/` | Console runner (macOS, Windows, Linux) |
| `Droid/` | Android runner: logs `MAPLEBENCH|…` lines to logcat |
| `table.py` | Turns the result lines into Markdown tables |
| `results/` | Raw results: Mac M1 Pro, the Android emulator, and the first run with the server's schema (`-schema-v1`) |

```sh
# Desktop
cd Desktop && dotnet run -c Release -- /tmp/maplebench 1000,10000,50000 > ../results/<machine>.txt
python3 ../table.py ../results/<machine>.txt

# Android (emulator or phone over adb)
cd Droid && dotnet build -c Release
adb install -r bin/Release/net10.0-android/android-arm64/dev.maplenotes.bench-Signed.apk
adb logcat -c
adb shell am start -n "dev.maplenotes.bench/$(adb shell cmd package resolve-activity --brief dev.maplenotes.bench | tail -1 | cut -d/ -f2)"
# wait for "MAPLEBENCH|finished" (several minutes on one core), then:
adb logcat -d -s 'MAPLEBENCH:*' | grep -o 'MAPLEBENCH|.*' > ../results/<device>.txt
python3 ../table.py ../results/<device>.txt
adb uninstall dev.maplenotes.bench
```

The Android runner needs about 1 GB of free storage for 50,000 notes in every variant. To run smaller sizes, pass
`--es sizes 1000,10000` to `am start`.
