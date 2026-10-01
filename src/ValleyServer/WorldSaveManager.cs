#pragma warning disable SYSLIB0050

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using StardewValley;
using StardewValley.SaveSerialization;

namespace HeadlessServer
{
    /// <summary>Metadata written to <c>world.json</c>.</summary>
    public sealed class WorldSaveMetadata
    {
        public int Version { get; set; } = CurrentVersion;
        public int Year { get; set; } = 1;
        public int SeasonIndex { get; set; }
        public int DayOfMonth { get; set; } = 1;
        public bool IsRaining { get; set; }
        public bool IsSnowing { get; set; }
        public bool IsLightning { get; set; }
        public bool IsDebrisWeather { get; set; }
        public int WeatherForTomorrow { get; set; }
        public int WeatherIcon { get; set; }
        public List<string> Locations { get; set; } = new List<string>();

        public const int CurrentVersion = 1;
    }

    /// <summary>
    /// Persists the calendar, weather and farm world so a restart keeps the same date and
    /// the same crops/terrain. Location state reuses the vanilla <see cref="SaveSerializer"/>,
    /// matching the farmhand save approach already used by the server.
    /// </summary>
    public interface IWorldSaveManager
    {
        string SavePath { get; }
        /// <summary>Full save: calendar, weather and every location's state.</summary>
        void Save();
        /// <summary>Cheap save: calendar and weather only. Safe to call during a day roll.</summary>
        void SaveMetadata();
        bool TryLoad();
    }

    internal sealed class WorldSaveManager : IWorldSaveManager
    {
        private const string MetadataFileName = "world.json";

        private readonly ICalendarService calendar;
        private readonly IWeatherService weather;

        public WorldSaveManager(ICalendarService calendar, IWeatherService weather)
        {
            this.calendar = calendar;
            this.weather = weather;
        }

        public string SavePath
        {
            get
            {
                string configured = ServerConfig.Current.Paths.SaveDirectory;
                string root = Path.IsPathRooted(configured)
                    ? configured
                    : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, configured);
                return Path.Combine(root, "world");
            }
        }

        private string LocationsPath => Path.Combine(SavePath, "locations");
        private string MetadataPath => Path.Combine(SavePath, MetadataFileName);

        public void Save()
        {
            if (!ServerConfig.Current.World.PersistWorld)
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(LocationsPath);

                CalendarSnapshot snapshot = calendar.Current;
                WeatherSnapshot weatherSnapshot = weather.Current;
                var written = new List<string>();

                foreach (GameLocation? location in Game1.locations)
                {
                    if (location == null)
                    {
                        continue;
                    }
                    string name = location.NameOrUniqueName;
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }
                    if (TrySaveLocation(location, name))
                    {
                        written.Add(name);
                    }
                }

