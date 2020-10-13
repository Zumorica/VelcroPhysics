using System.Collections.Generic;

namespace VelcroPhysics.Shared
{
    public static class Benchmark
    {
        private static readonly Dictionary<string, long> _results = new Dictionary<string, long>();
        private static readonly Pool<BenchmarkRun> _runPool = new Pool<BenchmarkRun>(() => new BenchmarkRun());

        public static BenchmarkRun StartRun(string area)
        {
            BenchmarkRun run = _runPool.GetFromPool();
            run.SetData(area);
            return run;
        }

        public static void RecordResults(string area, long results)
        {
#pragma warning disable 162
            // ReSharper disable once ConditionIsAlwaysTrueOrFalse
            if (!Settings.EnableDiagnostics)
                return;

            if (!_results.ContainsKey(area))
                _results.Add(area, results);
            else
                _results[area] += results;
        }

        public static long ResetResults(string area)
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalse
            if (!Settings.EnableDiagnostics)
                return 0;

            long oldVal = _results[area];
            _results[area] = 0;
            return oldVal;
        }

        public static long GetResults(string area)
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalse
            if (!Settings.EnableDiagnostics)
                return 0;
#pragma warning restore 162

            return _results.TryGetValue(area, out long value) ? value : 0;
        }
    }
}
