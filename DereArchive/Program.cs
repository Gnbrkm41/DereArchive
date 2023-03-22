// See https://aka.ms/new-console-template for more information
using HtmlAgilityPack;

using SwfLib;
using SwfLib.Actions;
using SwfLib.Tags.ActionsTags;

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Web;

// Configuration variables
try
{
    Configuration config = JsonSerializer.Deserialize<Configuration>(File.ReadAllText(@"configuration.json"), new JsonSerializerOptions(JsonSerializerDefaults.Web));

    string WorkingPath = config.WorkingPath;
    string token = config.Token;
    string pre = config.Pre;
    var idolsToArchive = config.Idols ?? Array.Empty<Idol>();
    var commusToArchive = config.Commus ?? Array.Empty<Commu>();

    // Code starts here

    int workingPathDepth = WorkingPath.Split(new[] { "\\", "/" }, StringSplitOptions.RemoveEmptyEntries).Length;

    CookieContainer cookies = new();
    cookies.Add(new Cookie("x-mbga-check-cookie", "1", "/", ".sp.pf.mbga.jp"));
    cookies.Add(new Cookie("sp_mbga_sid_12008305", token, "/", "sp.pf.mbga.jp"));
    cookies.Add(new Cookie("PRE", pre, "/", ".mbga.jp"));

    HttpClientHandler handler = new() { UseCookies = true, CookieContainer = cookies };
    HttpClient client = new(handler);
    client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Linux; Android 4.4.2; Nexus 4 Build/KOT49H) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/34.0.1847.114 Mobile Safari/537.36");

    List<string> SkipList = new();

    string[] LinkPrefixesToSkip =
    {
    "http://sp.mbga.jp/_grp_view",
    "http://sp.pf.mbga.jp/12008305/?guid=ON&amp;url=http%3A%2F%2Fmobamas.net%2Fidolmaster%2Fgame_error%3Fl_frm%3DSmart_phone_flash_convert_1",
};

    string[] LinkSuffixesToSkip =
    {
    "?_from=globalfooter",
    "_sign_effect"
};

    string[] LinkMatchesToSkip =
    {
    "http://mobamas.net/idolmaster/",
    "http://mobamas.net/idolmaster/smart_phone_flash/playerCash/",
    "http://sp.pf-img-a.mbga.jp/12008305/?url=http%3A%2F%2Fmobamas.net%2Fidolmaster%2Fimage_sp%2Fui%2Fsprite%2Fmypage%2Fproduction%2Fbutten_new.png%3F",
    "http://sp.pf-img-a.mbga.jp/12008305/?url=http%3A%2F%2Fmobamas.net%2Fidolmaster%2Fimage_sp%2Fui%2Fsprite%2Fmypage%2Fbg_tab_line.png%3F",
    "http://sp.pf.mbga.jp/12008305/?guid=ON"
};

    string[] LinkContainsToSkip =
    {
    "__hash_card_id__",
    "_frm%3DIdol_gallery_idol_detail_1",
    "_frm%3DIdol_gallery_idol_detail_2",
    "_frm%3DIdol_story_movie_play_1",
    "_frm%3DCampaign_present_redirect_flash_idol_comment_replay_"
};

    foreach (Idol idol in idolsToArchive)
    {
        await ArchiveGallery(idol.IdolName, idol.GalleryUrl);
    }

    foreach (Commu commu in commusToArchive)
    {
        // Create a directory in WorkingPath/idols/etc/episodes/commuName.
        // This is so that the absolute path match up even after moving it into more specific idol's folder
        FileInfo info = new FileInfo(Path.Join(WorkingPath, "idols", "etc", "episodes", commu.CommuName, "index.html"));
        info.Directory?.Create();
        
        using Stream stream = await client.GetStreamAsync(commu.CommuUrl);
        FileStream fs = info.Create();
        stream.CopyTo(fs);
        fs.Close();
        await HandleEpisodeHtml(info);
    }

    if (!string.IsNullOrWhiteSpace(config.PuchiProfileName))
    {
        await ArchivePuchi(config.PuchiProfileName);
    }

    File.Copy(@"pex-1.2.0.js", Path.Join(WorkingPath, "idolmaster", "js", "pex-1.2.0.js"), true);
    File.Copy(@"pex-1.2.0-kr.js", Path.Join(WorkingPath, "idolmaster", "js", "pex-1.2.0-kr.js"), true);

    // # 아이돌마스터 신데렐라 걸즈 아이돌 갤러리 아카이브
    // ## 보존된 아이돌 갤러리 목록
    // * [이름...](idols/(unique name))
    // ## 보존된 기타 커뮤 목록
    // * [이름...](etc/commu/(unique name))
    // ## 푸치 갤러리 목록
    // * [이름...](etc/(unique name))
    Console.WriteLine("Archiving complete");
    Dictionary<(string Type, string UniqueName), (string CustomName, string Type, string UniqueName)> dic = new();
    if (File.Exists(Path.Join(WorkingPath, "index.md")))
    {
        string text = File.ReadAllText(Path.Join(WorkingPath, "index.md"));
        foreach (Match match in Regex.Matches(text, @"\* \[(?<customName>[^\]]+)\]\((?<type>idols|etc\/commu|etc\/puchi)\/(?<uniqueName>.*)\)"))
        {
            string customName = match.Groups["customName"].Value;
            string type = match.Groups["type"].Value;
            string uniqueName = match.Groups["uniqueName"].Value;

            dic[(type, uniqueName)] = (customName, type, uniqueName);
        }
    }

    foreach (Idol idol in idolsToArchive)
    {
        dic[("idols", idol.IdolName)] = (idol.IdolName, "idols", idol.IdolName);
    }

    foreach (Commu commu in commusToArchive)
    {
        dic[("etc/commu", commu.CommuName)] = (commu.CommuName, "etc/commu", commu.CommuName);
    }

    if (!string.IsNullOrWhiteSpace(config.PuchiProfileName))
    {
        dic[("etc/puchi", config.PuchiProfileName)] = (config.PuchiProfileName, "etc", config.PuchiProfileName);
    }

    {
        StringBuilder sb = new();
        sb.AppendLine("# 아이돌마스터 신데렐라 걸즈 아이돌 갤러리 아카이브");

        var groups = dic.GroupBy(x => x.Key.Type, x => x.Value);
        
        foreach (var group in groups)
        {
            var title = group.Key switch
            {
                "etc/puchi" => "## 보존된 푸치 프로필 목록",
                "etc/commu" => "## 보존된 기타 커뮤 목록",
                "idols" => "## 보존된 아이돌 갤러리 목록",
                _ => null
            };

            if (title == null) 
            {
                Console.WriteLine($"Omitting unknown type of {group.Key}");
                continue;
            }

            sb.AppendLine(title);
            foreach (var entry in group)
            {
                sb.AppendLine($"* [{entry.CustomName}]({entry.Type}/{entry.UniqueName})");
            }
            
        }
        File.WriteAllText(Path.Join(WorkingPath, "index.md"), sb.ToString());
    }

    async Task ArchiveGallery(string idolName, string galleryLink)
    {
        Console.WriteLine($"Start archiving of {idolName}");
        var response = await client.GetAsync(new Uri(galleryLink));
        FileInfo info = new FileInfo(Path.Join(WorkingPath, "idols", idolName, "index.html"));
        info.Directory?.Create();
        FileStream fs = info.Create();
        response.Content.ReadAsStream().CopyTo(fs);
        fs.Close();

        HtmlDocument doc = new();
        doc.Load(info.FullName);

        doc.DocumentNode.Descendants("div").First(x => x.Attributes["id"]?.Value == "mbga-pf-footer")
            .Remove();

        doc.Save(info.FullName);
        await DownloadEpisodes(info.FullName, idolName);
        await MatchDownloadAndReplace(info.FullName, info.FullName, "");
    }

    string EscapePattern(string str) => str.Replace("(", "\\(").Replace(")", "\\)").Replace("{", "\\{").Replace("}", "\\}").Replace(".", "\\.").Replace("/", "\\/").Replace("?", "\\?").Replace("$", "\\$").Replace("\r\n", "\n");


    async Task ArchivePuchi(string folderName)
    {
        const string puchiProfilePage = "https://sp.pf.mbga.jp/12008305/?guid=ON&url=http%3A%2F%2Fmobamas.net%2Fidolmaster%2Fpetit_cg%3Fview_page%3D4";

        Console.WriteLine("Start archiving of Puchi profile");
        var responseStream = await client.GetStreamAsync(puchiProfilePage);
        FileInfo info = new(Path.Join(WorkingPath, "etc", "puchi", folderName, "index.html"));
        info.Directory?.Create();
        FileStream fs = info.Create();
        responseStream.CopyTo(fs);
        fs.Close();

        HtmlDocument doc = new();
        doc.Load(info.FullName);

        doc.DocumentNode.Descendants("div").First(x => x.Attributes["id"]?.Value == "mbga-pf-footer")
            .Remove();

        doc.Save(info.FullName);

        await MatchDownloadAndReplace(info.FullName, info.FullName, "");
        
        StringBuilder sb = new(File.ReadAllText(info.FullName));
        // TODO:
        // 2. 'type' is 0
        // 3. 'deck position' is 1~3
        // 4. Send a post message, save it as coordinateList-{deckPosition}.json, save alongside the puchi index

        {
            string coordUrl = "https://sp.pf.mbga.jp/12008305/?guid=ON&amp;url=http%3A%2F%2Fmobamas.net%2Fidolmaster%2Fpetit_cg%2Fajax_coordinate_list%3Fl_frm%3DPetit_cg_index_1";
            for (int i = 1; i <= 3; i++) {
                HttpRequestMessage coordRequest = new(HttpMethod.Post, coordUrl);
                coordRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>() { ["type"] = "0", ["deck_position"] = i.ToString() });
                var coordResponse = await client.SendAsync(coordRequest);
                var coordStream = await coordResponse.EnsureSuccessStatusCode().Content.ReadAsStreamAsync();
                FileInfo coordFile = new(Path.Join(WorkingPath, "etc", "puchi", folderName, $"coordinateList-{i}.json"));
                using FileStream coordFileStream = coordFile.Create();
                coordStream.CopyTo(coordFileStream);
            }
            string coordCodeReplace = """
            let deckPosition = chara()
            let url = `./coordinateList-${deckPosition}.json`
            $.ajax({
            type: 'GET',
            url: url,
            dataType: 'json',
            })
            """;
            string coordCodePattern = """
            $.ajax({
            type: 'POST',
            url: 'https://sp.pf.mbga.jp/12008305/?guid=ON&amp;url=http%3A%2F%2Fmobamas.net%2Fidolmaster%2Fpetit_cg%2Fajax_coordinate_list%3Fl_frm%3DPetit_cg_index_1%26rnd%3D\d+',
            dataType: 'json',
            data: {
            "type": type,
            "deck_position": chara()
            }
            })
            """;
            var coordMatch = Regex.Match(sb.ToString(), EscapePattern(coordCodePattern));
            if (coordMatch.Success) {
                sb.Replace(coordMatch.Groups[0].Value, coordCodeReplace);
            }
        }
        {
            string url = "https://sp.pf.mbga.jp/12008305/?guid=ON&amp;url=http%3A%2F%2Fmobamas.net%2Fidolmaster%2Fpetit_cg%2Fajax_idol_status%3Fl_frm%3DPetit_cg_index_1";
            for (int i = 1; i <= 3; i++) {
                HttpRequestMessage request = new(HttpMethod.Post, url);
                request.Content = new FormUrlEncodedContent(new Dictionary<string, string>() { ["deck_position"] = i.ToString() });
                var response = await client.SendAsync(request);
                var stream = await response.EnsureSuccessStatusCode().Content.ReadAsStreamAsync();
                FileInfo fileInfo = new(Path.Join(WorkingPath, "etc", "puchi", folderName, $"idolStatus-{i}.json"));
                using FileStream fileStream = fileInfo.Create();
                stream.CopyTo(fileStream);
            }
            string replace = """
            let deckPosition = chara()
            let url = `./idolStatus-${deckPosition}.json`
            $.ajax({
            type: 'GET',
            url: url,
            dataType: 'json',
            })
            """;
            string pattern = """
            $.ajax({
            type: 'POST',
            url: 'https://sp.pf.mbga.jp/12008305/?guid=ON&amp;url=http%3A%2F%2Fmobamas.net%2Fidolmaster%2Fpetit_cg%2Fajax_idol_status%3Fl_frm%3DPetit_cg_index_1%26rnd%3D\d+',
            dataType: 'json',
            data: {
            "deck_position": chara()
            }
            })
            """;
            var match = Regex.Match(sb.ToString(), EscapePattern(pattern));
            if (match.Success) {
                sb.Replace(match.Groups[0].Value, replace);
            }
        }

        // Need to read which characters are there

        var characterDataJson = Regex.Match(sb.ToString(), @"mc = new MakeCharacter\(\$\.parseJSON\('([^']*)'\)\)").Groups[1].Value;
        using JsonDocument jsonDoc = JsonDocument.Parse(characterDataJson);

        var characterIds = jsonDoc.RootElement.EnumerateObject().Select(x => new KeyValuePair<string, string>(x.Name, x.Value.GetProperty("idol_id").GetString())).ToArray();

        {
            string url = "https://sp.pf.mbga.jp/12008305/?guid=ON&amp;url=http%3A%2F%2Fmobamas.net%2Fidolmaster%2Fpetit_cg%2Fajax_episode_list%3Fl_frm%3DPetit_cg_index_1";
            foreach ((string position, string characterId) in characterIds) {
                HttpRequestMessage request = new(HttpMethod.Post, url);
                request.Content = new FormUrlEncodedContent(new Dictionary<string, string>() { ["idol_id"] = characterId });
                var response = await client.SendAsync(request);
                var stream = await response.EnsureSuccessStatusCode().Content.ReadAsStreamAsync();
                FileInfo fileInfo = new(Path.Join(WorkingPath, "etc", "puchi", folderName, $"episodeList-{characterId}.json"));
                using FileStream fileStream = fileInfo.Create();
                stream.CopyTo(fileStream);
            }
            string replace = """
            let url = `./episodeList-${idol_id}.json`
            $.ajax({
            type: 'GET',
            url: url,
            dataType: 'json',
            })
            """;
            string pattern = """
            $.ajax({
            type: 'POST',
            url: 'https://sp.pf.mbga.jp/12008305/?guid=ON&amp;url=http%3A%2F%2Fmobamas.net%2Fidolmaster%2Fpetit_cg%2Fajax_episode_list%3Fl_frm%3DPetit_cg_index_1%26rnd%3D\d+',
            dataType: 'json',
            data: {
            'idol_id': idol_id
            }
            })
            """;
            var match = Regex.Match(sb.ToString(), EscapePattern(pattern));
            if (match.Success) {
                sb.Replace(match.Groups[0].Value, replace);
            }
        }

        {
            string url = "https://sp.pf.mbga.jp/12008305/?guid=ON&amp;url=http%3A%2F%2Fmobamas.net%2Fidolmaster%2Fpetit_cg%2Fajax_idol_comment_list%3Fl_frm%3DPetit_cg_index_1";
            for (int i = 1; i <= 3; i++) {
                HttpRequestMessage request = new(HttpMethod.Post, url);
                request.Content = new FormUrlEncodedContent(new Dictionary<string, string>() { ["deck_position"] = i.ToString() });
                var response = await client.SendAsync(request);
                var stream = await response.EnsureSuccessStatusCode().Content.ReadAsStreamAsync();
                FileInfo fileInfo = new(Path.Join(WorkingPath, "etc", "puchi", folderName, $"idolCommentList-{i}.json"));
                using FileStream fileStream = fileInfo.Create();
                stream.CopyTo(fileStream);
            }
            string replace = """
            let deckPosition = chara()
            let url = `./idolCommentList-${deckPosition}.json`
            $.ajax({
            type: 'GET',
            url: url,
            dataType: 'json',
            })
            """;
            string pattern = """
            $.ajax({
            type: 'POST',
            url: 'https://sp.pf.mbga.jp/12008305/?guid=ON&amp;url=http%3A%2F%2Fmobamas.net%2Fidolmaster%2Fpetit_cg%2Fajax_idol_comment_list%3Fl_frm%3DPetit_cg_index_1%26rnd%3D\d+',
            dataType: 'json',
            data: {
            'deck_position': chara()
            }
            })
            """;
            var match = Regex.Match(sb.ToString(), EscapePattern(pattern));
            if (match.Success) {
                sb.Replace(match.Groups[0].Value, replace);
            }
        }

        string voiceFlagCode = """
            function episodeVoiceFlag(data){
            if(data.episode_list.length && data.episode_list[0].episode_voice == 1) $('#petitEpisodeVoiceFlg').show();
            else $('#petitEpisodeVoiceFlg').hide();
            }
            """.Replace("\r\n", "\n");
        string voiceFlagReplace = """
            function episodeVoiceFlag(data){
            $('#petitEpisodeVoiceFlg').hide();
            }
            """;
        sb.Replace(voiceFlagCode, voiceFlagReplace);

        string coordListPopulateCode = "createCoordinateList(data.accessory_list, data.take_off_info);";
        string coordListPopulateReplace = "//createCoordinateList(data.accessory_list, data.take_off_info);";

        sb.Replace(coordListPopulateCode, coordListPopulateReplace);

        foreach ((string position, string characterId) in characterIds)
        {
            string content = File.ReadAllText(Path.Join(WorkingPath, "etc", "puchi", folderName, $"episodeList-{characterId}.json"));
            using JsonDocument episodeDoc = JsonDocument.Parse(content);
            var episodeList = episodeDoc.RootElement.GetProperty("episode_list").EnumerateArray().Select(x => (EpisodeVoice: x.GetProperty("episode_voice").GetString()!, EpisodeId: x.GetProperty("episode_id").GetString()!));
            bool hasVoice = episodeList.FirstOrDefault().EpisodeVoice == "1";
            foreach (var episode in episodeList) {
                string url = $"https://sp.pf.mbga.jp/12008305/?guid=ON&amp;url=http%3A%2F%2Fmobamas.net%2Fidolmaster%2Fpetit_cg%2Fpetit_show_episode%2F{characterId}%2F{episode.EpisodeId}%2Fcoordinate_idol%2F{position}%3Fvoice_flag%3D{(hasVoice ? "1" : "0")}%26l_frm%3DPetit_cg_index_1";
                var stream = await client.GetStreamAsync(url);
                FileInfo fileInfo = new(Path.Join(WorkingPath, "etc", "puchi", folderName, characterId, episode.EpisodeId, "index.html"));
                fileInfo.Directory?.Create();
                FileStream fileStream = fileInfo.Create();
                stream.CopyTo(fileStream);
                fileStream.Close();

                await HandleEpisodeHtml(fileInfo);
            }
        }
        string episodePattern = """
        var baseUrl = ('https://sp.pf.mbga.jp/12008305/?guid=ON&amp;url=http%3A%2F%2Fmobamas.net%2Fidolmaster%2Fpetit_cg%2Fpetit_show_episode%2F__idol_ld__%2F__episode__%2Fcoordinate_idol%2F__position__%3Fvoice_flag%3D0%26l_frm%3DPetit_cg_index_1%26rnd%3D\d+');
        baseUrl = baseUrl.replace('__idol_ld__', idol_id);
        baseUrl = baseUrl.replace('__position__', chara());
        """;
        string episodeReplace = """
        var baseUrl = ('./__idol_ld__/__episode__');
        baseUrl = baseUrl.replace('__idol_ld__', idol_id);
        """;

        var episodeMatch = Regex.Match(sb.ToString(), EscapePattern(episodePattern));
        if (episodeMatch.Success) {
            sb.Replace(episodeMatch.Groups[0].Value, episodeReplace);
        }

        File.WriteAllText(info.FullName, sb.ToString());
    }

    async Task MatchDownloadAndReplace(string originalFilePath, string outputFilePath, string origin)
    {
        Console.WriteLine($"Recursively archiving contents of {originalFilePath}");
        int currentDepth = originalFilePath.Split(new[] { "\\", "/" }, StringSplitOptions.RemoveEmptyEntries).Skip(workingPathDepth).Count() - 1;
        string pathPrefix = GetPathPrefix(currentDepth);
        string text = File.ReadAllText(originalFilePath);
        StringBuilder sb = new(text);
        var matches = originalFilePath.EndsWith(".css") ? Regex.Matches(text, @"(?<!@import )(?<opening>url(?<openParen>\()((?<openQuote>"")|(?<openSingle>'))?)(?<content>.*?)((?<closeQuote-openQuote>"")|(?<closeSingle-openSingle>'))?(?<closeParen-openParen>\))") :
            Regex.Matches(text, @"(?<opening>(?<openSingle>')|(?<openDouble>"")|(?<openParen>\())(?<content>http.*?)(?<closing>(?<closeSingle-openSingle>')|(?<closeDouble-openDouble>"")|(?<closeParen-openParen>\)))");

        Console.WriteLine($"Match count: {matches.Count}");

        foreach (Match match in matches.DistinctBy(x => x.Value))
        {
            //Console.WriteLine(match.Value);

            string rawUrl = match.Groups["content"].Value;
            string url = rawUrl;
            string? filePath = null;
            bool isAbsoluteLocalPath = rawUrl.StartsWith('/');
            Uri? outerUrl = null;
            bool isJsEscaped = rawUrl.Contains(@"\/");

            // Skipping logic
            if (LinkPrefixesToSkip.Any(x => url.StartsWith(x))
                || LinkSuffixesToSkip.Any(x => url.EndsWith(x))
                || LinkMatchesToSkip.Any(x => url == x)
                || LinkContainsToSkip.Any(x => url.Contains(x)))
            {
                Console.WriteLine($"Skipping {url} prematurely");
                continue;
            }

            if (isAbsoluteLocalPath)
            {
                // Convert those to local paths
                url = "http://" + origin + rawUrl;

                var paramIndex = rawUrl.IndexOf('?');
                filePath = paramIndex >= 0 ? rawUrl[..paramIndex] : rawUrl;
            }
            else
            {
                if (isJsEscaped)
                {
                    url = rawUrl.Replace("\\/", "/");
                }
                outerUrl = new Uri(url);
                if (url.StartsWith("https://sp.pf-img-a.mbga.jp") || url.StartsWith("https://sp.pf.mbga.jp"))
                {
                    var decodedQuery = HttpUtility.HtmlDecode(outerUrl.Query);
                    var queryUrlCollection = HttpUtility.ParseQueryString(decodedQuery);
                    if (queryUrlCollection["url"] != null)
                    {
                        var innerUrl = new Uri(queryUrlCollection["url"]);
                        filePath = innerUrl.LocalPath;
                    }
                    else
                    {
                        filePath = outerUrl.LocalPath;
                    }
                }
                else
                {
                    filePath = outerUrl.LocalPath;
                }
            }

            FileInfo file = new(Path.Join(WorkingPath, filePath));

            if (file.Exists)
            {
                Console.WriteLine($"Not downloading {filePath} as it exists");
            }
            else
            {
                var response = await client.GetAsync(url);
                if (!response.IsSuccessStatusCode || response.Content.Headers.ContentType.MediaType is "text/html" or "application/x-shockwave-flash")
                {
                    Console.WriteLine($"Skipping {url} ({response.StatusCode}) {response.Content.Headers.ContentType.MediaType}");
                    SkipList.Add(url);
                    continue;
                }
                file.Directory?.Create();
                var fs = file.Create();
                response.Content.ReadAsStream().CopyTo(fs);
                fs.Close();

                if (response.Content.Headers.ContentType.MediaType == "text/css")
                {
                    await MatchDownloadAndReplace(file.FullName, file.FullName, outerUrl.Host);
                }
            }

            var replaceString = match.Groups["opening"].Value switch
            {
                "\"" => $"\"{pathPrefix + filePath}\"",
                "'" => $"'{pathPrefix + filePath}'",
                "(" => $"({pathPrefix + filePath})",
                "url(\"" => $"url(\"{pathPrefix + filePath}\")",
                "url(" => $"url({pathPrefix + filePath})",
                "url('" => $"url('{pathPrefix + filePath}')"
            };

            if (isJsEscaped)
            {
                replaceString = replaceString.Replace("/", "\\/");
            }

            var str = sb.Replace(match.Value, replaceString).ToString();
            File.WriteAllText(outputFilePath, str);
        }
    }

    async Task DownloadEpisodes(string file, string idolName)
    {
        Console.WriteLine($"Downloading episodes of {idolName}");
        HtmlDocument doc = new();
        doc.Load(file);

        var idolInitializationScriptNode = (HtmlTextNode)doc.DocumentNode.Descendants("script")
            .First(x => x.InnerText.Contains("var idol"))
            .ChildNodes[0];
        var idolInitializationScript = idolInitializationScriptNode.InnerHtml;

        string detailListJson = Regex.Match(idolInitializationScript, @"\nidol\.detail_list = (.*);").Groups[1].Value;
        string storyListJson = Regex.Match(idolInitializationScript, @"\nidol\.idol_story_list = (.*);").Groups[1].Value;
        var idolDetails = JsonSerializer.Deserialize<IdolDetail[]>(detailListJson);
        var storyList = JsonSerializer.Deserialize<IdolStory[]>(storyListJson);
        Console.WriteLine($"Expecting {storyList.Length * 2} episodes");

        string idolHash = idolDetails![0].Data.HashCardId;

        var annivMessages = doc.DocumentNode.Descendants("form").Where(x => x.Attributes["class"]?.Value.StartsWith("form_check_idol_comment") == true);

        foreach (var anniv in annivMessages)
        {
            var url = anniv.Attributes["action"].Value;
            var annivType = anniv.Attributes["class"].Value.Contains("3rd") ? "3rd" : "5th";
            Console.WriteLine($"Downloading anniversary message: {annivType}");
            var formContent = anniv.Descendants("input").Where(x => x.Attributes["name"] != null && x.Attributes["value"] != null)
                .Select(x =>
                {
                    string name = x.Attributes["name"].Value;
                    string value = x.Attributes["value"].Value;

                    value = name switch
                    {
                        "card_hash" or "select_card_hash" => idolHash,
                        "redirect_url" => $"idol_gallery/idol_detail/{idolHash}",
                        _ => value
                    };

                    return new KeyValuePair<string, string>(name, value);
                });

            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new FormUrlEncodedContent(formContent)
            };
            var annivResponse = await client.SendAsync(request);

            FileInfo annivIndex = new(Path.Join(WorkingPath, "idols", idolName, "anniversary", annivType, "index.html"));
            annivIndex.Directory?.Create();
            FileStream fs2 = annivIndex.Create();
            annivResponse.Content.ReadAsStream().CopyTo(fs2);
            fs2.Close();

            await HandleEpisodeHtml(annivIndex);

            anniv.Attributes["action"].Value = $"anniversary/{annivType}";
            anniv.Attributes["method"].Value = $"get";
            foreach (var input in anniv.Descendants("input").ToArray())
            {
                input.Remove();
            }
        }

        foreach (var story in storyList)
        {
            for (int i = 0; i < story.FlashPaths.Length; i++)
            {
                Console.WriteLine($"Downloading episode {story.StoryId}_{i + 1}");
                if (story.OpenFlags[i] != "1")
                {
                    Console.WriteLine("Skipping episode (not open)");
                    continue;
                }

                var request = new HttpRequestMessage(HttpMethod.Post, story.FlashPaths[i].Replace("__hash_card_id__", idolHash));

                if (story.MovieNameVoices != null)
                {
                    var kvp = new[] { new KeyValuePair<string, string>("voice", story.VoiceEnables[i]) };
                    request.Content = new FormUrlEncodedContent(kvp);
                }
                var response = await client.SendAsync(request);
                FileInfo info = new(Path.Join(WorkingPath, "idols", idolName, "episodes", $"{story.StoryId}_{i + 1}", "index.html"));
                info.Directory?.Create();
                FileStream fs = info.Create();
                response.Content.ReadAsStream().CopyTo(fs);
                fs.Close();
                await HandleEpisodeHtml(info);

                story.FlashPaths[i] = $"episodes/{story.StoryId}_{i + 1}";
            }
        }

        var replacedIdolDetails = JsonSerializer.Serialize(storyList, new JsonSerializerOptions() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

        idolInitializationScriptNode.Text = Regex.Replace(idolInitializationScript, @"\nidol\.idol_story_list = (.*);", "\nidol.idol_story_list = " + replacedIdolDetails + ";");

        (doc.DocumentNode.Descendants("script")
            .First(x => x.Attributes["type"]?.Value == "text/template" && x.Attributes["id"]?.Value == "story-template")
            .ChildNodes[0] as HtmlTextNode)!
            .Text = File.ReadAllText(@"template.txt");

        doc.Save(file);
    }

    async Task HandleEpisodeHtml(FileInfo info)
    {
        int currentDepth = info.FullName.Split(new[] { "\\", "/" }, StringSplitOptions.RemoveEmptyEntries).Skip(workingPathDepth).Count() - 1;
        string pathPrefix = GetPathPrefix(currentDepth);
        string file = File.ReadAllText(info.FullName);

        if (file.Contains("pex-1.2.0.js"))
        {
            Console.WriteLine($"{info.FullName} is flash episode");
            var flashPath = Regex.Match(file, @"new Pex\('([^']*)',");
            var response = await client.GetAsync(flashPath.Groups[1].Value);
            FileInfo flashFile = new(Path.Join(info.Directory.FullName, "flash.swf"));
            FileStream fs = flashFile.Create();
            response.Content.ReadAsStream().CopyTo(fs);
            fs.Close();
            file = file.Replace(flashPath.Groups[1].Value, "./flash.swf");
            File.WriteAllText(info.FullName, file);
            await HandleFlashFile(flashFile.FullName);
        }
        else if (file.Contains("window.file_name = "))
        {
            // Html based episode
            var fileName = Regex.Match(file, @"window\.file_name = ""(.*?)"";").Groups[1].Value;
            HtmlDocument doc = new();
            doc.LoadHtml(file);

            if (fileName == "story_generator_n1")
            {
                Console.WriteLine($"{info.FullName} is html episode (story_generator)");
                var scriptSource = doc.DocumentNode.Descendants("script").Select(x => x.GetAttributeValue("src", null)).First(x => x?.Contains(fileName.TrimStart('_')) == true);
                var script = await client.GetStringAsync(scriptSource);
                var manifest = Regex.Matches(script, @"(""|')(?<file>(image(s|_sp)|sounds)\/.*?)('|"")").Select(x => x.Groups["file"].Value);

                var charaImages = Regex.Matches(file, @"\] += '(?<file>image.*?)';").Select(x => x.Groups["file"].Value);
                var bgImages = Regex.Matches(file, @"window\.bg_replace_images\['(?<id>.*)'\] = '(?<file>.*?)';").Select(x => x.Groups["file"].Value);

                var files = manifest.Select(x => "image_sp/cjs/genechara/" + x)
                    .Concat(charaImages)
                    .Concat(bgImages.Select(x => $"image_sp/event_flash/story/bg/bg{x}_wide.jpg"))
                    .Distinct();

                foreach (var f in files)
                {
                    var response = await client.GetAsync("https://sp.pf-img-a.mbga.jp/12008305/" + "?url=" + WebUtility.UrlEncode("http://mobamas.net/idolmaster/" + f));
                    FileInfo info2 = new(Path.Join(WorkingPath, Regex.Replace(f, @"\?.*", "")));
                    info2.Directory?.Create();
                    FileStream fs = info2.Create();
                    response.Content.ReadAsStream().CopyTo(fs);
                    fs.Close();
                }

                file = file.Replace("image_server = \"https://sp.pf-img-a.mbga.jp/12008305/\";", $"image_server = \"{pathPrefix}/\";")
                    .Replace("'src': window.image_server + '?url=' + encodeURIComponent(window.base_url + _img_path),", "'src': window.image_server + _img_path,");
                file = Regex.Replace(file, @"im_cjs.jump_url = '(https:\/\/sp\.pf.*)';", "im_cjs.jump_url = '../..';");
                File.WriteAllText(info.FullName, file);
            }
            else
            {
                Console.WriteLine($"{info.FullName} is html episode");
                var dirName = Regex.Match(file, @"window\.dir_name = ""(.*?)"";").Groups[1].Value;
                var scriptSource = doc.DocumentNode.Descendants("script").Select(x => x.GetAttributeValue("src", null)).First(x => x?.Contains(fileName.TrimStart('_') + ".js") == true);
                var script = await client.GetStringAsync(scriptSource);
                var manifest = Regex.Matches(script, @"(""|')(?<file>image(s|_sp)\/.*?)('|"")").Select(x => x.Groups["file"].Value);

                var files = manifest.Select(x => dirName + x)
                    .Distinct();

                foreach (var f in files)
                {
                    var response = await client.GetAsync("https://sp.pf-img-a.mbga.jp/12008305/" + "?url=" + WebUtility.UrlEncode("http://mobamas.net/idolmaster/" + f));
                    FileInfo info2 = new(Path.Join(WorkingPath, Regex.Replace(f, @"\?.*", "")));
                    info2.Directory?.Create();
                    FileStream fs = info2.Create();
                    response.Content.ReadAsStream().CopyTo(fs);
                    fs.Close();
                }

                file = file.Replace("image_server = \"https://sp.pf-img-a.mbga.jp/12008305/\";", $"image_server = \"{pathPrefix}/\";")
                    .Replace("'src': window.image_server + '?url=' + encodeURIComponent(window.base_url + window.dir_name + lib.properties.manifest[i].src),", "'src': window.image_server + window.dir_name + lib.properties.manifest[i].src,");
                file = Regex.Replace(file, @"im_cjs.jump_url = '(https:\/\/sp\.pf.*)';", "im_cjs.jump_url = '../..';");
                File.WriteAllText(info.FullName, file);
            }
        }

        await MatchDownloadAndReplace(info.FullName, info.FullName, "");

    }
    string GetPathPrefix(int currentDepth)
    {
        if (currentDepth == 0)
        {
            return ".";
        }
        StringBuilder builder = new();

        builder.Append("..");

        for (int i = 1; i < currentDepth; i++)
        {
            builder.Append("/..");
        }

        return builder.ToString();
    }

    async Task HandleFlashFile(string file)
    {
        Console.WriteLine($"Processing flash file {file}");
        int currentDepth = file.Split(new[] { "\\", "/" }, StringSplitOptions.RemoveEmptyEntries).Skip(workingPathDepth).Count() - 1;
        string pathPrefix = GetPathPrefix(currentDepth);
        using FileStream fs = File.OpenRead(file);
        var swf = SwfFile.ReadFrom(fs);

        DoActionTag declarationAction = (DoActionTag)swf.Tags.Where(x => x is DoActionTag)
            .First(x => ((DoActionTag)x).ActionRecords.Any(x => x is ActionPush push && push.Items[0].String.StartsWith("url")));

        ActionPush returnUrlPush = (ActionPush)declarationAction.ActionRecords.First(x => x is ActionPush push && push.Items[0].String.Contains("sp.pf"));
        ActionPushItem returnUrlPushItem = returnUrlPush.Items[0];
        returnUrlPushItem.String = "../..";
        returnUrlPush.Items[0] = returnUrlPushItem;

        var stuffs = declarationAction.ActionRecords.Where(x => x is ActionPush push && push.Items[0].String.Contains("resource"));

        foreach (ActionPush push in stuffs)
        {
            ActionPushItem pushItem = push.Items[0];
            Uri url = new(pushItem.String);
            string localPath = url.LocalPath;

            var response = await client.GetAsync(url);
            FileInfo info = new(Path.Join(WorkingPath, localPath));
            info.Directory?.Create();
            using FileStream fs2 = info.Create();
            response.Content.ReadAsStream().CopyTo(fs2);

            pushItem.String = $"{pathPrefix}{localPath}";
            push.Items[0] = pushItem;
        }

        fs.Close();
        swf.FileInfo.Version = 6;
        using FileStream destination = File.Create(file);
        swf.WriteTo(destination);
    }
}
catch (Exception ex)
{
#if DEBUG
    throw;
#endif
    Console.WriteLine($"An exception occurred: {ex}");
}

