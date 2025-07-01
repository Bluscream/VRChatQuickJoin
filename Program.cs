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

class Program
{
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
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public string TOTPSecret { get; set; } = "";
        public List<string> GroupIds { get; set; } = new List<string>();
        public List<string> WorldIds { get; set; } = new List<string>();
        public string AuthCookie { get; set; } = "";
        public string TwoFactorAuthCookie { get; set; } = "";
        public string GameArguments { get; set; } = "";
        public LaunchMode LaunchMode { get; set; } = LaunchMode.Uri;
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

            // Handle 2FA if required
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

            var currentUser = authApi.GetCurrentUser();
            Console.WriteLine($"Logged in as {currentUser.DisplayName}");

            var cookies = Extensions.GetAllCookies(ApiClient.CookieContainer).ToList();
            foreach (var cookie in cookies)
            {
                if (cookie.Name == "auth") appConfig.AuthCookie = cookie.Value;
                if (cookie.Name == "twoFactorAuth") appConfig.TwoFactorAuthCookie = cookie.Value;
            }
            SaveConfiguration(appConfig);

#region MAIN_LOGIC
            if (appConfig.GroupIds != null && appConfig.GroupIds.Count > 0)
            {
                foreach (var groupId in appConfig.GroupIds)
                {
                    try
                    {
                        Console.WriteLine($"Trying Group ID: {groupId}");
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
                            Console.WriteLine(joinLink);
                            var process = Extensions.StartGame(joinLink);
                            Console.WriteLine($"Started game as process {process.Id}\n{process.StartInfo.Arguments}");
                            return;
                        }
                        Console.WriteLine($"No matching instance found for group {groupId}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error fetching group instances for {groupId}: {ex.Message}");
                    }
                }
            }
            if (appConfig.WorldIds != null && appConfig.WorldIds.Count > 0)
            {
                foreach (var worldId in appConfig.WorldIds)
                {
                    try
                    {
                        Console.WriteLine($"Trying World ID: {worldId}");
                        var world = await worldsApi.GetWorldAsync(worldId);
                        Console.WriteLine($"Resolved World: \"{world.Name}\" by \"{world.AuthorName}\"");
                        var instances = world.Instances.OrderByDescending(i => i.Count);
                        foreach (var _instance in instances) 
                        {
                            if (_instance is null) return;
                            var validUserCount = int.TryParse(_instance[1].ToString(), out int userCount);
                            if (validUserCount && userCount <= 0) continue; // Skip empty instances
                            if (validUserCount && userCount >= _instance.Capacity) continue; // Skip full instances
                            var InstanceId = _instance[0].ToString();
                            var Location = $"{worldId}:{InstanceId}";
                            Console.WriteLine($"Instance: {InstanceId} ({userCount})");
                            var joinLink = Extensions.BuildJoinLink(worldId, InstanceId);
                            Console.WriteLine(joinLink);
                            var process = Extensions.StartGame(joinLink);
                            Console.WriteLine($"Started game as process {process.Id}\n{process.StartInfo.Arguments}");
                            return;
                        }
                        Console.WriteLine("No non-empty instance found for this world.");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error fetching world instances for {worldId}: {ex.Message}");
                    }
                }
            }

            Extensions.StartGame(gameUri);

            #endregion MAIN_LOGIC

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
        finally
        {
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
    }
}

static class Extensions {
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
        return Process.Start(new ProcessStartInfo(joinLink.ToString()) { UseShellExecute = true, Arguments = $"{Program.appConfig.GameArguments}{string.Join(" ", Program.args)}" });
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