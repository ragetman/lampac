﻿using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Online;
using Shared.Model.Online.FanCDN;
using Shared.Engine.Online;
using Shared.Engine;
using Lampac.Models.LITE;
using Microsoft.Playwright;
using Shared.Engine.CORE;
using System;
using Shared.PlaywrightCore;
using Lampac.Engine.CORE;
using Shared.Model.Online;
using System.Net;
using BrowserCookie = Microsoft.Playwright.Cookie;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Routing;

namespace Lampac.Controllers.LITE
{
    public class FanCDN : BaseOnlineController
    {
        [HttpGet]
        [Route("lite/fancdn")]
        async public Task<ActionResult> Index(string imdb_id, long kinopoisk_id, string title, string original_title, int year, int serial, int t = -1, int s = -1, bool origsource = false, bool rjson = false)
        {
            var init = await loadKit(AppInit.conf.FanCDN);
            if (await IsBadInitialization(init, rch: true))
                return badInitMsg;

            if ((string.IsNullOrEmpty(init.token) && string.IsNullOrEmpty(init.cookie)) || kinopoisk_id == 0)
                return OnError();

            reset: var rch = new RchClient(HttpContext, host, init, requestInfo);
            if (rch.IsNotSupport("web,cors", out string rch_error))
                return ShowError(rch_error);

            var proxyManager = new ProxyManager(init);
            var proxy = proxyManager.BaseGet();

            var oninvk = new FanCDNInvoke
            (
               host,
               init.host,
               async ongettourl => 
               {
                   if (ongettourl.Contains("fancdn."))
                       return await black_magic(init, rch, init.cors(ongettourl), proxy);

                   var headers = httpHeaders(init, HeadersModel.Init(
                       ("sec-fetch-dest", "document"),
                       ("sec-fetch-mode", "navigate"),
                       ("sec-fetch-site", "none"),
                       ("cookie", init.cookie)
                   ));

                   if (rch.enable)
                       return await rch.Get(init.cors(ongettourl), headers);

                   if (init.priorityBrowser == "http")
                       return await HttpClient.Get(init.cors(ongettourl), httpversion: 2, timeoutSeconds: 8, proxy: proxy.proxy, headers: headers);

                   #region Browser Search
                   try
                   {
                       using (var browser = new PlaywrightBrowser())
                       {
                           var page = await browser.NewPageAsync(init.plugin, proxy: proxy.data);
                           if (page == null)
                               return null;

                           string fanhost = "." + Regex.Replace(init.host, "^https?://", "");
                           var excookie = DateTimeOffset.UtcNow.AddYears(1).ToUnixTimeSeconds();

                           await page.Context.ClearCookiesAsync(new BrowserContextClearCookiesOptions { Domain = fanhost, Name = "cf_clearance" });

                           var cookies = new List<BrowserCookie>();
                           foreach (string line in init.cookie.Split(";"))
                           {
                               if (string.IsNullOrEmpty(line) || !line.Contains("=") || line.Contains("cf_clearance") || line.Contains("PHPSESSID"))
                                   continue;

                               cookies.Add(new BrowserCookie()
                               {
                                   Domain = fanhost,
                                   Expires = excookie,
                                   Path = "/",
                                   HttpOnly = true,
                                   Secure = true,
                                   Name = line.Split("=")[0].Trim(),
                                   Value = line.Split("=")[1].Trim()
                               });
                           }

                           await page.Context.AddCookiesAsync(cookies);

                           var response = await page.GotoAsync($"view-source:{ongettourl}");
                           if (response == null)
                               return null;

                           //await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

                           string result = await response.TextAsync();
                           PlaywrightBase.WebLog("GET", ongettourl, result, proxy.data, response: response);
                           return result;
                       }
                   }
                   catch
                   {
                       return null;
                   }
                   #endregion
               },
               streamfile => HostStreamProxy(init, streamfile, proxy: proxy.proxy)
            );

            var cache = await InvokeCache<EmbedModel>(rch.ipkey($"fancdn:{title}", proxyManager), cacheTime(20, init: init), proxyManager, async res =>
            {
                if (rch.IsNotConnected())
                    return res.Fail(rch.connectionMsg);

                var result = string.IsNullOrEmpty(init.token) ? await oninvk.EmbedSearch(title, original_title, year, serial) : await oninvk.EmbedToken(kinopoisk_id, init.token);
                if (result == null)
                    return res.Fail("result");

                return result;
            });

            if (IsRhubFallback(cache, init))
                goto reset;

            return OnResult(cache, () => oninvk.Html(cache.Value, imdb_id, kinopoisk_id, title, original_title, t, s, rjson: rjson, vast: init.vast), origsource: origsource);
        }


        #region black_magic
        async ValueTask<string> black_magic(OnlinesSettings init, RchClient rch, string uri, (WebProxy proxy, (string ip, string username, string password) data) baseproxy)
        {
            try
            {
                var headers = httpHeaders(init, HeadersModel.Init(
                    ("sec-fetch-dest", "iframe"),
                    ("sec-fetch-mode", "navigate"),
                    ("sec-fetch-site", "cross-site"),
                    ("referer", $"{init.host}/")
                ));

                if (rch.enable)
                    return await rch.Get(uri, headers);

                if (init.priorityBrowser == "http")
                    return await HttpClient.Get(uri, httpversion: 2, timeoutSeconds: 8, proxy: baseproxy.proxy, headers: headers);

                using (var browser = new PlaywrightBrowser())
                {
                    var page = await browser.NewPageAsync(init.plugin, proxy: baseproxy.data);
                    if (page == null)
                        return null;

                    browser.failedUrl = uri;

                    await page.Context.ClearCookiesAsync(new BrowserContextClearCookiesOptions { Domain = ".fancdn.net", Name = "cf_clearance" });

                    await page.RouteAsync("**/*", async route =>
                    {
                        try
                        {
                            if (route.Request.Url.Contains("api/chromium/iframe"))
                            {
                                await route.ContinueAsync();
                                return;
                            }

                            if (browser.IsCompleted)
                            {
                                Console.WriteLine($"Playwright: Abort {route.Request.Url}");
                                await route.AbortAsync();
                                return;
                            }

                            if (route.Request.Url == uri)
                            {
                                string html = null;
                                await route.ContinueAsync(new RouteContinueOptions
                                {
                                    Headers = headers.ToDictionary()
                                });

                                var response = await page.WaitForResponseAsync(route.Request.Url);
                                if (response != null)
                                    html = await response.TextAsync();

                                browser.SetPageResult(html);
                                PlaywrightBase.WebLog(route.Request, response, html, baseproxy.data);
                                return;
                            }

                            await route.AbortAsync();
                        }
                        catch { }
                    });

                    var response = await page.GotoAsync(PlaywrightBase.IframeUrl(uri));
                    if (response == null)
                        return null;

                    return await browser.WaitPageResult();
                }
            }
            catch
            {
                return null; 
            }
        }
        #endregion
    }
}
