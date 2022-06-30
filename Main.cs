using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.PowerToys.Settings.UI.Library;
using Wox.Plugin;

namespace AltV.PowerToysRun.Natives;

internal record Native(string Jhash, string? Comment, Dictionary<string, string>? Hashes, string AltName);

public class Main : IPlugin, IContextMenu, IDisposable
{
    private Dictionary<string, Native> _natives = new();

    private readonly HttpClient _httpClient = new();
    private bool _initialized;
    private int _initAttempt;
    public async void Init(PluginInitContext context)
    {
        try
        {
            await InitAsync();
            _initialized = true;
        }
        catch (Exception)
        {
            _initAttempt++;
            if (_initAttempt > 5) return;
            _ = Task.Delay(2000).ContinueWith(_ => Init(context));
        }
    }

    private async Task InitAsync()
    {
        var nativesJsonMessage = await _httpClient.GetAsync("https://natives.altv.mp/natives");
        var nativesJson = await nativesJsonMessage.Content.ReadAsStringAsync();
        var namespaces = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, Native>>>(
            nativesJson,
            new JsonSerializerOptions
            {
                IncludeFields = true,
                PropertyNameCaseInsensitive = true
            }
        )!;
        _natives = namespaces.Values.SelectMany(e => e).ToDictionary(e => e.Key, e => e.Value);
    }

    public string Name => "alt:V natives";
    public string Description => "Searches alt:V native reference";
    private readonly Regex _splitRegex = new(@"[ _]+");

    public List<Result> Query(Query query)
    {
        if (!_initialized)
        {
            return new List<Result>
            {
                new()
                {
                    Title = (_initAttempt == 6 ? "Plugin failed to initialize" : "Initializing plugin...") + " (attempt " + _initAttempt + ")",
                    SubTitle = "alt:V",
                    QueryTextDisplay = " "
                }
            };
        }
        var strs = _splitRegex
            .Split(string.Join(" ", query.Terms))
            .Select(e => e.Trim().ToLowerInvariant())
            .Where(e => e.Length > 0).ToArray();

        var results = new List<(Native native, string key, List<int> titleHighlight, List<int> subTitleHighlight)>();

        var count = 0;
        foreach (var (key, native) in _natives)
        {
            var titleHighlight = new List<int>();
            var subTitleHighlight = new List<int>();
            var found = 0;

            foreach (var str in strs)
            {
                var nameIndex = native.AltName.ToLowerInvariant().IndexOf(str.ToLowerInvariant(), StringComparison.Ordinal);
                if (nameIndex != -1)
                {
                    found++;
                    titleHighlight.AddRange(Enumerable.Range(nameIndex, str.Length));
                }
                else if (native.Jhash.ToLowerInvariant() == str)
                {
                    found++;
                }
                else if (native.Hashes != null)
                {
                    foreach (var (_, hash) in native.Hashes)
                    {
                        if (hash.ToLowerInvariant() == str)
                        {
                            found++;
                            break;
                        }
                    }
                }

                if (found == strs.Length)
                {
                    count++;
                    results.Add((native, key, titleHighlight, subTitleHighlight));
                    continue;
                }

                if (count >= 200)
                {
                    break;
                }
            }
        }

        return results.Select(e => new Result
        {
            Title = e.native.AltName,
            TitleHighlightData = e.titleHighlight,
            SubTitle = e.native.Comment is not ("" or null) ? e.native.Comment : "alt:V native",
            SubTitleHighlightData = e.subTitleHighlight,
            ToolTipData = new ToolTipData("Native " + e.native.AltName, e.native.Comment is not ("" or null) ? e.native.Comment : "alt:V native"),
            QueryTextDisplay = e.native.AltName,
            ContextData = e,
            IcoPath = "Images\\altv.green.png",
            Action = _ =>
            {
                var link = "https://natives.altv.mp/#/" + e.key;
                var psi = new ProcessStartInfo
                {
                    FileName = link,
                    UseShellExecute = true
                };
                Process.Start(psi);
                return false;
            }

        }).ToList();
    }

    public List<ContextMenuResult> LoadContextMenus(Result selectedResult)
    {
        var e = ((Native native, string key, List<int> titleHighlight, List<int> subTitleHighlight)) selectedResult.ContextData;
        var link = "https://natives.altv.mp/#/" + e.key;
        return new List<ContextMenuResult>
        {
            new()
            {
                Title = "Copy link (Ctrl + Enter)",
                Action = _ =>
                {
                    Clipboard.SetDataObject(link, true);
                    return false;
                },
                Glyph = "\uE71B",
                FontFamily = "Segoe MDL2 Assets",
                AcceleratorKey = Key.Return,
                AcceleratorModifiers = ModifierKeys.Control
            },
            new()
            {
                Title = "Copy name (Shift + Enter)",
                Action = _ =>
                {
                    Clipboard.SetDataObject(e.native.AltName, true);
                    return false;
                },
                Glyph = "\uE8C8",
                FontFamily = "Segoe MDL2 Assets",
                AcceleratorKey = Key.Return,
                AcceleratorModifiers = ModifierKeys.Shift
            },
            new()
            {
                Title = "Copy capitalized name (Ctrl + Shift + Enter)",
                Action = _ =>
                {
                    Clipboard.SetDataObject(e.native.AltName[0].ToString().ToUpperInvariant() + e.native.AltName[1..], true);
                    return false;
                },
                Glyph = "\uE8C8",
                FontFamily = "Segoe MDL2 Assets",
                AcceleratorKey = Key.Return,
                AcceleratorModifiers = ModifierKeys.Shift | ModifierKeys.Control,
            }
        };
    }

    public void Dispose()
    {
    }
}