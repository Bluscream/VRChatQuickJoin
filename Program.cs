using File = System.IO.File;
using Newtonsoft.Json;
using OtpNet;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using System.Web;
using VRChat.API.Api;
using VRChat.API.Client;
using VRChat.API.Model;
using System.Management;

internal class Program
{
    public static readonly Uri RepositoryUrl = new Uri("https://github.com/Bluscream/VRChatQuickJoin/");
    private static readonly FileInfo ownExe = new FileInfo(Assembly.GetExecutingAssembly().Location);
    private static readonly DirectoryInfo baseDir = ownExe.Directory;
    private static readonly string exeName = ownExe.FileNameWithoutExtension();
    private static readonly string configFileName = $"{exeName}.json";
    private static readonly string configPath = baseDir.CombineFile(configFileName).FullName;
    internal static Uri gameUri = new Uri("vrchat://launch"); // ?ref={exeName}
    //private static Uri steamUri = new Uri("steam://launch/438100/");
    //private static DirectoryInfo gameDir = baseDir.Combine("VRChat");
    //private static FileInfo gameLauncherExe = gameDir.CombineFile("launch.exe");
    //private static FileInfo gameEACExe = gameDir.CombineFile("start_protected_game.exe");
    internal static AppConfig appConfig;
    internal static string[] args;
    internal enum LaunchMode {
        Unknown = 0,
        Uri,
        Launcher,
        Steam
    }
    public class AppConfig
    {
        //public bool Skip2FA { get; set; } = false;
        public bool WaitOnExit { get; set; } = false;
        public bool FetchGroupDetails { get; set; } = false;
        public bool OverwriteComments { get; set; } = true;
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public string TOTPSecret { get; set; } = "";
        public string GameArguments { get; set; } = "";
        public LaunchMode LaunchMode { get; set; } = LaunchMode.Uri;
        public Dictionary<string, string> Ids { get; set; } = new Dictionary<string, string>();
        public string AuthCookie { get; set; } = "";
        public string TwoFactorAuthCookie { get; set; } = "";
    }

    private static AppConfig LoadConfiguration()
    {
        try {
            Console.WriteLine($"Loading {configPath}");

            if (!File.Exists(configPath))
            {
                Console.WriteLine($"Configuration file not found. Creating default configuration file at\n{configPath}");
                var defaultConfig = new AppConfig {};
                var json = JsonConvert.SerializeObject(defaultConfig, Formatting.Indented);
                File.WriteAllText(configPath, json);
                
                Console.WriteLine("Default configuration file created. Please edit it with your credentials and run the application again.");
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
                Environment.Exit(0);
            }
            var configJson = File.ReadAllText(configPath);
            var config = JsonConvert.DeserializeObject<AppConfig>(configJson);

            if (config == null)
            {
                throw new InvalidOperationException("Failed to deserialize configuration file.");
            }

            Console.WriteLine("Configuration loaded successfully.");
            return config;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading configuration: {ex.Message}");
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
            Environment.Exit(1);
            return null;
        }
    }
    private static bool SaveConfiguration(AppConfig appConfig) {
    try {
        Console.WriteLine($"Saving configuration to {configPath}");
        var json = JsonConvert.SerializeObject(appConfig, Formatting.Indented);
        File.WriteAllText(configPath, json);
        Console.WriteLine("Configuration saved successfully.");
        return true;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error saving configuration: {ex.Message}");
        return false;
    }
    }

