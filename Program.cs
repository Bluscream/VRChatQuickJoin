using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using Newtonsoft.Json;
using OtpNet;
using VRChat.API.Api;
using VRChat.API.Client;
using VRChat.API.Model;
using VRChatQuickJoin;
using static VRChatQuickJoin.Configuration;
using Configuration = VRChatQuickJoin.Configuration;
using File = System.IO.File;

internal class Program
{
    public static readonly Uri RepositoryUrl = new Uri("https://github.com/Bluscream/VRChatQuickJoin/");
    private static readonly FileInfo ownExe = new FileInfo(Assembly.GetExecutingAssembly().Location);
    private static readonly DirectoryInfo baseDir = ownExe.Directory;
    private static readonly string exeName = ownExe.FileNameWithoutExtension();
    internal static Uri gameUri = new Uri("vrchat://launch"); // ?ref={exeName}
    //private static Uri steamUri = new Uri("steam://launch/438100/");
    //private static DirectoryInfo gameDir = baseDir.Combine("VRChat");
    //private static FileInfo gameLauncherExe = gameDir.CombineFile("launch.exe");
    //private static FileInfo gameEACExe = gameDir.CombineFile("start_protected_game.exe");
    internal static VrcApiClient client;
    internal static Configuration cfg;
    internal static List<string> args;
    static async Task Main(string[] _args)
    {
        args = _args.ToList();

        cfg = new Configuration(baseDir.CombineFile($"{exeName}.json"));
        cfg.LoadConfiguration();

        try
        {
            client = new VrcApiClient();
            client.Configuration = new VRChat.API.Client.Configuration()
            {
                UserAgent = "VRChatQuickJoin/1.0",
                Username = cfg.App.Username,
                Password = cfg.App.Password
            };

            Console.WriteLine("Logging in...");
            if (!string.IsNullOrWhiteSpace(cfg.App._AuthCookie))
            {
                Console.WriteLine("Using existing cookies from App Config");
                var cookieStr = $"auth={cfg.App._AuthCookie};";
                if (!string.IsNullOrWhiteSpace(cfg.App._TwoFactorAuthCookie)) cookieStr += $" twoFactorAuth={cfg.App._TwoFactorAuthCookie};";
                client.Configuration.DefaultHeaders.Add("Cookie", cookieStr);
            }

            var currentUserResp = client.Auth.GetCurrentUserWithHttpInfo();
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
                client.Auth.Verify2FAEmailCode(new TwoFactorEmailCode(code));
            }
            else
            {
                Console.WriteLine("Regular 2FA required");
                var code = string.Empty;
                if (string.IsNullOrWhiteSpace(cfg.App.TOTPSecret))
                {
                    Console.Write("Enter 2FA Code: ");
                    code = Console.ReadLine();
                }
                else
                {
                    var secretBytes = Base32Encoding.ToBytes(cfg.App.TOTPSecret);
                    code = new Totp(secretBytes).ComputeTotp(DateTime.UtcNow);
                    Console.WriteLine($"Generated 2FA Code: {code}");
                }
                client.Auth.Verify2FA(new TwoFactorAuthCode(code));
            }
            //}
            if (cfg.App.FetchDetails) {
                var currentUser = client.Auth.GetCurrentUser();
                Console.WriteLine($"Logged in as {currentUser.DisplayName}");
            }

            var cookies = Extensions.GetAllCookies(ApiClient.CookieContainer).ToList();
            foreach (var cookie in cookies)
            {
                if (cookie.Name == "auth") cfg.App._AuthCookie = cookie.Value;
                if (cookie.Name == "twoFactorAuth") cfg.App._TwoFactorAuthCookie = cookie.Value;
            }

            #region MAIN_LOGIC
            bool joined = false;
            if (cfg.App.Ids != null && cfg.App.Ids.Count > 0)
            {
                Console.WriteLine($"Trying {cfg.App.Ids.Count} Ids");
                var idsCopy = new Dictionary<string, string>(cfg.App.Ids);
                foreach (var kvp in idsCopy)
                {
                    var id = kvp.Key; var comment = kvp.Value;
                    Console.WriteLine($"Trying ID: {id} ({comment})");
                    if (await client.TryId(id))
                    {
                        joined = true;
                        break;
                    }
                }
            }
            if (!joined)
            {
                Utils.RunAdditionalApps(cfg.App.RunAdditional);
                Utils.StartGame(gameUri, args);
            }
            #endregion MAIN_LOGIC
            cfg.SaveConfiguration();

        }
        catch (ApiException ex)
        {
            Console.WriteLine($"API Error: {ex.Message}");
            Console.WriteLine($"Status Code: {ex.ErrorCode}");
        }
        //catch (Exception ex)
        //{
        //    Console.WriteLine($"Error: {ex.Message}");
        //}
        if (cfg.App.WaitOnExit)
        {
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
        else
        {
            await Task.Delay(1000);
            Console.WriteLine("Exiting...");
        }
    }
}