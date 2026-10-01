#pragma warning disable SYSLIB0050

using System;
using System.Reflection;
using StardewValley;

namespace HeadlessServer
{
    /// <summary>
    /// Refreshes season-dependent map data (tile sheets and season-specific map variants)
    /// for every loaded location. Driven by <see cref="ICalendarService.SeasonChanged"/>,
    /// which covers both overnight rolls and operator commands.
    /// </summary>
    public interface ISeasonalWorldUpdater
    {
        void ApplySeason(Season season);
        void ApplySeasonForLocation(GameLocation location, Season season);
    }

    /// <summary>
    /// Runs the per-location day update (crop growth, regrowth, out-of-season handling)
    /// when the headless overnight path skipped it, so the vanilla <c>Crop</c>/<c>HoeDirt</c>
    /// rules remain the single source of truth.
    /// </summary>
    public interface ICropDayUpdater
    {
        void RunDayUpdate(int dayOfMonth);
    }

    internal sealed class SeasonalWorldUpdater : ISeasonalWorldUpdater, ICropDayUpdater
    {
        private static readonly object logLock = new object();
        private static bool tileSheetFailureLogged;

        public void ApplySeason(Season season)
        {
            Console.WriteLine($"[Season] Applying season {season} to {Game1.locations.Count} location(s).");
            foreach (GameLocation? location in Game1.locations)
            {
                if (location != null)
                {
                    ApplySeasonForLocation(location, season);
                }
            }
        }

        public void ApplySeasonForLocation(GameLocation location, Season season)
        {
            try
            {
                InvokeBestEffort(location, "seasonUpdate", season);
                InvokeBestEffort(location, "updateSeasonalTileSheets", season);
            }
            catch (Exception ex)
            {
                LogTileSheetFailure($"{location.NameOrUniqueName}: {ex.Message}");
            }
        }

        public void RunDayUpdate(int dayOfMonth)
        {
            foreach (GameLocation? location in Game1.locations)
            {
                if (location == null)
                {
                    continue;
                }
                try
                {
                    InvokeBestEffort(location, "DayUpdate", dayOfMonth);
                    InvokeBestEffort(location, "dayUpdate", dayOfMonth);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Season] DayUpdate failed for {location.NameOrUniqueName}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Invokes a vanilla method whose exact signature varies between game versions. The
        /// parameter list is filled with the current season, the day-of-month or the headless
        /// display device, whichever the target method accepts.
        /// </summary>
        private static void InvokeBestEffort(object target, string methodName, object context)
        {
            MethodInfo? method = target.GetType().GetMethod(
                methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (method == null)
            {
                return;
            }

            ParameterInfo[] parameters = method.GetParameters();
            object?[] args = new object?[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                Type parameterType = parameters[i].ParameterType;
                if (parameterType == typeof(Season) || parameterType == typeof(Season?))
                {
                    args[i] = context is Season season ? season : Game1.season;
                }
                else if (parameterType == typeof(int))
                {
                    args[i] = context is int day ? day : Game1.dayOfMonth;
                }
                else if (parameterType == typeof(bool))
                {
                    args[i] = false;
                }
                else
                {
                    args[i] = MatchDisplayDevice(parameterType);
                }
            }

            method.Invoke(target, args);
        }

        private static object? MatchDisplayDevice(Type parameterType)
        {
            object? device = Game1.mapDisplayDevice;
            if (device != null && parameterType.IsInstanceOfType(device))
            {
                return device;
            }
            return parameterType.IsValueType ? Activator.CreateInstance(parameterType) : null;
        }

        private static void LogTileSheetFailure(string message)
        {
            lock (logLock)
            {
                if (tileSheetFailureLogged)
                {
                    return;
                }
                tileSheetFailureLogged = true;
            }
            Console.WriteLine($"[Season] Seasonal tile update failed (further failures suppressed): {message}");
        }
    }
}
