﻿using Lampac;
using Lampac.Engine.CORE;
using Microsoft.Playwright;
using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Web;

namespace Shared.Engine
{
    public enum PlaywrightStatus
    {
        disabled,
        headless,
        NoHeadless
    }

    public class PlaywrightBase
    {
        async public static ValueTask<bool> InitializationAsync()
        {
            try
            {
                if (!AppInit.conf.chromium.enable && !AppInit.conf.firefox.enable)
                    return false;

                if (!File.Exists(".playwright/package/index.js"))
                {
                    bool res = await DownloadFile("https://github.com/immisterio/playwright/releases/download/chrome/package.zip", ".playwright/package.zip");
                    if (!res)
                    {
                        Console.WriteLine("Playwright: error download package.zip");
                        return false;
                    }
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    switch (RuntimeInformation.ProcessArchitecture)
                    {
                        case Architecture.X86:
                        case Architecture.X64:
                        case Architecture.Arm64:
                            {
                                string arc = RuntimeInformation.ProcessArchitecture.ToString().ToLower();
                                bool res = await DownloadFile($"https://github.com/immisterio/playwright/releases/download/chrome/node-win-{arc}.exe", $".playwright\\node\\win32_{arc}\\node.exe");
                                if (!res)
                                {
                                    Console.WriteLine($"Playwright: error download node-win-{arc}.exe");
                                    return false;
                                }
                                break;
                            }
                        default:
                            Console.WriteLine("Playwright: Architecture unknown");
                            return false;
                    }
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    switch (RuntimeInformation.ProcessArchitecture)
                    {
                        case Architecture.X64:
                        case Architecture.Arm64:
                            {
                                string arc = RuntimeInformation.ProcessArchitecture.ToString().ToLower();
                                bool res = await DownloadFile($"https://github.com/immisterio/playwright/releases/download/chrome/node-mac-{arc}", $".playwright/node/mac-{arc}/node");
                                if (!res)
                                {
                                    Console.WriteLine($"Playwright: error download node-mac-{arc}");
                                    return false;
                                }

                                await Bash.Run($"chmod +x {Path.Join(Directory.GetCurrentDirectory(), $".playwright/node/mac-{arc}/node")}");
                                break;
                            }
                        default:
                            Console.WriteLine("Playwright: Architecture unknown");
                            return false;
                    }
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    switch (RuntimeInformation.ProcessArchitecture)
                    {
                        case Architecture.X86:
                        case Architecture.X64:
                        case Architecture.Arm64:
                            {
                                string arc = RuntimeInformation.ProcessArchitecture.ToString().ToLower();
                                bool res = await DownloadFile($"https://github.com/immisterio/playwright/releases/download/chrome/node-linux-{arc}", $".playwright/node/linux-{arc}/node");
                                if (!res)
                                {
                                    Console.WriteLine($"Playwright: error download node-linux-{arc}");
                                    return false;
                                }

                                await Bash.Run($"chmod +x {Path.Join(Directory.GetCurrentDirectory(), $".playwright/node/linux-{arc}/node")}");
                                break;
                            }
                        case Architecture.Arm:
                            {
                                bool res = await DownloadFile("https://github.com/immisterio/playwright/releases/download/chrome/node-linux-armv7l", ".playwright/node/linux-arm/node");
                                if (!res)
                                {
                                    Console.WriteLine("Playwright: error download node-linux-armv7l");
                                    return false;
                                }

                                await Bash.Run($"chmod +x {Path.Join(Directory.GetCurrentDirectory(), ".playwright/node/linux-arm/node")}");
                                break;
                            }
                        default:
                            Console.WriteLine("Playwright: Architecture unknown");
                            return false;
                    }
                }
                else
                {
                    Console.WriteLine("Playwright: IsOSPlatform unknown");
                    return false;
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && (AppInit.conf.chromium.Xvfb || AppInit.conf.firefox.Xvfb))
                {
                    _ = Bash.Run("Xvfb :99 -screen 0 1280x1024x24").ConfigureAwait(false);
                    Environment.SetEnvironmentVariable("DISPLAY", ":99");
                    await Task.Delay(TimeSpan.FromSeconds(5));
                    Console.WriteLine("Playwright: Xvfb");
                }

                Console.WriteLine("Playwright: Initialization");
                return true;
            }
            catch (Exception ex) 
            {
                Console.WriteLine($"Playwright: {ex.Message}");
                return false;
            }
        }
        

        async public static ValueTask<bool> DownloadFile(string uri, string outfile, string folder = null)
        {
            if (File.Exists($"{outfile}.ok"))
                return true;

            if (File.Exists(outfile))
                File.Delete(outfile);

            Directory.CreateDirectory(Path.GetDirectoryName(outfile));

            if (await HttpClient.DownloadFile(uri, outfile).ConfigureAwait(false))
            {
                File.Create($"{outfile}.ok");

                if (outfile.EndsWith(".zip"))
                {
                    ZipFile.ExtractToDirectory(outfile, ".playwright/"+folder, overwriteFiles: true);
                    File.Delete(outfile);
                }

                return true;
            }
            else
            {
                File.Delete(outfile);
                return false;
            }
        }
        

        public static void WebLog(IRequest request, IResponse response, string result, (string ip, string username, string password) proxy = default)
        {
            if (request.Url.Contains("127.0.0.1"))
                return;

            string log = $"{DateTime.Now}\n";
            if (proxy != default)
                log += $"proxy: {proxy}\n";

            log += $"{request.Method}: {request.Url}\n";

            foreach (var item in request.Headers)
                log += $"{item.Key}: {item.Value}\n";

            if (response == null)
            {
                log += "\nresponse null";
                HttpClient.onlog?.Invoke(null, log);
                return;
            }

            log += "\n\n";
            foreach (var item in response.Headers)
                log += $"{item.Key}: {item.Value}\n";

            log += $"\n{result}";

            HttpClient.onlog?.Invoke(null, log);
        }

        public static void WebLog(string method, string url, string result, (string ip, string username, string password) proxy = default)
        {
            if (url.Contains("127.0.0.1"))
                return;

            string log = $"{DateTime.Now}\n";
            if (proxy != default)
                log += $"proxy: {proxy}\n";

            HttpClient.onlog?.Invoke(null, $"{method}: {url}\n\n{result}");
        }


        public static string IframeUrl(string link) => $"http://{AppInit.conf.localhost}:{AppInit.conf.listenport}/api/chromium/iframe?src={HttpUtility.UrlEncode(link)}";
    }
}