Console.WriteLine("아무 키나 눌러 종료");
Console.ReadKey(true);

record IdolStory([property: JsonConverter(typeof(CustomStringArrayReader))] [property: JsonPropertyName("flash_path")] string[] FlashPaths,
    [property: JsonConverter(typeof(CustomStringArrayReader))] [property: JsonPropertyName("icon")] string[] Icons,
    [property: JsonConverter(typeof(CustomStringArrayReader))] [property: JsonPropertyName("movie_name_voice")] string[] MovieNameVoices,
    [property: JsonConverter(typeof(CustomStringArrayReader))][property: JsonPropertyName("movie_name")] string[] MovieNames,
    [property: JsonConverter(typeof(CustomStringArrayReader))] [property: JsonPropertyName("open_flag")] string[] OpenFlags,
    [property: JsonPropertyName("story_id")] string StoryId,
    [property: JsonPropertyName("story_title")] string StoryTitle,
    [property: JsonConverter(typeof(CustomStringArrayReader))] [property: JsonPropertyName("voice_enable")] string[] VoiceEnables,
    [property: JsonConverter(typeof(CustomStringArrayReader))] [property: JsonPropertyName("voice_url")] string[] VoiceUrls);

record IdolDetail([property: JsonPropertyName("data")] Data Data);
record Data([property: JsonPropertyName("hash_card_id")] string HashCardId);
record Configuration(string WorkingPath, string Token, string Pre, Idol[]? Idols, Commu[]? Commus, string PuchiProfileName);
record Idol(string IdolName, string GalleryUrl);
record Commu(string CommuName, string CommuUrl);
public class CustomStringArrayReader : JsonConverter<string?[]>
{
    public override string?[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        List<string?> list = new();
        // Check if this is an object thing or an array
        if (reader.TokenType == JsonTokenType.StartArray)
        {
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (reader.TokenType != JsonTokenType.String)
                {
                    throw new JsonException("Unexpected token");
                }

                list.Add(reader.GetString());
            }
            return list.ToArray();
        }
        else if (reader.TokenType == JsonTokenType.StartObject)
        {
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    throw new JsonException("Unexpected token");
                }

                if (!reader.Read() || reader.TokenType != JsonTokenType.String)
                {
                    throw new JsonException("Unexpected token");
                }

                list.Add(reader.GetString());
            }
            return list.ToArray();
        }
        else
        {
            throw new JsonException("Unexpected token");
        }
    }

    public override void Write(Utf8JsonWriter writer, string?[] value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var item in value)
        {
            writer.WriteStringValue(item);
        }
        writer.WriteEndArray();
    }
}
