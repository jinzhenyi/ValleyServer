#pragma warning disable SYSLIB0050

using System;

namespace HeadlessServer
{
    /// <summary>
    /// Console command surface of the dedicated server: the command table, the built-in
    /// commands, and the main-loop pump that executes queued command lines.
    /// </summary>
    partial class Program
    {
        private static readonly ServerCommandRegistry commandRegistry = new ServerCommandRegistry();

        /// <summary>
        /// Registers the built-in console commands. Called once during startup, before
        /// the message loop begins.
        /// </summary>
        private static void RegisterBuiltInCommands()
        {
            commandRegistry.Register(new ServerCommand(
                name: "help",
                usage: "help",
                description: "List the available console commands.",
                handler: _ => commandRegistry.PrintHelp()));

            commandRegistry.Register(new ServerCommand(
                name: "stop",
                usage: "stop",
                description: "Save every farmhand, disconnect clients and exit.",
                handler: _ => RequestGracefulShutdown("the stop command"),
                aliases: new[] { "shutdown", "quit" }));

            RegisterSeasonCommands();
        }

        /// <summary>
        /// Registers the calendar/season operator commands. All handlers run on the main
        /// loop thread and therefore may touch game and world-save state directly.
        /// </summary>
        private static void RegisterSeasonCommands()
        {
            commandRegistry.Register(new ServerCommand(
                name: "time",
                usage: "time",
                description: "Show the current in-game date and weather.",
                handler: _ => PrintCalendar()));

            commandRegistry.Register(new ServerCommand(
                name: "setday",
                usage: "setday <1-28>",
                description: "Set the current day of the month.",
                handler: args =>
                {
                    if (calendarService == null || args.Length != 1
                        || !int.TryParse(args[0], out int day) || day < 1 || day > 28)
                    {
                        Console.WriteLine("[Commands] Usage: setday <1-28>");
                        return;
                    }
                    CalendarSnapshot current = calendarService.Current;
                    if (calendarService.SetDate(current.Year, current.SeasonIndex, day))
                    {
                        worldSaveManager?.Save();
                    }
                }));

            commandRegistry.Register(new ServerCommand(
                name: "setseason",
                usage: "setseason <spring|summer|fall|winter>",
                description: "Switch to another season and refresh the world.",
                handler: args =>
                {
                    int season = ParseSeason(args);
                    if (calendarService == null || season < 0)
                    {
                        Console.WriteLine("[Commands] Usage: setseason <spring|summer|fall|winter>");
                        return;
                    }
                    CalendarSnapshot current = calendarService.Current;
                    if (calendarService.SetDate(current.Year, season, current.DayOfMonth))
                    {
                        worldSaveManager?.Save();
                    }
                }));

            commandRegistry.Register(new ServerCommand(
                name: "advance",
                usage: "advance [days]",
                description: "Advance the calendar by N days (default 1); for debugging.",
                handler: args =>
                {
                    if (calendarService == null)
                    {
                        return;
                    }
                    int days = 1;
                    if (args.Length > 1 || (args.Length == 1 && !int.TryParse(args[0], out days)))
                    {
                        Console.WriteLine("[Commands] Usage: advance [days]");
                        return;
                    }
                    if (days <= 0)
                    {
                        Console.WriteLine("[Commands] days must be a positive integer.");
                        return;
                    }
                    calendarService.AdvanceDays(days);
                    worldSaveManager?.Save();
                }));
        }

        private static int ParseSeason(string[] args)
        {
            if (args.Length != 1)
            {
                return -1;
            }
            return args[0].ToLowerInvariant() switch
            {
                "spring" => 0,
                "summer" => 1,
                "fall" => 2,
                "autumn" => 2,
                "winter" => 3,
                _ => -1
            };
        }

        private static void PrintCalendar()
        {
            if (calendarService == null)
            {
                Console.WriteLine("[Commands] Season services are not initialized yet.");
                return;
            }
            Console.WriteLine($"[Commands] Date: {calendarService.Current}");
            if (weatherService != null)
            {
                Console.WriteLine($"[Commands] Weather: {weatherService.Current}");
            }
        }

        /// <summary>
        /// Executes the commands typed on the console since the last pass. Invoked from
        /// the main loop so handlers observe a consistent game and network state.
        /// </summary>
        private static void PumpConsoleCommands()
        {
            ConsoleCommandReader.Drain(line =>
            {
                string[] tokens = ServerCommandRegistry.Tokenize(line);
                if (tokens.Length == 0)
                {
                    return;
                }

                ServerCommand? command = commandRegistry.Find(tokens[0]);
                if (command == null)
                {
                    Console.WriteLine($"[Commands] Unknown command '{tokens[0]}'. Type 'help' for the list.");
                    return;
                }

                string[] arguments = tokens.Length > 1 ? tokens[1..] : Array.Empty<string>();
                try
                {
                    command.Handler(arguments);
                }
                catch (Exception ex)
                {
                    // A misbehaving command must not take the server down with it.
                    Console.WriteLine($"[Commands] '{command.Name}' failed: {ex}");
                }
            });
        }
    }
}
