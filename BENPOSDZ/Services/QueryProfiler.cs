using System.Diagnostics;

namespace BENPOSDZ.Services
{
    // أداة قياس زمن الاستعلامات: تسجل في سجل النظام أي استعلام يتجاوز العتبة
    // (مفيدة لفحص أداء وضع LAN على MySQL)
    public static class QueryProfiler
    {
        private static long _slowCount;

        // عدد الاستعلامات البطيئة المتراكمة في هذه الجلسة
        public static long SlowQueryCount => Interlocked.Read(ref _slowCount);

        public static async Task<T> TimeAsync<T>(DatabaseService db, string operation, Func<Task<T>> query, long warnMs = 400)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                return await query();
            }
            finally
            {
                sw.Stop();
                if (sw.ElapsedMilliseconds >= warnMs)
                {
                    Interlocked.Increment(ref _slowCount);
                    db.LogEvent($"⚠️ استعلام بطيء [{operation}]: {sw.ElapsedMilliseconds}ms");
                }
            }
        }

        public static async Task TimeAsync(DatabaseService db, string operation, Func<Task> query, long warnMs = 400)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                await query();
            }
            finally
            {
                sw.Stop();
                if (sw.ElapsedMilliseconds >= warnMs)
                {
                    Interlocked.Increment(ref _slowCount);
                    db.LogEvent($"⚠️ استعلام بطيء [{operation}]: {sw.ElapsedMilliseconds}ms");
                }
            }
        }
    }
}
