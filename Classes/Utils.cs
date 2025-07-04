using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace VRChatQuickJoin
{
    static class Utils
    {
        internal static bool IsVirtualDesktopConnected() => Process.GetProcessesByName("VRServer").Any();
        internal static bool IsSteamVRRunning() => Process.GetProcessesByName("vrmonitor").Any() && Process.GetProcessesByName("vrcompositor").Any();
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
                        RunAdditionalApps(Program.cfg.App.RunAdditional);
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
            var args = JoinArgs(Program.cfg.App.GameArguments.Split(' '), additionalArgs);
            if (Program.useVR)
            {
                args += "--vrmode OpenVR";
            } else
            {
                args += " --no-vr -vrmode None";
            }
# if DEBUG
            return null;
#endif
            var p = Process.Start(new ProcessStartInfo(joinLink.ToString()) { UseShellExecute = true, Arguments = args });
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
        /// <summary>
        /// Launches each additional app in the form of [binary, arg1, arg2, ...] using ShellExecute, not waiting for them to finish.
        /// </summary>
        /// <param name="apps">A list of apps, each as a list of strings: [binary, arg1, arg2, ...]</param>
        internal static void RunAdditionalApps(List<List<string>> apps)
        {
            if (apps == null || apps.Count == 0) return;
            foreach (var app in apps)
            {
                if (app == null || app.Count == 0 || string.IsNullOrWhiteSpace(app[0])) continue;
                var binary = app[0];
                var args = app.Count > 1 ? string.Join(" ", app.Skip(1).Select(a => a.Contains(' ') ? $"\"{a}\"" : a)) : string.Empty;
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = binary,
                        Arguments = args,
                        UseShellExecute = true,
                        CreateNoWindow = true,
                    };
                    Process.Start(psi);
                    Console.WriteLine($"Launched additional app: {binary} {args}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to launch additional app: {binary} {args}\n{ex.Message}");
                }
            }
        }
    }
}
