using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace VRChatQuickJoin
{
    static class Utils
    {
        internal static bool IsVrchatRunning() => Process.GetProcessesByName("VRChat").Any();
        internal static Uri BuildJoinLink(string worldId, string instanceId)
        {
            return Program.gameUri.AddQuery("id", $"{worldId}:{instanceId}", encode: false);
        }
        internal static bool HandleJoin(string worldId, string instanceId, List<string> additionalArgs = null)
        {
            if (string.IsNullOrWhiteSpace(worldId) || string.IsNullOrWhiteSpace(instanceId))
            {
                Console.WriteLine("Invalid world or instance ID.");
                return false;
            }
            try {
                switch (Program.cfg.App.LaunchMode) {
                    case Configuration.LaunchMode.Uri:
                        var joinLink = BuildJoinLink(worldId, instanceId);
                        Console.WriteLine($"Joining world {worldId} instance {instanceId} with link: {joinLink}");
                        StartGame(joinLink, additionalArgs);
                        return true;
                    case Configuration.LaunchMode.Launcher:
                        throw new NotImplementedException();
                    case Configuration.LaunchMode.Steam:
                        throw new NotImplementedException("Steam launch mode is not implemented yet.");
                    case Configuration.LaunchMode.SelfInvite:
                        if (!IsVrchatRunning()) Console.WriteLine($"Using self-invite launch mode but VRChat is not running");
                        Program.client.InviteSelf(worldId, instanceId).GetAwaiter().GetResult();
                        break;
                    case Configuration.LaunchMode.Unknown:
                    default:
                        Console.WriteLine($"Unknown launch mode ({Enum.GetName(typeof(Configuration.LaunchMode), Program.cfg.App.LaunchMode)}). Please check your configuration.");
                        break;
                }
            }
            catch (Exception ex) {
                Console.WriteLine($"Error starting game: {ex.Message}");
            }
            return false;
        }
        internal static Process StartGame(Uri joinLink, List<string> additionalArgs = null)
        {
            // $"{Program.cfg.App.GameArguments}{string.Join(" ", Program.args)}"
            var p = Process.Start(new ProcessStartInfo(joinLink.ToString()) { UseShellExecute = true, Arguments = JoinArgs(Program.cfg.App.GameArguments.Split(' '), additionalArgs) });
            //Console.WriteLine($"Started game as process #{p.Id} with args \"{p.StartInfo.Arguments}\"");
            var commandLine = p.GetCommandLine();
            Console.WriteLine($"{commandLine}");
            return p;
        }
        private static string JoinArgs(params IEnumerable<IEnumerable<string>> args) => string.Join(" ", JoinListsUnique(args));
        internal static IEnumerable<string> JoinListsUnique(params IEnumerable<IEnumerable<string>> arglists)
        {
            var unique = new HashSet<string>();
            foreach (var args in arglists)
            {
                if (args is null) continue;
                foreach (var a in args)
                {
                    if (!string.IsNullOrWhiteSpace(a))
                    {
                        unique.Add(a.Trim());
                    }
                }
            }
            return unique;
        }
    }
}
