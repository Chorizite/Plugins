﻿using Octokit;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Chorizite.PluginIndexBuilder {
    internal class IndexBuilder {
        private Options options;
        private List<RespositoryInfo> respositories = [];

        internal static string GithubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? Environment.GetEnvironmentVariable("GH_TOKEN");
        internal static string GithubUser = Environment.GetEnvironmentVariable("GITHUB_USER") ?? Environment.GetEnvironmentVariable("GH_USER");

        internal static ConcurrentBag<string> AddedPlugins = [];
        private GitHubClient _client;

        public IndexBuilder(Options opts) {
            options = opts;

            if (string.IsNullOrEmpty(GithubToken) || string.IsNullOrEmpty(GithubUser)) {
                throw new Exception("Missing GITHUB_TOKEN or GITHUB_USER environment variables");
            }
        }

        public class ChoriziteReleaseInfo {
            public string Version { get; set; }
            public string DownloadUrl { get; set; }
            public string Changelog { get; set; }
        }

        public class ReleasesObj {
            public ChoriziteReleaseInfo Chorizite { get; set; } = new();
            public Dictionary<string, RespositoryInfo> Plugins { get; set; } = [];
        }

        internal async void Build() {
            try {
                MakeDirectories();

                var jsonString = File.ReadAllText(options.RespositoriesJsonPath);
                var json = JsonNode.Parse(jsonString);

                if (json?.AsObject().ContainsKey("repositories") != true) {
                    throw new Exception($"Missing repositories key in {options.RespositoriesJsonPath}");
                }

                _client = new GitHubClient(new ProductHeaderValue("Chorizite.PluginIndexBuilder"));
                var tokenAuth = new Credentials(GithubToken);
                _client.Credentials = tokenAuth;

                var releasesObj = new ReleasesObj();
                GetReleases(releasesObj).Wait();

                foreach (var repo in json["repositories"]!.AsObject()) {
                    respositories.Add(new RespositoryInfo(repo.Key.ToString(), repo.Value.ToString(), _client, options));
                }

                Task.WhenAll(respositories.Select(r => r.Build())).Wait(60000 * 5);

                var releaseResults = respositories.Where(r => r.Latest is not null);

                foreach (var release in releaseResults) {
                    releasesObj.Plugins[release.Name] = release;
                }

                var releaseJson = JsonSerializer.Serialize(releasesObj, new JsonSerializerOptions {
                    WriteIndented = true,
                    IncludeFields = false
                });
                File.WriteAllText(Path.Combine(options.OutputDirectory, "index.json"), releaseJson);

                if (!Directory.Exists(Path.Combine(options.OutputDirectory, "plugins"))) {
                    Directory.CreateDirectory(Path.Combine(options.OutputDirectory, "plugins"));
                }

                var jsonRepoOpts = new JsonSerializerOptions {
                    WriteIndented = true,
                    IncludeFields = true
                };

                foreach (var repo in releaseResults) {
                    var repoJsonPath = Path.Combine(options.OutputDirectory, "plugins", $"{repo.Name}.json");
                    var repoJson = JsonSerializer.Serialize(repo, jsonRepoOpts);
                    File.WriteAllText(repoJsonPath, repoJson);
                }

                BuildHtml(releasesObj.Plugins.Values.ToList(), Path.Combine(options.OutputDirectory, "index.html"));
            }
            catch (Exception e) {
                Console.WriteLine($"Error building index: {e.Message}");
            }
        }

        private async Task GetReleases(ReleasesObj releases) {
            try {
                Console.WriteLine($"----------- Chorizite ----------");
                var allReleases = await _client.Repository.Release.GetAll("Chorizite", "Chorizite");
                Console.WriteLine($"----------- Chorizite ----------:");
                var release = allReleases.First();
                var asset =  release.Assets.FirstOrDefault(a => !a.Name.Contains("Source code") && a.Name.Contains("Installer"));

                Console.WriteLine($"Chorizite version: {release.TagName}");

                releases.Chorizite.Version = release.TagName.Split('/').Last();
                releases.Chorizite.DownloadUrl = asset.BrowserDownloadUrl;
                releases.Chorizite.Changelog = release.Body;
            }
            catch (Exception e) {
                Console.WriteLine($"Error getting releases: {e.Message}");
            }
        }

        private void MakeDirectories() {
            if (Directory.Exists(options.OutputDirectory)) {
                Directory.Delete(options.OutputDirectory, true);
            }

            if (Directory.Exists(options.WorkDirectory)) {
                Directory.CreateDirectory(options.WorkDirectory);
            }

            Directory.CreateDirectory(options.OutputDirectory);
            Directory.CreateDirectory(options.WorkDirectory);
        }

        private void BuildHtml(List<RespositoryInfo> releaseResults, string outFile) {
            var body = new StringBuilder();

            foreach (var release in releaseResults) {
                body.AppendLine($"""
                <tr>
                    <td>{release.Name}</td>
                    <td>{release.Author}</td>
                    <td><a href="{release.RepoUrl}" target="_blank">{release.RepoUrl.Replace("https://github.com/", "")}</a></td>
                    <td>{release.Latest.Date.ToString("yyyy-MM-dd")}</td>
                    <td>{release.Latest.Version}</td>
                    <td>{release.Description}</td>
                    <td>
                        <a href="{release.Latest?.DownloadUrl}">{release.Name}.{release.Latest?.Version}.zip</a>
                        (<a href="./plugins/{release.Name}.json">json</a>)
                    </td>
                </tr>
                """);
            }

            var style = "\r\n        ::backdrop,:root{--sans-font:-apple-system,BlinkMacSystemFont,\"Avenir Next\",Avenir,\"Nimbus Sans L\",Roboto,\"Noto Sans\",\"Segoe UI\",Arial,Helvetica,\"Helvetica Neue\",sans-serif;--mono-font:Consolas,Menlo,Monaco,\"Andale Mono\",\"Ubuntu Mono\",monospace;--standard-border-radius:5px;--bg:#fff;--accent-bg:#f5f7ff;--text:#212121;--text-light:#585858;--border:#898EA4;--accent:#0d47a1;--code:#d81b60;--preformatted:#444;--marked:#ffdd33;--disabled:#efefef}@media (prefers-color-scheme:dark){::backdrop,:root{color-scheme:dark;--bg:#212121;--accent-bg:#2b2b2b;--text:#dcdcdc;--text-light:#ababab;--accent:#ffb300;--code:#f06292;--preformatted:#ccc;--disabled:#111}img,video{opacity:.8}}*,::after,::before{box-sizing:border-box}input,progress,select,textarea{appearance:none;-webkit-appearance:none;-moz-appearance:none}html{font-family:var(--sans-font);scroll-behavior:smooth}body{color:var(--text);background-color:var(--bg);font-size:1.15rem;line-height:1.5;display:grid;grid-template-columns:1fr min(45rem,90%) 1fr;margin:0}body>*{grid-column:2}body>header{background-color:var(--accent-bg);border-bottom:1px solid var(--border);text-align:center;padding:0 .5rem 2rem .5rem;grid-column:1/-1}body>header h1{max-width:1200px;margin:1rem auto}body>header p{max-width:40rem;margin:1rem auto}main{padding-top:1.5rem}body>footer{margin-top:4rem;padding:2rem 1rem 1.5rem 1rem;color:var(--text-light);font-size:.9rem;text-align:center;border-top:1px solid var(--border)}h1{font-size:3rem}h2{font-size:2.6rem;margin-top:3rem}h3{font-size:2rem;margin-top:3rem}h4{font-size:1.44rem}h5{font-size:1.15rem}h6{font-size:.96rem}h1,h2,h3,h4,h5,h6,p{overflow-wrap:break-word}h1,h2,h3{line-height:1.1}@media only screen and (max-width:720px){h1{font-size:2.5rem}h2{font-size:2.1rem}h3{font-size:1.75rem}h4{font-size:1.25rem}}a,a:visited{color:var(--accent)}a:hover{text-decoration:none}[role=button],button,input[type=button],input[type=reset],input[type=submit],label[type=button]{border:none;border-radius:var(--standard-border-radius);background-color:var(--accent);font-size:1rem;color:var(--bg);padding:.7rem .9rem;margin:.5rem 0}[role=button][aria-disabled=true],button[disabled],input[type=button][disabled],input[type=checkbox][disabled],input[type=radio][disabled],input[type=reset][disabled],input[type=submit][disabled],select[disabled]{cursor:not-allowed}button[disabled],input:disabled,select:disabled,textarea:disabled{cursor:not-allowed;background-color:var(--disabled);color:var(--text-light)}input[type=range]{padding:0}abbr[title]{cursor:help;text-decoration-line:underline;text-decoration-style:dotted}[role=button]:not([aria-disabled=true]):hover,button:enabled:hover,input[type=button]:enabled:hover,input[type=reset]:enabled:hover,input[type=submit]:enabled:hover,label[type=button]:hover{filter:brightness(1.4);cursor:pointer}button:focus-visible:where(:enabled,[role=button]:not([aria-disabled=true])),input:enabled:focus-visible:where([type=submit],[type=reset],[type=button]){outline:2px solid var(--accent);outline-offset:1px}header>nav{font-size:1rem;line-height:2;padding:1rem 0 0 0}header>nav ol,header>nav ul{align-content:space-around;align-items:center;display:flex;flex-direction:row;flex-wrap:wrap;justify-content:center;list-style-type:none;margin:0;padding:0}header>nav ol li,header>nav ul li{display:inline-block}header>nav a,header>nav a:visited{margin:0 .5rem 1rem .5rem;border:1px solid var(--border);border-radius:var(--standard-border-radius);color:var(--text);display:inline-block;padding:.1rem 1rem;text-decoration:none}header>nav a:hover{border-color:var(--accent);color:var(--accent);cursor:pointer}@media only screen and (max-width:720px){header>nav a{border:none;padding:0;text-decoration:underline;line-height:1}}aside,details,pre,progress{background-color:var(--accent-bg);border:1px solid var(--border);border-radius:var(--standard-border-radius);margin-bottom:1rem}aside{font-size:1rem;width:30%;padding:0 15px;margin-left:15px;float:right}@media only screen and (max-width:720px){aside{width:100%;float:none;margin-left:0}}article,dialog,fieldset{border:1px solid var(--border);padding:1rem;border-radius:var(--standard-border-radius);margin-bottom:1rem}article h2:first-child,section h2:first-child{margin-top:1rem}section{border-top:1px solid var(--border);border-bottom:1px solid var(--border);padding:2rem 1rem;margin:3rem 0}section+section,section:first-child{border-top:0;padding-top:0}section:last-child{border-bottom:0;padding-bottom:0}details{padding:.7rem 1rem}summary{cursor:pointer;font-weight:700;padding:.7rem 1rem;margin:-.7rem -1rem;word-break:break-all}details[open]>summary+*{margin-top:0}details[open]>summary{margin-bottom:.5rem}details[open]>:last-child{margin-bottom:0}table{border-collapse:collapse;margin:1.5rem 0}td,th{border:1px solid var(--border);text-align:left;padding:.5rem}th{background-color:var(--accent-bg);font-weight:700}tr:nth-child(even){background-color:var(--accent-bg)}table caption{font-weight:700;margin-bottom:.5rem}input,select,textarea{font-size:inherit;font-family:inherit;padding:.5rem;margin-bottom:.5rem;color:var(--text);background-color:var(--bg);border:1px solid var(--border);border-radius:var(--standard-border-radius);box-shadow:none;max-width:100%;display:inline-block}label{display:block}textarea:not([cols]){width:100%}select:not([multiple]){background-image:linear-gradient(45deg,transparent 49%,var(--text) 51%),linear-gradient(135deg,var(--text) 51%,transparent 49%);background-position:calc(100% - 15px),calc(100% - 10px);background-size:5px 5px,5px 5px;background-repeat:no-repeat;padding-right:25px}input[type=checkbox],input[type=radio]{vertical-align:middle;position:relative;width:min-content}input[type=checkbox]+label,input[type=radio]+label{display:inline-block}input[type=radio]{border-radius:100%}input[type=checkbox]:checked,input[type=radio]:checked{background-color:var(--accent)}input[type=checkbox]:checked::after{content:\" \";width:.18em;height:.32em;border-radius:0;position:absolute;top:.05em;left:.17em;background-color:transparent;border-right:solid var(--bg) .08em;border-bottom:solid var(--bg) .08em;font-size:1.8em;transform:rotate(45deg)}input[type=radio]:checked::after{content:\" \";width:.25em;height:.25em;border-radius:100%;position:absolute;top:.125em;background-color:var(--bg);left:.125em;font-size:32px}@media only screen and (max-width:720px){input,select,textarea{width:100%}}input[type=color]{height:2.5rem;padding:.2rem}input[type=file]{border:0}hr{border:none;height:1px;background:var(--border);margin:1rem auto}mark{padding:2px 5px;border-radius:var(--standard-border-radius);background-color:var(--marked);color:#000}img,video{max-width:100%;height:auto;border-radius:var(--standard-border-radius)}figure{margin:0;display:block;overflow-x:auto}figcaption{text-align:center;font-size:.9rem;color:var(--text-light);margin-bottom:1rem}blockquote{margin:2rem 0 2rem 2rem;padding:.4rem .8rem;border-left:.35rem solid var(--accent);color:var(--text-light);font-style:italic}cite{font-size:.9rem;color:var(--text-light);font-style:normal}dt{color:var(--text-light)}code,kbd,pre,pre span,samp{font-family:var(--mono-font);color:var(--code)}kbd{color:var(--preformatted);border:1px solid var(--preformatted);border-bottom:3px solid var(--preformatted);border-radius:var(--standard-border-radius);padding:.1rem .4rem}pre{padding:1rem 1.4rem;max-width:100%;overflow:auto;color:var(--preformatted)}pre code{color:var(--preformatted);background:0 0;margin:0;padding:0}progress{width:100%}progress:indeterminate{background-color:var(--accent-bg)}progress::-webkit-progress-bar{border-radius:var(--standard-border-radius);background-color:var(--accent-bg)}progress::-webkit-progress-value{border-radius:var(--standard-border-radius);background-color:var(--accent)}progress::-moz-progress-bar{border-radius:var(--standard-border-radius);background-color:var(--accent);transition-property:width;transition-duration:.3s}progress:indeterminate::-moz-progress-bar{background-color:var(--accent-bg)}dialog{max-width:40rem;margin:auto}dialog::backdrop{background-color:var(--bg);opacity:.8}@media only screen and (max-width:720px){dialog{max-width:100%;margin:auto 1em}}.button,.button:visited{display:inline-block;text-decoration:none;border:none;border-radius:5px;background:var(--accent);font-size:1rem;color:var(--bg);padding:.7rem .9rem;margin:.5rem 0}.button:focus,.button:hover{filter:brightness(1.4);cursor:pointer}.notice{background:var(--accent-bg);border:2px solid var(--border);border-radius:5px;padding:1.5rem;margin:2rem 0}";

            var html = $"""
<html>
    <head>
       <title>Chorizite Plugin Index</title>
       <style>
        {style}
       </style>
    </head>
    <body>
        <h1>Chorizite Plugin Index</h1>
        <table>
            <thead>
                <tr>
                    <th>Plugin Name</th>
                    <th>Author</th>
                    <th>Repo</th>
                    <th>Updated</th>
                    <th>Version</th>
                    <th>Description</th>
                    <th>Download</th>
                </tr>
            </thead>
            <tbody>
                {body}
                <br /><br />
                <strong>Listing API: <a href="index.json" target="_blank">index.json</a></strong>
            </tbody>
        </table>
    </body>
</html>
""";

            File.WriteAllText(outFile, html);
        }
    }
}