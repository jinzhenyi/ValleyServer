#pragma warning disable SYSLIB0050

using System;
using System.Reflection;
using StardewValley;

namespace HeadlessServer
{
    /// <summary>Immutable snapshot of the world weather.</summary>
    public readonly record struct WeatherSnapshot(
        bool IsRaining,
        bool IsSnowing,
        bool IsLightning,
        bool IsDebrisWeather,
        int WeatherForTomorrow,
        int WeatherIcon)
    {
        public override string ToString() =>
            $"rain={IsRaining} snow={IsSnowing} storm={IsLightning} debris={IsDebrisWeather} tomorrow={WeatherForTomorrow} icon={WeatherIcon}";
    }

    /// <summary>
    /// Reads the weather that the vanilla overnight path produced and keeps the networked
    /// world state in sync. The server never re-implements the weather probability tables:
    /// <c>NetWorldState.UpdateWeatherForNewDay</c> remains the single source of truth and is
    /// only invoked explicitly when the headless overnight path skipped it.
    /// </summary>
    public interface IWeatherService
    {
        /// <summary>Current weather, read from <c>Game1.netWorldState</c>.</summary>
        WeatherSnapshot Current { get; }

        /// <summary>Rolls weather for a new day using the vanilla method. Returns false on failure.</summary>
        bool RollForNewDay();

        /// <summary>Pushes the current weather into the networked world state for broadcast.</summary>
        void SyncToNetWorldState();
    }

    internal sealed class WeatherService : IWeatherService
    {
        public WeatherSnapshot Current => ReadSnapshot();

        public bool RollForNewDay()
        {
            object? worldState = GetWorldState();
            if (worldState == null)
            {
                Console.WriteLine("[Weather] netWorldState unavailable; skipping weather roll.");
                return false;
            }

            try
            {
                MethodInfo? update = worldState.GetType().GetMethod(
                    "UpdateWeatherForNewDay", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (update == null)
                {
                    Console.WriteLine("[Weather] NetWorldState.UpdateWeatherForNewDay not found; keeping current weather.");
                    return false;
                }

                update.Invoke(worldState, update.GetParameters().Length == 0 ? null : new object?[] { null });
                Console.WriteLine($"[Weather] Rolled new-day weather: {Current}.");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Weather] Weather roll failed: {ex.Message}");
                return false;
            }
        }

        public void SyncToNetWorldState()
        {
            try
            {
                Game1.netWorldState?.Value?.UpdateFromGame1();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Weather] Failed to push weather to netWorldState: {ex.Message}");
            }
        }

        internal static WeatherSnapshot ReadSnapshot()
        {
            object? worldState = GetWorldState();
            if (worldState == null)
            {
                return default;
            }

            return new WeatherSnapshot(
                ReadBoolMember(worldState, "isRaining"),
                ReadBoolMember(worldState, "isSnowing"),
                ReadBoolMember(worldState, "isLightning"),
                ReadBoolMember(worldState, "isDebrisWeather"),
                ReadIntMember(worldState, "weatherForTomorrow"),
                ReadIntMember(worldState, "weatherIcon"));
        }

        private static object? GetWorldState()
        {
            try
            {
                return Game1.netWorldState?.Value;
            }
            catch
            {
                return null;
            }
        }

        private static object? ReadMember(object target, string name)
        {
            Type type = target.GetType();
            FieldInfo? field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                return field.GetValue(target);
            }
            PropertyInfo? property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return property?.GetValue(target);
        }

        private static bool ReadBoolMember(object target, string name)
        {
            object? value = ReadMember(target, name);
            if (value == null)
            {
                return false;
            }
            object? inner = value.GetType().GetProperty("Value")?.GetValue(value);
            return inner is bool b ? b : false;
        }

        private static int ReadIntMember(object target, string name)
        {
            object? value = ReadMember(target, name);
            if (value == null)
            {
                return 0;
            }
            object? inner = value.GetType().GetProperty("Value")?.GetValue(value);
            if (inner is int i)
            {
                return i;
            }
            return value is int direct ? direct : 0;
        }
    }
}
