#pragma warning disable SYSLIB0050

using System;
using System.Reflection;
using StardewValley;

namespace HeadlessServer
{
    /// <summary>
    /// Immutable snapshot of the in-game calendar. Mirrors <c>Game1.year</c>,
    /// <c>Game1.seasonIndex</c> and <c>Game1.dayOfMonth</c>.
    /// </summary>
    public readonly record struct CalendarSnapshot(int Year, int SeasonIndex, int DayOfMonth)
    {
        /// <summary>Number of days in a Stardew Valley season.</summary>
        public const int DaysPerSeason = 28;

        /// <summary>Number of seasons in a Stardew Valley year.</summary>
        public const int SeasonsPerYear = 4;

        public string SeasonName => SeasonIndex switch
        {
            0 => "Spring",
            1 => "Summer",
            2 => "Fall",
            3 => "Winter",
            _ => $"Unknown({SeasonIndex})"
        };

        public override string ToString() => $"Year {Year} {SeasonName} {DayOfMonth}";
    }

    /// <summary>
    /// Owns the headless calendar: validates the date the vanilla overnight path produced,
    /// advances it when that path did not, and raises a season-change signal for the
    /// seasonal world updater. The vanilla game already rolls the date for a normal
    /// dedicated host, so this service is mostly an observer plus a safety net.
    /// </summary>
    public interface ICalendarService
    {
        /// <summary>Current date, read from <see cref="Game1"/>.</summary>
        CalendarSnapshot Current { get; }

        /// <summary>Snapshot taken at the end of the previous reconcile (or initialization).</summary>
        CalendarSnapshot LastReconciled { get; }

        /// <summary>True when the most recent reconcile had to advance the date itself.</summary>
        bool LastReconcileWasManual { get; }

        /// <summary>Applies the configured starting date to a freshly created world.</summary>
        void InitializeFromConfig();

        /// <summary>
        /// Called after the overnight coroutine returns. Normalizes the date and advances it
        /// by one day when the vanilla path did not. Returns true when the season changed.
        /// </summary>
        bool ReconcileAfterOvernight(out CalendarSnapshot previous, out CalendarSnapshot current);

        /// <summary>Sets the absolute date; returns false when the arguments are out of range.</summary>
        bool SetDate(int year, int seasonIndex, int dayOfMonth);

        /// <summary>Advances the calendar in place by <paramref name="days"/> days (debug/manual).</summary>
        void AdvanceDays(int days);

        /// <summary>Raised after a reconcile/set when the season index changed.</summary>
        event Action<CalendarSnapshot, CalendarSnapshot>? SeasonChanged;
    }

    internal sealed class CalendarService : ICalendarService
    {
        private CalendarSnapshot lastReconciled;

        public CalendarService()
        {
            lastReconciled = ReadGameCalendar();
        }

        public CalendarSnapshot Current => ReadGameCalendar();

        public CalendarSnapshot LastReconciled => lastReconciled;

        public bool LastReconcileWasManual { get; private set; }

        public event Action<CalendarSnapshot, CalendarSnapshot>? SeasonChanged;

        public static CalendarSnapshot ReadGameCalendar()
        {
            int year = Game1.year;
            int day = Game1.dayOfMonth;
            int season = (int)Game1.season;
            return new CalendarSnapshot(year < 1 ? 1 : year, NormalizeSeason(season), NormalizeDay(day));
        }

        public void InitializeFromConfig()
        {
            ServerConfig.WorldSection world = ServerConfig.Current.World;
            int year = world.StartingYear < 1 ? 1 : world.StartingYear;
            int season = NormalizeSeason(world.StartingSeason);
            int day = NormalizeDay(world.StartingDayOfMonth);

            var target = new CalendarSnapshot(year, season, day);
            WriteGameCalendar(target);
            lastReconciled = target;
            Console.WriteLine($"[Calendar] Initialized from config: {target}.");
        }