    static async Task Main(string[] _args)
    {
        args = _args;
        appConfig = LoadConfiguration();
        var libConfig = new Configuration
        {
            UserAgent = "VRChatQuickJoin/1.0",
            Username = appConfig.Username,
            Password = appConfig.Password
        };

        try
        {
            var client = new ApiClient();
            var authApi = new AuthenticationApi(client, client, libConfig);
            var worldsApi = new WorldsApi(client, client, libConfig);
            var groupApi = new GroupsApi(client, client, libConfig);
            var usersApi = new UsersApi(client, client, libConfig);

            Console.WriteLine("Logging in...");
            if (!string.IsNullOrWhiteSpace(appConfig.AuthCookie))
            {
                Console.WriteLine("Using existing cookies from App Config");
                var cookieStr = $"auth={appConfig.AuthCookie};";
                if (!string.IsNullOrWhiteSpace(appConfig.TwoFactorAuthCookie)) cookieStr += $" twoFactorAuth={appConfig.TwoFactorAuthCookie};";
                libConfig.DefaultHeaders.Add("Cookie", cookieStr);
            }

            var currentUserResp = authApi.GetCurrentUserWithHttpInfo();
            if (currentUserResp.RawContent is null)
            {
                Console.WriteLine("Failed to read server response (Are you online?)");
                return;
            }

            //if (!appConfig.Skip2FA)
            //{
            if (currentUserResp.RawContent.Contains("emailOtp"))
            {
                Console.WriteLine("Email 2FA required");
                Console.Write("Enter 2FA Code: ");
                var code = Console.ReadLine();
                authApi.Verify2FAEmailCode(new TwoFactorEmailCode(code));
            }
            else
            {
                Console.WriteLine("Regular 2FA required");
                var code = string.Empty;
                if (string.IsNullOrWhiteSpace(appConfig.TOTPSecret))
                {
                    Console.Write("Enter 2FA Code: ");
                    code = Console.ReadLine();
                }
                else
                {
                    var secretBytes = Base32Encoding.ToBytes(appConfig.TOTPSecret);
                    code = new Totp(secretBytes).ComputeTotp(DateTime.UtcNow);
                    Console.WriteLine($"Generated 2FA Code: {code}");
                }
                authApi.Verify2FA(new TwoFactorAuthCode(code));
            }
            //}
            var currentUser = authApi.GetCurrentUser();
            Console.WriteLine($"Logged in as {currentUser.DisplayName}");

            var cookies = Extensions.GetAllCookies(ApiClient.CookieContainer).ToList();
            foreach (var cookie in cookies)
            {
                if (cookie.Name == "auth") appConfig.AuthCookie = cookie.Value;
                if (cookie.Name == "twoFactorAuth") appConfig.TwoFactorAuthCookie = cookie.Value;
            }

#region MAIN_LOGIC
            bool joined = false;
            if (appConfig.Ids != null && appConfig.Ids.Count > 0) {
                Console.WriteLine($"Trying {appConfig.Ids.Count} Ids");
                var idsCopy = new Dictionary<string, string>(appConfig.Ids);
                foreach (var kvp in idsCopy)
                {
                    var id = kvp.Key; var comment = kvp.Value;
                    Console.WriteLine($"Trying ID: {id} ({comment})");
                    if (await TryId(id, groupApi, worldsApi, usersApi)) {
                        joined = true;
                        break;
                    }
                }
            }
            if (!joined)
            {
                Extensions.StartGame(gameUri);
            }
#endregion MAIN_LOGIC
            SaveConfiguration(appConfig);

        }
        catch (ApiException ex)
        {
            Console.WriteLine($"API Error: {ex.Message}");
            Console.WriteLine($"Status Code: {ex.ErrorCode}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
        if (appConfig.WaitOnExit)
        {
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
        else
        {
            Console.WriteLine("Exiting...");
        }
    }

    static async Task<bool> TryId(string id, GroupsApi groupApi, WorldsApi worldsApi, UsersApi usersApi)
    {
        if (id.StartsWith("grp_"))
        {
            return await TryGroupId(id, groupApi);
        }
        else if (id.StartsWith("wrld_"))
        {
            return await TryWorldId(id, worldsApi);
        }
        else if (id.StartsWith("usr_"))
        {
            return await TryUserId(id, usersApi, worldsApi);
        }
        else
        {
            Console.WriteLine($"Unknown or Unsupported id prefix: {id}");
            Console.WriteLine($"Please file an issue at {RepositoryUrl}issues/new");
            return false;
        }
    }

    static async Task<bool> TryGroupId(string groupId, GroupsApi groupApi)
    {
        try
        {
            //Console.WriteLine($"Trying Group ID: {groupId}");
if (appConfig.FetchGroupDetails) {
            var group = await groupApi.GetGroupAsync(groupId);
            if (group is null)
            {
                Console.WriteLine($"Group {groupId} not found.");
                return false;
            }
            if (appConfig.OverwriteComments) appConfig.Ids[groupId] = group.Name; // Update config with group name
}
            var groupInstances = await groupApi.GetGroupInstancesAsync(groupId);
            Console.WriteLine($"Found {groupInstances.Count} Group Instances");
            var instances = groupInstances.OrderByDescending(i => i.MemberCount);
            foreach (var instance in instances)
            {
                if (instance is null) continue;
                if (instance.MemberCount <= 0) continue; // Skip empty instances
                if (instance.MemberCount >= instance.World.Capacity) continue; // Skip full instances
                Console.WriteLine($"Instance: {instance.InstanceId} ({instance.MemberCount})");
                var joinLink = Extensions.BuildJoinLink(instance.World.Id, instance.InstanceId);
                Extensions.StartGame(joinLink);
                return true;
            }
            Console.WriteLine($"No matching instance found for group {groupId}");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching group instances for {groupId}: {ex.Message}");
            return false;
        }
    }

    static async Task<bool> TryWorldId(string worldId, WorldsApi worldsApi)
    {
        try
        {
            //Console.WriteLine($"Trying World ID: {worldId}");
            var world = await worldsApi.GetWorldAsync(worldId);
            if (world is null) {
                Console.WriteLine($"World {worldId} not found.");
                return false;
            }
            if (appConfig.OverwriteComments) appConfig.Ids[worldId] = $"{world.Name} by {world.AuthorName}"; // Update config with world name
            Console.WriteLine($"Resolved World: \"{world.Name}\" by \"{world.AuthorName}\"");
            var instances = world.Instances.OrderByDescending(i => i.Count);
            foreach (var _instance in instances)
            {
                if (_instance is null) return false;
                var validUserCount = int.TryParse(_instance[1].ToString(), out int userCount);
                if (validUserCount && userCount <= 0) continue; // Skip empty instances
                if (validUserCount && userCount >= _instance.Capacity) continue; // Skip full instances
                var InstanceId = _instance[0].ToString();
                var Location = $"{worldId}:{InstanceId}";
                Console.WriteLine($"Instance: {InstanceId} ({userCount})");
                var joinLink = Extensions.BuildJoinLink(worldId, InstanceId);
                Extensions.StartGame(joinLink);
                return true;
            }
            Console.WriteLine($"No matching instance found for world {worldId}");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching world instances for {worldId}: {ex.Message}");
            return false;
        }
    }

    static async Task<bool> TryUserId(string userId, UsersApi usersApi, WorldsApi worldsApi)
    {
        try
        {
            //Console.WriteLine($"Trying User ID: {userId}");
            var user = await usersApi.GetUserAsync(userId);
            if (user == null)
            {
                Console.WriteLine($"User {userId} not found or not a friend.");
                return false;
            }
            if (appConfig.OverwriteComments) appConfig.Ids[userId] = user.DisplayName; // Update config with user display name
            if (string.IsNullOrEmpty(user.Location) || user.Location == "traveling" || user.Location == "offline" || user.Location == "private")
            {
                Console.WriteLine($"User {user.DisplayName} is not in a joinable location (Location: {user.Location}).");
                return false;
            }
            Console.WriteLine($"User {user.DisplayName} is in location: {user.Location}");
            // Location format: wrld_xxxx:instanceId~... or similar
            var locationParts = user.Location.Split(':');
            if (locationParts.Length < 2 || !locationParts[0].StartsWith("wrld_"))
            {
                Console.WriteLine($"User {user.DisplayName} is not in a joinable world instance.");
                return false;
            }
            var worldId = locationParts[0];
            var instanceId = locationParts[1];
            var joinLink = Extensions.BuildJoinLink(worldId, instanceId);
            Console.WriteLine($"Joining user {user.DisplayName} at {joinLink}");
            Extensions.StartGame(joinLink);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching user {userId}: {ex.Message}");
            return false;
        }
    }
}

static class Extensions {
    internal static string GetCommandLine(this Process process) {
        if (process == null)
        {
            throw new ArgumentNullException(nameof(process));
        }

        try
        {
            using (var searcher = new ManagementObjectSearcher("SELECT CommandLine FROM Win32_Process WHERE ProcessId = " + process.Id))
            using (var objects = searcher.Get())
            {
                var result = objects.Cast<ManagementBaseObject>().SingleOrDefault();
                return result?["CommandLine"]?.ToString() ?? string.Empty;
            }
        }
        catch
        {
            return string.Empty;
        }
    }
    internal static IEnumerable<Cookie> GetAllCookies(this CookieContainer c) {
        Hashtable k = (Hashtable)c.GetType().GetField("m_domainTable", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(c);
        foreach (DictionaryEntry element in k)
        {
            SortedList l = (SortedList)element.Value.GetType().GetField("m_list", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(element.Value);
            foreach (var e in l)
            {
                var cl = (CookieCollection)((DictionaryEntry)e).Value;
                foreach (Cookie fc in cl)
                {
                    yield return fc;
                }
            }
        }
    }
    internal static Uri AddQuery(this Uri uri, string key, string value, bool encode = true) {
        var uriBuilder = new UriBuilder(uri);
        var query = HttpUtility.ParseQueryString(uriBuilder.Query);
        if (encode)
        {
            query[key] = value;
            uriBuilder.Query = query.ToString();
        }
        else
        {
            var queryDict = query.AllKeys.Where(k => k != null).ToDictionary(k => k, k => query[k]);
            queryDict[key] = value;
            var queryString = string.Join("&", queryDict.Select(kvp => $"{HttpUtility.UrlEncode(kvp.Key)}={(kvp.Key == key ? value : HttpUtility.UrlEncode(kvp.Value))}"));
            uriBuilder.Query = queryString;
        }
        return uriBuilder.Uri;
    }
    internal static Uri BuildJoinLink(string worldId, string instanceId) {
        return Program.gameUri.AddQuery("id", $"{worldId}:{instanceId}", encode: false);
    }
    internal static Process StartGame(Uri joinLink) {
        var p = Process.Start(new ProcessStartInfo(joinLink.ToString()) { UseShellExecute = true, Arguments = $"{Program.appConfig.GameArguments}{string.Join(" ", Program.args)}" });
        //Console.WriteLine($"Started game as process #{p.Id} with args \"{p.StartInfo.Arguments}\"");
        var commandLine = p.GetCommandLine();
        Console.WriteLine($"{commandLine}");
        return p;
    }
    #region DirectoryInfo
    internal static DirectoryInfo Combine(this DirectoryInfo dir, params string[] paths)
    {
        var final = dir.FullName;
        foreach (var path in paths)
        {
            final = Path.Combine(final, path);
        }
        return new DirectoryInfo(final);
    }
    internal static bool IsEmpty(this DirectoryInfo directory)
    {
        return !Directory.EnumerateFileSystemEntries(directory.FullName).Any();
    }
#endregion
#region FileInfo

    internal static FileInfo CombineFile(this DirectoryInfo dir, params string[] paths)
    {
        var final = dir.FullName;
        foreach (var path in paths)
        {
            final = Path.Combine(final, path);
        }
        return new FileInfo(final);
    }
    internal static FileInfo Combine(this FileInfo file, params string[] paths)
    {
        var final = file.DirectoryName;
        foreach (var path in paths)
        {
            final = Path.Combine(final, path);
        }
        return new FileInfo(final);
    }
    internal static string FileNameWithoutExtension(this FileInfo file)
    {
        return Path.GetFileNameWithoutExtension(file.Name);
    }
#endregion
}