using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Reflection;
using System.Web;

namespace VRChatQuickJoin
{
    static class Extensions
    {
        #region Process
        internal static string GetCommandLine(this Process process)
        {
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
        #endregion Process
        #region CookieContainer
        internal static IEnumerable<Cookie> GetAllCookies(this CookieContainer c)
        {
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
        #endregion CookieContainer
        #region Uri
        internal static Uri AddQuery(this Uri uri, string key, string value, bool encode = true)
        {
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
        #endregion Uri
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
        internal static bool WriteAllText(this FileInfo file, string content)
        {
            if (file.Directory != null && !file.Directory.Exists) file.Directory.Create();
            try
            {
                File.WriteAllText(file.FullName, content);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error writing to file {file.FullName}: {ex.Message}");
                return false;
            }
            return true;
        }
        internal static string ReadAllText(this FileInfo file)
        {
            if (file.Directory != null && !file.Directory.Exists) file.Directory.Create();
            if (!file.Exists) return string.Empty;
            return File.ReadAllText(file.FullName);
        }
        #endregion
    }
}