        public bool ReconcileAfterOvernight(out CalendarSnapshot previous, out CalendarSnapshot current)
        {
            previous = lastReconciled;
            current = ReadGameCalendar();

            // The vanilla overnight path advances the date; if it did not (headless branch
            // skipped it), advance exactly one day so the one-day-per-roll invariant holds.
            if (current == previous)
            {
                current = Advance(current, 1);
                WriteGameCalendar(current);
                LastReconcileWasManual = true;
                Console.WriteLine($"[Calendar] Overnight did not advance the date; advanced manually to {current}.");
            }
            else
            {
                LastReconcileWasManual = false;
                Console.WriteLine($"[Calendar] Overnight advanced the date: {previous} -> {current}.");
            }

            lastReconciled = current;
            bool seasonChanged = previous.SeasonIndex != current.SeasonIndex;
            if (seasonChanged)
            {
                Console.WriteLine($"[Calendar] Season changed: {previous.SeasonName} -> {current.SeasonName}.");
                SeasonChanged?.Invoke(previous, current);
            }
            return seasonChanged;
        }

        public bool SetDate(int year, int seasonIndex, int dayOfMonth)
        {
            if (year < 1 || seasonIndex < 0 || seasonIndex >= CalendarSnapshot.SeasonsPerYear
                || dayOfMonth < 1 || dayOfMonth > CalendarSnapshot.DaysPerSeason)
            {
                return false;
            }

            CalendarSnapshot previous = lastReconciled;
            var target = new CalendarSnapshot(year, seasonIndex, dayOfMonth);
            WriteGameCalendar(target);
            lastReconciled = target;
            Console.WriteLine($"[Calendar] Date set: {previous} -> {target}.");

            if (previous.SeasonIndex != target.SeasonIndex)
            {
                SeasonChanged?.Invoke(previous, target);
            }
            return true;
        }

        public void AdvanceDays(int days)
        {
            if (days <= 0)
            {
                return;
            }

            CalendarSnapshot previous = lastReconciled;
            CalendarSnapshot current = previous;
            for (int i = 0; i < days; i++)
            {
                current = Advance(current, 1);
            }
            WriteGameCalendar(current);
            lastReconciled = current;
            Console.WriteLine($"[Calendar] Advanced {days} day(s): {previous} -> {current}.");

            if (previous.SeasonIndex != current.SeasonIndex)
            {
                SeasonChanged?.Invoke(previous, current);
            }
        }

        /// <summary>
        /// Pure calendar arithmetic: adds <paramref name="days"/> days, rolling days &gt; 28
        /// into the next season and rolling past Winter into the next year.
        /// </summary>
        internal static CalendarSnapshot Advance(CalendarSnapshot snapshot, int days)
        {
            int index = (snapshot.Year - 1) * CalendarSnapshot.SeasonsPerYear + snapshot.SeasonIndex;
            int day = snapshot.DayOfMonth - 1 + days;

            index += day / CalendarSnapshot.DaysPerSeason;
            day %= CalendarSnapshot.DaysPerSeason;
            if (day < 0)
            {
                day += CalendarSnapshot.DaysPerSeason;
                index -= 1;
            }

            int year = index / CalendarSnapshot.SeasonsPerYear;
            int season = index % CalendarSnapshot.SeasonsPerYear;
            if (season < 0)
            {
                season += CalendarSnapshot.SeasonsPerYear;
                year -= 1;
            }

            return new CalendarSnapshot(year + 1, season, day + 1);
        }

        private static void WriteGameCalendar(CalendarSnapshot snapshot)
        {
            Game1.year = snapshot.Year;
            Game1.season = (Season)snapshot.SeasonIndex;
            Game1.dayOfMonth = snapshot.DayOfMonth;
            TryWriteSeasonIndex(snapshot.SeasonIndex);
        }

        private static void TryWriteSeasonIndex(int seasonIndex)
        {
            // Game1.seasonIndex may be a read-only computed property; keep it in sync through
            // reflection only when it is actually writable.
            try
            {
                var property = typeof(Game1).GetProperty("seasonIndex", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (property != null && property.CanWrite)
                {
                    property.SetValue(null, seasonIndex);
                    return;
                }
                var field = typeof(Game1).GetField("seasonIndex", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null && !field.IsInitOnly)
                {
                    field.SetValue(null, seasonIndex);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Calendar] Could not write seasonIndex via reflection: {ex.Message}");
            }
        }

        private static int NormalizeSeason(int season)
        {
            if (season < 0 || season >= CalendarSnapshot.SeasonsPerYear)
            {
                return 0;
            }
            return season;
        }

        private static int NormalizeDay(int day)
        {
            if (day < 1)
            {
                return 1;
            }
            if (day > CalendarSnapshot.DaysPerSeason)
            {
                return CalendarSnapshot.DaysPerSeason;
            }
            return day;
        }
    }
}
