using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OtpNet;
using Polly;
using VRChat.API.Api;
using VRChat.API.Client;
using VRChat.API.Model;
using System.Web;
using File = System.IO.File;

class Program
{
    internal static readonly string exeName = Path.GetFileNameWithoutExtension(Assembly.GetExecutingAssembly().Location);
    internal static readonly string configFileName = $"{exeName}.json";
    internal static readonly string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, configFileName);
    internal static Uri gameUri = new Uri($"vrchat://launch?ref={exeName}");
    internal static AppConfig appConfig;
    internal static string[] args;
    public class AppConfig
    {
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public string TOTPSecret { get; set; } = "";
        public string GroupId { get; set; } = "";
        public string WorldId { get; set; } = "";
        public string AuthCookie { get; set; } = "";
        public string TwoFactorAuthCookie { get; set; } = "";
        public string GameArguments { get; set; } = "";
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

            // Load configuration from file
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

    static async Task Main(string[] _args) {
        args = _args;
        string configFileName = $"{exeName}.json";
        string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, configFileName);
        appConfig = LoadConfiguration();
        var libConfig = new Configuration {
            UserAgent = "VRChatQuickJoin/1.0",
            Username = appConfig.Username,
            Password = appConfig.Password
        };

        try {
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
            } else {
                Console.WriteLine("Regular 2FA required");
                var code = string.Empty;
                if (string.IsNullOrWhiteSpace(appConfig.TOTPSecret))
                {
                    Console.Write("Enter 2FA Code: ");
                    code = Console.ReadLine();
                } else {
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

            if (!string.IsNullOrWhiteSpace(appConfig.GroupId)) {
                try {
                    Console.WriteLine($"Using Group ID: {appConfig.GroupId}");
                    var groupInstances = await groupApi.GetGroupInstancesAsync(appConfig.GroupId);
                    Console.WriteLine($"Found {groupInstances.Count} Group Instances");
                    var instance = groupInstances.OrderByDescending(i => i.MemberCount).FirstOrDefault();
                    if (instance != null)
                    {
                        Console.WriteLine($"Instance: {instance.InstanceId} ({instance.MemberCount})");
                        var joinLink = Extensions.BuildJoinLink(instance.World.Id, instance.InstanceId);
                        var process = Extensions.StartGame(joinLink);
                        Console.WriteLine($"Started game as process {process.Id}\n{process.StartInfo.Arguments}");
                        return;
                    }
                } catch (Exception ex) {
                    Console.WriteLine($"Error fetching group instances: {ex.Message}");
                }
            }

            if (!string.IsNullOrWhiteSpace(appConfig.WorldId)) {
                try
                {
                    Console.WriteLine($"Using World ID: {appConfig.WorldId}");
                    var world = await worldsApi.GetWorldAsync(appConfig.WorldId);
                    Console.WriteLine($"Resolved World: \"{world.Name}\" by \"{world.AuthorName}\"");
                    var _instance = world.Instances.OrderByDescending(i => i.Count).FirstOrDefault();
                    if (_instance != null)
                    {
                        var InstanceId = _instance[0].ToString();
                        var Location = $"{appConfig.WorldId}:{InstanceId}";
                        var UserCount = int.Parse(_instance[1].ToString());
                        Console.WriteLine($"Instance: {InstanceId} ({UserCount})");
                        var joinLink = Extensions.BuildJoinLink(appConfig.WorldId, InstanceId);
                        var process = Extensions.StartGame(joinLink);
                        Console.WriteLine($"Started game as process {process.Id}\n{process.StartInfo.Arguments}");
                        return;
                    }
                } catch (Exception ex) {
                    Console.WriteLine($"Error fetching world instances: {ex.Message}");
                }
            }

            Extensions.StartGame(gameUri);

#endregion MAIN_LOGIC

        }
        catch (ApiException ex) {
            Console.WriteLine($"API Error: {ex.Message}");
            Console.WriteLine($"Status Code: {ex.ErrorCode}");
        } catch (Exception ex) {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }
}

static class Extensions {
    public static IEnumerable<Cookie> GetAllCookies(this CookieContainer c) {
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
    internal static Uri AddQuery(this Uri uri, string key, string value) {
        var uriBuilder = new UriBuilder(uri);
        var query = HttpUtility.ParseQueryString(uriBuilder.Query);
        query[key] = value;
        uriBuilder.Query = query.ToString();
        return uriBuilder.Uri;
    }
    internal static Uri BuildJoinLink(string worldId, string instanceId) {
        return Program.gameUri.AddQuery("id", $"{worldId}:{instanceId}");
    }
    internal static Process StartGame(Uri joinLink) {
        return Process.Start(new ProcessStartInfo(joinLink.ToString()) { UseShellExecute = true, Arguments = $"{Program.appConfig.GameArguments}{string.Join(" ", Program.args)}" });
    }
}