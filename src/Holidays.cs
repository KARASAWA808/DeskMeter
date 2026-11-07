using System;
using System.Collections.Generic;

namespace DeskMeter
{
    // 日本の祝日 (外部データなしで計算する)。振替休日・国民の休日を含む。
    static class Holidays
    {
        static readonly Dictionary<int, Dictionary<DateTime, string>> cache = new Dictionary<int, Dictionary<DateTime, string>>();

        public static bool IsHoliday(DateTime d)
        {
            return Name(d) != null;
        }

        // 祝日名。祝日でなければ null
        public static string Name(DateTime d)
        {
            string name;
            return Year(d.Year).TryGetValue(d.Date, out name) ? name : null;
        }

        // その年の祝日を日付順に返す
        public static List<KeyValuePair<DateTime, string>> Of(int year)
        {
            var list = new List<KeyValuePair<DateTime, string>>(Year(year));
            list.Sort(delegate(KeyValuePair<DateTime, string> a, KeyValuePair<DateTime, string> b) { return a.Key.CompareTo(b.Key); });
            return list;
        }

        static Dictionary<DateTime, string> Year(int y)
        {
            Dictionary<DateTime, string> m;
            lock (cache)
            {
                if (!cache.TryGetValue(y, out m))
                {
                    m = Compute(y);
                    cache[y] = m;
                }
            }
            return m;
        }

        // n 回目の指定曜日 (例: 1月の第2月曜)
        static DateTime NthWeekday(int y, int month, DayOfWeek dow, int n)
        {
            var first = new DateTime(y, month, 1);
            int offset = ((int)dow - (int)first.DayOfWeek + 7) % 7;
            return first.AddDays(offset + (n - 1) * 7);
        }

        static Dictionary<DateTime, string> Compute(int y)
        {
            var m = new Dictionary<DateTime, string>();
            Action<DateTime, string> add = delegate(DateTime d, string n) { if (!m.ContainsKey(d)) m[d] = n; };

            add(new DateTime(y, 1, 1), "元日");
            add(NthWeekday(y, 1, DayOfWeek.Monday, 2), "成人の日");
            add(new DateTime(y, 2, 11), "建国記念の日");
            add(new DateTime(y, 2, 23), "天皇誕生日");
            // 春分・秋分 (2000〜2099 年の近似式)
            int shunbun = (int)(20.8431 + 0.242194 * (y - 1980) - (int)((y - 1980) / 4));
            int shubun = (int)(23.2488 + 0.242194 * (y - 1980) - (int)((y - 1980) / 4));
            add(new DateTime(y, 3, shunbun), "春分の日");
            add(new DateTime(y, 4, 29), "昭和の日");
            add(new DateTime(y, 5, 3), "憲法記念日");
            add(new DateTime(y, 5, 4), "みどりの日");
            add(new DateTime(y, 5, 5), "こどもの日");
            add(NthWeekday(y, 7, DayOfWeek.Monday, 3), "海の日");
            add(new DateTime(y, 8, 11), "山の日");
            add(NthWeekday(y, 9, DayOfWeek.Monday, 3), "敬老の日");
            add(new DateTime(y, 9, shubun), "秋分の日");
            add(NthWeekday(y, 10, DayOfWeek.Monday, 2), "スポーツの日");
            add(new DateTime(y, 11, 3), "文化の日");
            add(new DateTime(y, 11, 23), "勤労感謝の日");

            // 振替休日: 祝日が日曜なら、その後の最初の「祝日でない日」
            var fixedDays = new List<DateTime>(m.Keys);
            foreach (DateTime d in fixedDays)
            {
                if (d.DayOfWeek != DayOfWeek.Sunday) continue;
                DateTime t = d.AddDays(1);
                while (m.ContainsKey(t)) t = t.AddDays(1);
                add(t, "振替休日");
            }
            // 国民の休日: 前日と翌日が祝日の平日 (日曜・振替休日は除く)
            var all = new List<DateTime>(m.Keys);
            foreach (DateTime d in all)
            {
                DateTime t = d.AddDays(2);
                DateTime mid = d.AddDays(1);
                if (m.ContainsKey(t) && !m.ContainsKey(mid) && mid.DayOfWeek != DayOfWeek.Sunday) add(mid, "国民の休日");
            }
            return m;
        }
    }
}
