using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;

namespace RadioLabDownloader
{
    class Program
    {
        private static string PrevPercent = string.Empty;

        static void Main(string[] args)
        {
            try
            {
                int startPage = 1;
                int currentPage = startPage;

                List<EpisodeDetails> allEpisodes = new List<EpisodeDetails>();

                if (File.Exists("episodeManifest.json"))
                {
                    string dataString = File.ReadAllText("episodeManifest.json");
                    allEpisodes = Newtonsoft.Json.JsonConvert.DeserializeObject<List<EpisodeDetails>>(dataString);
                }

                Console.WriteLine("Counting episodes (Please wait)...");
                List<string> allEpisodePages = new List<string>();
                while (true)
                {
                    List<string> episodeLinks = GetPodcastPageLinks(currentPage++);
                    if (episodeLinks.Count == 0)
                    {
                        break;
                    }
                    allEpisodePages.AddRange(episodeLinks);
                }
                Console.Write("Building Episode list... ");
                UpdatePercentage(0);
                int count = 0;

                allEpisodePages = allEpisodePages.FindAll(ep => !allEpisodes.Exists(e => e.EpisodePageLink == ep));

                List<EpisodeDetails> newEpisodes = new List<EpisodeDetails>();

                foreach (string episodePage in allEpisodePages)
                {
                    EpisodeDetails details = GetMp3LinkFromEpisodePage(episodePage);
                    if (details != null)
                    {
                        details.EpisodePageLink = episodePage;
                        newEpisodes.Add(details);
                    }
                    UpdatePercentage((int)Math.Ceiling((100.0 / allEpisodePages.Count) * count++));
                }
                UpdatePercentage(100);

                //make sure the newest episodes stay at the front of the list
                //to ensure the files are numbered correctly
                newEpisodes.AddRange(allEpisodes);
                allEpisodes = newEpisodes;

                string manifestString = Newtonsoft.Json.JsonConvert.SerializeObject(allEpisodes, Newtonsoft.Json.Formatting.Indented);
                File.WriteAllText("episodeManifest.json", manifestString);

                Console.WriteLine();
                Console.WriteLine("Found {0} episodes", allEpisodes.Count);
                Console.WriteLine("Downloading missing episodes");
                //download the episodes in reverse order to start the count from the first episode
                allEpisodes.Reverse();
                DownloadMp3s(allEpisodes);
                Console.WriteLine("Done :D");
            }
            catch(Exception ex)
            {
                Console.WriteLine("There was an error, try running again.");
                Console.WriteLine("Error encountered: " + ex.Message);
            }
            Console.ReadKey();
        }

        private static void UpdatePercentage(int Percentage)
        {
            Console.CursorLeft = Console.CursorLeft - PrevPercent.Length;
            for (int i = 0;i < PrevPercent.Length;++i)
            {
                Console.Write(" ");
            }
            Console.CursorLeft = Console.CursorLeft - PrevPercent.Length;
            PrevPercent = Percentage + "%";
            Console.Write(PrevPercent);
        }

        private static void DownloadMp3s(List<EpisodeDetails> Episodes)
        {
            if(!Directory.Exists("Episodes"))
            {
                Directory.CreateDirectory("Episodes");
            }
            int count = 0;
            foreach(EpisodeDetails episode in Episodes)
            {
                string epName = count++.ToString() + "-" + episode.FileName;
                string filePath = "Episodes\\" + epName;
                if (File.Exists(epName))
                {
                    //Console.WriteLine("Skipping: {0}", episode.Name);
                    continue;
                }
                Console.Write("Downloading {0}...", episode.Name);
                byte[] data = GetBytes(episode.Url);
                if(data != null)
                {
                    File.WriteAllBytes(filePath, data);
                    Console.WriteLine(" saved as: {0}", epName);
                }
                else
                {
                    ConsoleColor orignal = Console.ForegroundColor;
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine(" failed");
                    Console.ForegroundColor = orignal;
                }
            }
        }

        private static List<string> GetPodcastPageLinks(int PageNumber)
        {
            string markup = GetMakrkup(string.Format("https://www.wnycstudios.org/podcasts/radiolab/podcasts/{0}", PageNumber));
            string[] markupParts = markup.Split(new string[] { "<section data-test-selector=\"episode-list\"" }, StringSplitOptions.RemoveEmptyEntries);
            if (markupParts.Length != 2)
            {
                //empty page
                return new List<string>();
            }
            string episodeListSection = markupParts[1];
            episodeListSection = episodeListSection.Split(new string[] { "</section>" }, StringSplitOptions.RemoveEmptyEntries)[0];
            string[] articles = episodeListSection.Split(new string[] { "<article" }, StringSplitOptions.RemoveEmptyEntries);

            List<string> links = new List<string>();
            for (int i = 1; i < articles.Length; ++i)
            {
                string article = articles[i];
                string[] articleParts = article.Split("href=\"", StringSplitOptions.RemoveEmptyEntries);
                if (articleParts.Length < 2)
                {
                    Console.WriteLine("Error splitting markup on article, page: {0}", PageNumber);
                    continue;
                }
                string href = articleParts[1];
                href = "https://www.wnycstudios.org" + href.Split("\" id=", StringSplitOptions.RemoveEmptyEntries)[0];
                links.Add(href);
            }

            return links;
        }

        private static EpisodeDetails GetMp3LinkFromEpisodePage(string Url)
        {
            string markup = GetMakrkup(Url);
            string[] markupParts = markup.Split("\" download=\"", StringSplitOptions.RemoveEmptyEntries);
            string[] hrefParts = markupParts[0].Split("href=\"", StringSplitOptions.RemoveEmptyEntries);
            string href = hrefParts[hrefParts.Length - 1];
            if(href.Contains("<") || href.Contains(">"))
            {
                //didn't find a podcast link so this is probably a video or other article
                return null;
            }
            markupParts = markup.Split("<h3 class=\"story__title\">", StringSplitOptions.RemoveEmptyEntries);
            string name = markupParts[1].Split("</h3>", StringSplitOptions.RemoveEmptyEntries)[0];
            name = name.Trim();
            name.Replace("&amp;", "&");
            //only allow numbers, letters in the name
            string fileName = string.Empty;
            bool spaceLast = false;
            foreach(char c in name)
            {
                if((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '&')
                {
                    if (spaceLast && c >= 'a' && c <= 'z')
                    {
                        fileName += (char)((c - 'a') + 'A');
                    }
                    else
                    {
                        fileName += c;
                    }
                    spaceLast = false;
                }
                else if(c == ' ')
                {
                    spaceLast = true;
                }
            }
            fileName += Path.GetExtension(href.Split("?", StringSplitOptions.RemoveEmptyEntries)[0]); //remove query string if there is one
            //Console.WriteLine("{0}: {1}", name, href);
            return new EpisodeDetails() { Url = href, FileName = fileName, Name = name };
        }

        private static string GetMakrkup(string Url)
        {
            using(HttpClient client = new HttpClient())
            {
                return client.GetStringAsync(Url).Result;
            }
        }

        private static byte[] GetBytes(string Url)
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    return client.GetByteArrayAsync(Url).Result;
                }
            }
            catch { }
            return null;
        }

        class EpisodeDetails
        {
            public string Name { get; set; }
            public string FileName { get; set; }
            public string Url { get; set; }
            public string EpisodePageLink { get; set; }
        }
    }
}