                WorldSaveMetadata metadata = BuildMetadata(written);
                WriteMetadataAtomically(metadata);
                Console.WriteLine($"[WorldSave] Saved {calendar.Current} with {written.Count} location(s) to {SavePath}.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WorldSave] Save failed: {ex}");
            }
        }

        /// <summary>
        /// Writes only <c>world.json</c> (calendar + weather + the location name list). This is
        /// deliberately cheap so it can run on the main thread right after a day roll without
        /// blocking the overnight network sync. The per-location XML files are written by
        /// <see cref="Save"/> on clean shutdown.
        /// </summary>
        public void SaveMetadata()
        {
            if (!ServerConfig.Current.World.PersistWorld)
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(SavePath);
                var names = new List<string>();
                foreach (GameLocation? location in Game1.locations)
                {
                    if (location != null && !string.IsNullOrWhiteSpace(location.NameOrUniqueName))
                    {
                        names.Add(location.NameOrUniqueName);
                    }
                }
                WriteMetadataAtomically(BuildMetadata(names));
                Console.WriteLine($"[WorldSave] Saved calendar metadata: {calendar.Current}.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WorldSave] Metadata save failed: {ex}");
            }
        }

        private WorldSaveMetadata BuildMetadata(List<string> locations)
        {
            CalendarSnapshot snapshot = calendar.Current;
            WeatherSnapshot weatherSnapshot = weather.Current;
            return new WorldSaveMetadata
            {
                Version = WorldSaveMetadata.CurrentVersion,
                Year = snapshot.Year,
                SeasonIndex = snapshot.SeasonIndex,
                DayOfMonth = snapshot.DayOfMonth,
                IsRaining = weatherSnapshot.IsRaining,
                IsSnowing = weatherSnapshot.IsSnowing,
                IsLightning = weatherSnapshot.IsLightning,
                IsDebrisWeather = weatherSnapshot.IsDebrisWeather,
                WeatherForTomorrow = weatherSnapshot.WeatherForTomorrow,
                WeatherIcon = weatherSnapshot.WeatherIcon,
                Locations = locations
            };
        }

        public bool TryLoad()
        {
            if (!ServerConfig.Current.World.PersistWorld)
            {
                Console.WriteLine("[WorldSave] PersistWorld is disabled; starting a fresh world.");
                return false;
            }
            if (!File.Exists(MetadataPath))
            {
                Console.WriteLine($"[WorldSave] No world save at {MetadataPath}; starting a fresh world.");
                return false;
            }

            WorldSaveMetadata? metadata;
            try
            {
                string json = File.ReadAllText(MetadataPath);
                metadata = JsonSerializer.Deserialize<WorldSaveMetadata>(json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WorldSave] Corrupt metadata ({ex.Message}); starting a fresh world.");
                return false;
            }

            if (metadata == null)
            {
                Console.WriteLine("[WorldSave] Metadata was empty; starting a fresh world.");
                return false;
            }

            TryApplyMetadata(metadata);
            int restored = 0;
            foreach (string name in metadata.Locations)
            {
                if (TryLoadLocation(name))
                {
                    restored++;
                }
            }

            try
            {
                Game1.flushLocationLookup();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WorldSave] flushLocationLookup failed: {ex.Message}");
            }
            weather.SyncToNetWorldState();
            Console.WriteLine($"[WorldSave] Restored {restored}/{metadata.Locations.Count} location(s) from {SavePath}.");
            return true;
        }

        private void TryApplyMetadata(WorldSaveMetadata metadata)
        {
            try
            {
                if (!calendar.SetDate(metadata.Year, metadata.SeasonIndex, metadata.DayOfMonth))
                {
                    Console.WriteLine($"[WorldSave] Metadata date was out of range; keeping current calendar.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WorldSave] Failed to apply date: {ex.Message}");
            }

            TryWriteWeather(metadata);
        }

        private static bool TrySaveLocation(GameLocation location, string name)
        {
            string path = Path.Combine(GetLocationsPathStatic(), Sanitize(name) + ".xml");
            string temp = path + ".tmp";
            try
            {
                var serializer = SaveSerializer.GetSerializer(typeof(GameLocation));
                using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    serializer.Serialize(stream, location);
                    stream.Flush(true);
                }
                File.Move(temp, path, true);
                return true;
            }
            catch (Exception ex)
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                Console.WriteLine($"[WorldSave] Could not serialize location '{name}': {ex.Message}");
                return false;
            }
        }

        private bool TryLoadLocation(string name)
        {
            string path = Path.Combine(LocationsPath, Sanitize(name) + ".xml");
            if (!File.Exists(path))
            {
                return false;
            }

            try
            {
                var serializer = SaveSerializer.GetSerializer(typeof(GameLocation));
                GameLocation? loaded;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read))
                {
                    loaded = serializer.Deserialize(stream) as GameLocation;
                }
                if (loaded == null)
                {
                    return false;
                }

                int index = IndexOfLocation(name);
                if (index >= 0)
                {
                    Game1.locations[index] = loaded;
                }
                else
                {
                    Game1.locations.Add(loaded);
                }
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WorldSave] Could not load location '{name}': {ex.Message}");
                return false;
            }
        }

        private static int IndexOfLocation(string name)
        {
            IList<GameLocation> locations = Game1.locations;
            for (int i = 0; i < locations.Count; i++)
            {
                if (locations[i] != null && locations[i].NameOrUniqueName == name)
                {
                    return i;
                }
            }
            return -1;
        }

        private static void WriteMetadataAtomically(WorldSaveMetadata metadata)
        {
            string path = GetMetadataPathStatic();
            string temp = path + ".tmp";
            string json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(temp, json);
            File.Move(temp, path, true);
        }

        private static void TryWriteWeather(WorldSaveMetadata metadata)
        {
            try
            {
                object? worldState = Game1.netWorldState?.Value;
                if (worldState == null)
                {
                    return;
                }
                WriteBoolMember(worldState, "isRaining", metadata.IsRaining);
                WriteBoolMember(worldState, "isSnowing", metadata.IsSnowing);
                WriteBoolMember(worldState, "isLightning", metadata.IsLightning);
                WriteBoolMember(worldState, "isDebrisWeather", metadata.IsDebrisWeather);
                WriteIntMember(worldState, "weatherForTomorrow", metadata.WeatherForTomorrow);
                WriteIntMember(worldState, "weatherIcon", metadata.WeatherIcon);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WorldSave] Failed to apply weather: {ex.Message}");
            }
        }

        private static void WriteBoolMember(object target, string name, bool value)
        {
            object? member = ReadMember(target, name);
            if (member == null)
            {
                return;
            }
            PropertyInfo? valueProperty = member.GetType().GetProperty("Value");
            if (valueProperty != null && valueProperty.CanWrite)
            {
                valueProperty.SetValue(member, value);
            }
        }

        private static void WriteIntMember(object target, string name, int value)
        {
            object? member = ReadMember(target, name);
            if (member == null)
            {
                return;
            }
            PropertyInfo? valueProperty = member.GetType().GetProperty("Value");
            if (valueProperty != null && valueProperty.CanWrite)
            {
                valueProperty.SetValue(member, value);
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

        private static string GetLocationsPathStatic()
        {
            string configured = ServerConfig.Current.Paths.SaveDirectory;
            string root = Path.IsPathRooted(configured)
                ? configured
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, configured);
            return Path.Combine(root, "world", "locations");
        }

        private static string GetMetadataPathStatic()
        {
            string configured = ServerConfig.Current.Paths.SaveDirectory;
            string root = Path.IsPathRooted(configured)
                ? configured
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, configured);
            return Path.Combine(root, "world", MetadataFileName);
        }

        private static string Sanitize(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name;
        }
    }
}
