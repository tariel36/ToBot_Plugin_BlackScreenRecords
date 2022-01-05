// The MIT License (MIT)
//
// Copyright (c) 2022 tariel36
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using HtmlAgilityPack;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ToBot.Common.Maintenance;
using ToBot.Common.Maintenance.Logging;
using ToBot.Common.Pocos;
using ToBot.Common.Watchers;
using ToBot.Communication.Commands;
using ToBot.Communication.Messaging.Formatters;
using ToBot.Communication.Messaging.Providers;
using ToBot.Data.Repositories;
using ToBot.Plugin.BlackScreenRecords.Data;
using ToBot.Plugin.GenericScrapperPlugin;
using ToBot.Common.Attributes;

namespace ToBot.Plugin.PluginBlackScreenRecords
{
    public class BlackScreenRecords
        : PluginGenericScrapper<BlackScreenRecordsEntry>
    {
        public const string Prefix = "bsr";

        private const string BaseUrl = "https://blackscreenrecords.com";
        private const string AllReleasesUrlTemplate = "https://blackscreenrecords.com/collections/all-releases?page={0}";
        private const string DistroReleasesUrlTemplate = "https://blackscreenrecords.com/collections/distro?page={0}";
        private const string NbpApiEuroUrl = "http://api.nbp.pl/api/exchangerates/rates/A/EUR?format=json";

        public BlackScreenRecords(IRepository repository,
            ILogger logger,
            IMessageFormatter messageFormatter,
            IEmoteProvider emoteProvider,
            string commandsPrefix,
            ExceptionHandler exceptionHandler)
            : base(repository, logger, messageFormatter, emoteProvider, commandsPrefix, exceptionHandler, (caller) => ((BlackScreenRecords) caller).ContentWatcherProcedure)
        {
            Repository.PropertyMapper
                .Id<BlackScreenRecordsEntry, string>(x => x.IdObject, false)
                .Id<SubscribedChannel, string>(x => x.IdObject, false)
                ;

            Repository.AddInvocator<SubscribedChannel>();
            Repository.AddInvocator<BlackScreenRecordsEntry>();
        }

        private decimal EuroValue { get; set; }

        [IsCommand]
        public async Task GetFirst(CommandExecutionContext ctx)
        {
            UpdateLatestExchangeRate();

            string url = string.Format(AllReleasesUrlTemplate, 1);
            HtmlWeb web = new HtmlWeb();
            HtmlDocument doc = web.Load(url);
            List<BlackScreenRecordsEntry> parsedEntries = ParsePage(doc);

            SendNotifications(parsedEntries, EntriesFilter, EntryMessageFormatter);

            await Task.CompletedTask;
        }

        [IsCommand]
        public async Task Clear(CommandExecutionContext ctx)
        {
            Repository.DeleteAll<BlackScreenRecordsEntry>();
            Entries.Clear();

            await ctx.Context.RespondStringAsync($"Database cleared for {Name}");
        }

        private void ContentWatcherProcedure(ContentWatcherContext ctx)
        {
            try
            {
                foreach (BlackScreenRecordsEntry item in Repository.TryGetItems<BlackScreenRecordsEntry>(x => true))
                {
                    Entries[item.Title] = item;
                }

                UpdateLatestExchangeRate();

                ParsePages(AllReleasesUrlTemplate);
                ParsePages(DistroReleasesUrlTemplate);
            }
            catch (Exception ex)
            {
                Logger.LogMessage(LogLevel.Error, nameof(Name), ex.ToString());
            }
        }

        private void ParsePages(string urlTemplate)
        {
            int firstPage = 1;

            string url = string.Format(urlTemplate, firstPage);

            HtmlWeb web = new HtmlWeb();
            HtmlDocument doc = web.Load(url);

            int lastPage = doc.DocumentNode
               .Descendants("ul")
               .FirstOrDefault(x => x.HasClass("pagination-custom"))
               ?.Descendants("li")
               .Select(x =>
               {
                   bool parsed = int.TryParse(x.InnerText?.Trim(), out int val);
                   return new { parsed = parsed, val = val };
               })
               .Where(x => x.parsed)
               .Max(x => x.val) 
               ?? 0;

            if (lastPage == 0)
            {
                throw new InvalidOperationException($"Failed to parse last page of `{urlTemplate}`");
            }

            List<BlackScreenRecordsEntry> parsedEntries = ParsePage(doc);

            List<BlackScreenRecordsEntry> newEntries = new List<BlackScreenRecordsEntry>();

            for (int i = firstPage; i <= lastPage; ++i)
            {
                if (i > firstPage)
                {
                    url = string.Format(urlTemplate, i);
                    doc = web.Load(url);
                    parsedEntries = ParsePage(doc);
                }

                foreach (BlackScreenRecordsEntry entry in parsedEntries)
                {
                    BlackScreenRecordsEntry toProcess = entry;

                    if (Entries.TryGetValue(toProcess.Title, out BlackScreenRecordsEntry existing))
                    {
                        if (existing.SetFrom(toProcess))
                        {
                            newEntries.Add(existing);
                        }

                        toProcess = existing;
                    }
                    else
                    {
                        newEntries.Add(toProcess);
                    }

                    Repository.SetItem(toProcess);
                    Entries[toProcess.Title] = toProcess;
                }
            }

            SendNotifications(newEntries, EntriesFilter, EntryMessageFormatter);
        }

        private List<BlackScreenRecordsEntry> ParsePage(HtmlDocument doc)
        {
            List<BlackScreenRecordsEntry> entries = new List<BlackScreenRecordsEntry>();

            foreach (HtmlNode gridCard in doc.DocumentNode.Descendants("a").Where(x => x.HasClass("grid-link")))
            {
                HtmlNode priceNode = gridCard.Descendants("p").First(x => x.HasClass("grid-link__meta"));

                string fullPrice = priceNode.ChildNodes.Last().InnerText.Trim();
                string priceValue = fullPrice.ToLower().Replace("from", string.Empty).Trim();

                BlackScreenRecordsEntry entry = new BlackScreenRecordsEntry()
                {
                    IsPreOrder = GetText(gridCard, "div", "badge__pre-order").Length > 0,
                    IsPriceRange = GetText(gridCard, "p", "grid-link__meta").ToLower().Contains("from"),
                    IsSoldOut = GetText(gridCard, "span", "badge__text").ToLower().Contains("sold out"),
                    IsVinyl = !GetText(gridCard, "p", "grid-link__title").ToLower().Contains("CD]") && !gridCard.Descendants("img").Any(x => x.GetAttributeValue("src", string.Empty).ToLower().Contains("cassette")),
                    Currency = priceValue.Substring(0, 1),
                    Price = priceValue.Substring(1),
                    FullPrice = fullPrice,
                    Title = HtmlEntity.DeEntitize(GetText(gridCard, "p", "grid-link__title")),
                    Url = $"{BaseUrl}{gridCard.GetAttributeValue("href", string.Empty)}"
                };

                if (entry.IsVinyl)
                {
                    entries.Add(entry);
                }
            }

            return entries;
        }

        private string GetText(HtmlNode gridCard, string descendant, string styleClass)
        {
            return gridCard.Descendants(descendant).FirstOrDefault(x => x.HasClass(styleClass))?.InnerText?.Trim() ?? string.Empty;
        }

        private bool EntriesFilter(BlackScreenRecordsEntry entry)
        {
            return !entry.IsSoldOut;
        }

        private string EntryMessageFormatter(BlackScreenRecordsEntry entry)
        {
            return $"{(entry.IsPreOrder ? $"{MessageFormatter.Bold("[PRE-ORDER]")} " : string.Empty)}{entry.Title} - {entry.FullPrice}{GetPlnValueStr(entry.Price)} - {MessageFormatter.NoEmbed(entry.Url)}";
        }

        private void UpdateLatestExchangeRate()
        {
            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(NbpApiEuroUrl);

                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                {
                    using (Stream stream = response.GetResponseStream())
                    {
                        if (stream != null)
                        {
                            using (StreamReader reader = new StreamReader(stream))
                            {
                                string json = reader.ReadToEnd();
                                string euro = JObject.Parse(json)?["rates"]?[0]?["mid"]?.Value<string>() ?? string.Empty;

                                EuroValue = TryConvertToDecimal(euro);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogMessage(LogLevel.Error, nameof(Name), ex.ToString());
            }
        }

        private string GetPlnValueStr(string priceStr)
        {
            decimal price = TryConvertToDecimal(priceStr);

            decimal finalValue = price * EuroValue;

            return $" ({finalValue:F} zł)";
        }

        private decimal TryConvertToDecimal(string str)
        {
            str = str.Replace(",", ".");

            decimal converted = Convert.ToDecimal(str, CultureInfo.InvariantCulture);

            return converted;
        }
    }
}
