using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Newtonsoft.Json;

namespace ScanSkodaMirrorLinkCatalog
{
    class Program
    {
        private static readonly HttpClient _client = new HttpClient();

        static void Main(string[] args)
        {
            GetCompatibilityTableAsync("https://compatibilitylist.skoda-auto.com")
                .GetAwaiter()
                .GetResult();
        }

        private static async Task GetCompatibilityTableAsync(string url)
        {
            var models = await LoadDocumentAsync(url);
            foreach (var modelNode in models.DocumentNode.SelectNodes("//span[@class='caption']"))
            {
                var modelName = GetNormalizedInnerText(modelNode);
                var modelYears = await LoadDocumentAsync(url + GetUrl(modelNode.ParentNode.ParentNode));
                foreach (var modelYearNode in modelYears.DocumentNode.SelectNodes("//a"))
                {
                    var modelYear = GetNormalizedInnerText(modelYearNode);
                    var equipments = await LoadDocumentAsync(url + GetUrl(modelYearNode));

                    var tasks = equipments
                        .DocumentNode
                        .SelectNodes("//span[@class='caption']")
                        .Select(async equipmentNode => await SaveCarCompatibilityTableAsync(
                           modelName,
                           modelYear,
                           GetNormalizedInnerText(equipmentNode),
                           await GetEquipmentMobilePhoneFeaturesAsync(url, equipmentNode)))
                        .ToArray();

                    await Task.WhenAll(tasks);
                }
            }
        }

        private static async Task<MobilePhoneFeaturesInfo[]> GetEquipmentMobilePhoneFeaturesAsync(string url, HtmlNode equipmentNode)
        {
            var manufacturers = await LoadDocumentAsync(url + GetUrl(equipmentNode.ParentNode.ParentNode));

            var tasks = manufacturers
                .DocumentNode
                .SelectNodes("//span[@class='caption']")
                .Select(node => GetManufacturerMobilePhoneFeaturesAsync(url, node))
                .ToArray();

            var results = await Task.WhenAll(tasks);

            return results.SelectMany(_ => _).OrderBy(_ => _.Manufacturer).ThenBy(_ => _.Model).ToArray();
        }

        private static async Task<MobilePhoneFeaturesInfo[]> GetManufacturerMobilePhoneFeaturesAsync(string url, HtmlNode manufacturerNode)
        {
            var manufacturerName = GetNormalizedInnerText(manufacturerNode);

            var mobiles = await LoadDocumentAsync(url + GetUrl(manufacturerNode.ParentNode.ParentNode));

            var tasks = mobiles.DocumentNode.SelectNodes("//a").Select(async node =>
            {
                var mobileName = GetNormalizedInnerText(node);
                var features = await GetFeaturesAsync(url, node);

                Console.WriteLine($"{manufacturerName} {mobileName}");
                
                return new MobilePhoneFeaturesInfo
                {
                    Model = mobileName,
                    Manufacturer = manufacturerName,
                    Features = features
                };
            }).ToArray();

            return await Task.WhenAll(tasks);
        }

        private static Task SaveCarCompatibilityTableAsync(string carName, string carYears, string equipmentName, IEnumerable<MobilePhoneFeaturesInfo> features)
        {
            var path = $"{NormalizePath(carName)}/{NormalizePath(carYears)}/{NormalizePath(equipmentName)}.json";
            var directory = Path.GetDirectoryName(path);
            if (directory != null && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            return File.WriteAllTextAsync(path, JsonConvert.SerializeObject(features.ToArray(), Formatting.Indented));
        }

        private static async Task<FeatureInfo[]> GetFeaturesAsync(string url, HtmlNode mobileNode)
        {
            var functions = await LoadDocumentAsync(url + GetUrl(mobileNode));
            return functions
                .DocumentNode
                .SelectNodes("//h3")
                .Select(functionNode => new FeatureInfo
                {
                    Name = GetNormalizedInnerText(functionNode.ChildNodes[0]),
                    Description = GetNormalizedInnerText(functionNode.ParentNode.ChildNodes.First(n => n.Name == "p")),
                    Supported = IsSupported(functionNode),
                    SubFeatures = functionNode.ParentNode.ParentNode.ChildNodes.First(n => n.Name == "ul").ChildNodes.Where(n => n.Name == "li")
                        .Select(subFunctionNode => new SubFeatureInfo
                        {
                            Name = GetNormalizedInnerText(subFunctionNode.ChildNodes[0]),
                            Description = GetNormalizedInnerText(subFunctionNode.ChildNodes[3]),
                            Supported = IsSupported(subFunctionNode)
                        })
                        .ToArray()
                }).ToArray();
        }

        private static async Task<HtmlDocument> LoadDocumentAsync(string url)
        {
            var document = new HtmlDocument();
            document.LoadHtml(await _client.GetStringAsync(url));
            return document;
        }

        private static string GetNormalizedInnerText(HtmlNode node)
        {
            return node.InnerHtml.Trim(' ', '\r', '\n');
        }

        private static string NormalizePath(string path)
        {
            return path.Replace("&quot;", "'").Replace("&amp;", "&").Replace("\r", "").Replace("\n", "").Replace("/", "_").Replace("\\", "_").Replace(" ", "");
        }

        private static string GetUrl(HtmlNode node)
        {
            var url = node.Attributes["onclick"].Value.Split("'").Skip(1).First();
            return url.Replace("&amp;", "&");
        }

        private static bool IsSupported(HtmlNode node)
        {
            return node.ChildNodes.First(n => n.Name == "img").Attributes["alt"].Value == "Function is supported";
        } 
    }
}