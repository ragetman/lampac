﻿using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Web;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Lampac.Engine;
using Lampac.Engine.CORE;
using Newtonsoft.Json.Linq;
using System.Linq;
using Microsoft.Extensions.Caching.Memory;

namespace Lampac.Controllers.LITE
{
    public class Seasonvar : BaseController
    {
        [HttpGet]
        [Route("lite/seasonvar")]
        async public Task<ActionResult> Index(string title, string original_title, int year, int clarification, string original_language, int seasonid, string t, int s = -1)
        {
            if (string.IsNullOrWhiteSpace(AppInit.conf.Seasonvar.token))
                return Content(string.Empty);

            if (original_language != "en")
                clarification = 1;

            seasonid = seasonid == 0 ? await search(clarification == 1 ? title : (original_title ?? title), year) : seasonid;
            if (seasonid == 0)
                return Content(string.Empty);

            bool firstjson = true;
            string html = "<div class=\"videos__line\">";

            if (s == -1)
            {
                #region Сезоны
                foreach (var season in await getSeason(seasonid))
                {
                    string link = $"{host}/lite/seasonvar?seasonid={season.Value}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&s={season.Key}";

                    html += "<div class=\"videos__item videos__season selector " + (firstjson ? "focused" : "") + "\" data-json='{\"method\":\"link\",\"url\":\"" + link + "\"}'><div class=\"videos__season-layers\"></div><div class=\"videos__item-imgbox videos__season-imgbox\"><div class=\"videos__item-title videos__season-title\">" + $"{season.Key} сезон" + "</div></div></div>";
                    firstjson = false;
                }
                #endregion
            }
            else
            {
                var playlist = await getPlaylist(seasonid);
                if (playlist == null)
                    return Content(string.Empty);

                #region Перевод
                foreach (var pl in playlist)
                {
                    string perevod = pl.Value<string>("perevod");

                    if (perevod.ToLower().Contains("трейлер") || html.Contains(perevod))
                        continue;

                    if (string.IsNullOrWhiteSpace(t))
                        t = perevod;

                    string link = $"{host}/lite/seasonvar?seasonid={seasonid}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&s={s}&t={HttpUtility.UrlEncode(perevod)}";
                    string active = t == perevod ? "active" : "";

                    html += "<div class=\"videos__button selector " + active + "\" data-json='{\"method\":\"link\",\"url\":\"" + link + "\"}'>" + perevod + "</div>";
                }

                html += "</div><div class=\"videos__line\">";
                #endregion

                #region Серии
                foreach (var pl in playlist)
                {
                    if (pl.Value<string>("perevod") != t)
                        continue;

                    string link = HostStreamProxy(AppInit.conf.Seasonvar.streamproxy, pl.Value<string>("link"));
                    string name = pl.Value<string>("name");

                    string subtitles = pl.Value<string>("subtitles");
                    if (!string.IsNullOrWhiteSpace(subtitles))
                        subtitles = "{\"label\": \"rus\",\"url\": \"" + subtitles + "\"}";

                    html += "<div class=\"videos__item videos__movie selector " + (firstjson ? "focused" : "") + "\" media=\"\" s=\"" + s + "\" e=\"" + Regex.Match(name.Trim(), "^([0-9]+)").Groups[1].Value + "\" data-json='{\"method\":\"play\",\"url\":\"" + link + "\",\"title\":\"" + $"{title ?? original_title} ({name})" + "\", \"subtitles\": [" + subtitles + "]}'><div class=\"videos__item-imgbox videos__movie-imgbox\"></div><div class=\"videos__item-title\">" + name + "</div></div>";
                    firstjson = false;
                }
                #endregion
            }

            return Content(html + "</div>", "text/html; charset=utf-8");
        }


        #region search
        async ValueTask<int> search(string title, int year)
        {
            if (string.IsNullOrWhiteSpace(title) || year == 0)
                return 0;

            string memKey = $"seasonvar:search:{title}:{year}";
            if (!memoryCache.TryGetValue(memKey, out JArray root))
            {
                root = await HttpClient.Post<JArray>(AppInit.conf.Seasonvar.apihost, $"key={AppInit.conf.Seasonvar.token}&command=search&query={HttpUtility.UrlEncode(title)}", timeoutSeconds: 8, useproxy: AppInit.conf.Seasonvar.useproxy);
                if (root == null || root.Count == 0)
                    return 0;

                memoryCache.Set(memKey, root, DateTime.Now.AddMinutes(AppInit.conf.multiaccess ? 40 : 10));
            }

            int reservedid = 0;
            foreach (var item in root)
            {
                string name = item.Value<string>("name");
                string name_original = item.Value<string>("name_original");
                string y = item.Value<string>("year");

                if (title != name && title != name_original)
                    continue;

                reservedid = int.Parse(item.Value<string>("id"));

                if (year.ToString() != y)
                    continue;

                return reservedid;
            }

            return reservedid;
        }
        #endregion

        #region seasonObject 
        async ValueTask<JObject> seasonObject(int season_id)
        {
            string memKey = $"seasonvar:season:{season_id}";
            if (!memoryCache.TryGetValue(memKey, out JObject root))
            {
                root = await HttpClient.Post<JObject>(AppInit.conf.Seasonvar.apihost, $"key={AppInit.conf.Seasonvar.token}&command=getSeason&season_id={season_id}", timeoutSeconds: 8, useproxy: AppInit.conf.Seasonvar.useproxy);
                if (root == null)
                    return null;

                memoryCache.Set(memKey, root, DateTime.Now.AddMinutes(AppInit.conf.multiaccess ? 40 : 10));
            }

            return root;
        }
        #endregion

        #region getSeason 
        async ValueTask<Dictionary<int, string>> getSeason(int season_id)
        {
            var result = new Dictionary<int, string>();

            var root = await seasonObject(season_id);
            if (root == null)
                return result;

            int season_number = int.Parse(root.Value<string>("season_number"));
            result.Add(season_number == 0 ? 1 : season_number, root.Value<string>("id"));

            var other_season = root.GetValue("other_season");
            if (other_season != null)
            {
                foreach (var item in other_season.ToObject<Dictionary<string, string>>())
                    result.Add(int.Parse(item.Key), item.Value);
            }

            return result.OrderBy(i => i.Key).ToDictionary(k => k.Key, v => v.Value);
        }
        #endregion

        #region getPlaylist 
        async ValueTask<JArray> getPlaylist(int season_id)
        {
            var root = await seasonObject(season_id);
            if (root == null)
                return null;

            return root.Value<JArray>("playlist");
        }
        #endregion
    }
}
