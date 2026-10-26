using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Tint.Playground;

/// <summary>
/// The playground page. The code box and the highlighted output are owned by <c>js/playground.js</c>, so typing never
/// re-renders them through Blazor: the script calls <see cref="Update"/> (debounced) and writes the returned HTML, and
/// this component re-renders only the pickers, the inspector and the status line.
/// </summary>
public sealed partial class App : IAsyncDisposable
{
    private const string DefaultLanguage = "csharp";
    private const string DefaultTheme = "dark-plus";

    private ElementReference _textarea;
    private ElementReference _output;
    private IJSObjectReference? _module;
    private IJSObjectReference? _editor;
    private DotNetObjectReference<App>? _self;

    private LanguageOption _language = null!;
    private ThemeOption _theme = null!;
    private ThemeOption? _secondTheme;
    private string _code = string.Empty;
    private HighlightResult? _result;
    private TokenHit? _hit;
    private ThemeExplanation? _explanation;
    private bool _showSecond;
    private bool _showHtml;
    private bool _loading;
    private string _copyLabel = "Copy HTML";
    private string? _message;
    private string? _previewCss;
    private string? _dualCss;
    private double? _interactiveMs;

    [Inject]
    private PlaygroundEngine Engine { get; set; } = null!;

    [Inject]
    private HttpClient Http { get; set; } = null!;

    [Inject]
    private IJSRuntime JS { get; set; } = null!;

    protected override void OnInitialized()
    {
        _language = Engine.FindLanguage(DefaultLanguage) ?? Engine.Languages[0];
        _theme = Engine.FindTheme(DefaultTheme) ?? Engine.Themes[0];
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        _module = await JS.InvokeAsync<IJSObjectReference>("import", "./js/playground.js");
        var (language, theme, secondTheme, code) = PlaygroundState.Parse(await _module.InvokeAsync<string>("readHash"));
        _language = Engine.FindLanguage(language) ?? _language;
        _theme = Engine.FindTheme(theme) ?? _theme;
        _secondTheme = Engine.FindTheme(secondTheme) is { } second && second != _theme ? second : null;
        code ??= await FetchSampleAsync(_language) ?? string.Empty;

        _code = Normalize(code);
        Highlight();
        _self = DotNetObjectReference.Create(this);
        _editor = await _module.InvokeAsync<IJSObjectReference>("init", _textarea, _output, _self, _code, _result!.Html);
        await ApplyColorsAsync();
        _interactiveMs = await _module.InvokeAsync<double>("markInteractive");
        StateHasChanged();
    }

    /// <summary>Called by the script, debounced, when the code changes: highlights it and returns the HTML.</summary>
    [JSInvokable]
    public string Update(string code)
    {
        _code = Normalize(code);
        Highlight();
        StateHasChanged();
        return _result!.Html;
    }

    /// <summary>Called by the script after typing stops: writes the permalink.</summary>
    [JSInvokable]
    public async Task WriteHash()
    {
        if (_module is not null)
        {
            await _module.InvokeVoidAsync("writeHash", new PlaygroundState(_language.Id, _theme.Id, _secondTheme?.Id, _code).ToHash());
        }
    }

    /// <summary>Called by the script when the mouse is over a character.</summary>
    [JSInvokable]
    public void InspectAt(int line, int column)
    {
        var offset = _result?.Tokens.GetOffset(line, column) ?? -1;
        Inspect(offset >= 0 ? _result!.Tokens.Find(offset) : null);
    }

    /// <summary>Called by the script when the caret moves and the mouse is not over the code.</summary>
    [JSInvokable]
    public void InspectCaret(int offset) => Inspect(_result?.Tokens.Find(offset, caret: true));

    public async ValueTask DisposeAsync()
    {
        if (_editor is not null)
        {
            await _editor.DisposeAsync();
        }

        if (_module is not null)
        {
            await _module.DisposeAsync();
        }

        _self?.Dispose();
    }

    private void Inspect(TokenHit? hit)
    {
        if (hit == _hit)
        {
            return;
        }

        _hit = hit;
        _explanation = hit is { } h ? Engine.GetExplainer(InspectedTheme).Explain(h.Token.Scopes) : null;
        StateHasChanged();
    }

    private void Highlight()
    {
        _result = Engine.Highlight(_language, _theme, _secondTheme, _code, InspectedTheme);
        _hit = null;
        _explanation = null;
        _previewCss = _secondTheme is null ? null : PlaygroundEngine.GetDualThemeCss(_secondTheme, ".show-second .editor-output");
        _dualCss = _secondTheme is null ? null : PlaygroundEngine.GetDualThemeCss(_secondTheme);
    }

    private async Task RefreshAsync()
    {
        Highlight();
        if (_editor is not null)
        {
            await _editor.InvokeVoidAsync("setHtml", _result!.Html);
        }

        await ApplyColorsAsync();
        await WriteHash();
    }

    private async Task OnLanguageChanged(ChangeEventArgs e)
    {
        if (Engine.FindLanguage(e.Value as string) is not { } language || language == _language)
        {
            return;
        }

        _language = language;
        _message = null;

        // The sample follows the language, replacing whatever was typed; a language without one keeps the code.
        await LoadSampleAsync();
    }

    private async Task OnThemeChanged(ChangeEventArgs e)
    {
        if (Engine.FindTheme(e.Value as string) is not { } theme)
        {
            return;
        }

        _theme = theme;
        if (_secondTheme == theme)
        {
            _secondTheme = null;
            _showSecond = false;
        }

        await RefreshAsync();
    }

    private async Task OnSecondThemeChanged(ChangeEventArgs e)
    {
        _secondTheme = Engine.FindTheme(e.Value as string);
        if (_secondTheme is null)
        {
            _showSecond = false;
        }

        await RefreshAsync();
    }

    // The theme on screen: the second one while its preview is on.
    private ThemeOption InspectedTheme => _showSecond && _secondTheme is not null ? _secondTheme : _theme;

    private async Task ShowSecond(bool show)
    {
        _showSecond = show;
        Highlight();
        await ApplyColorsAsync();
    }

    private async Task LoadSampleAsync()
    {
        _loading = true;
        try
        {
            var sample = await FetchSampleAsync(_language);
            if (sample is null)
            {
                _message = $"There is no sample for {_language.Label}.";
                await RefreshAsync();
                return;
            }

            _message = null;
            _code = Normalize(sample);
            Highlight();
            if (_editor is not null)
            {
                await _editor.InvokeVoidAsync("setCode", _code, _result!.Html);
            }

            await ApplyColorsAsync();
            await WriteHash();
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task<string?> FetchSampleAsync(LanguageOption language)
    {
        try
        {
            using var response = await Http.GetAsync("samples/" + Uri.EscapeDataString(language.Id) + ".txt");
            return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync() : null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private async Task CopyHtmlAsync()
    {
        if (_module is null || _result is null)
        {
            return;
        }

        try
        {
            await _module.InvokeVoidAsync("copyText", _result.Html);
            _copyLabel = "Copied";
        }
        catch (JSException)
        {
            _copyLabel = "Copy failed";
        }

        StateHasChanged();
        await Task.Delay(1500);
        _copyLabel = "Copy HTML";
    }

    private async Task ApplyColorsAsync()
    {
        if (_module is not null)
        {
            var (background, foreground) = Engine.GetEditorColors(InspectedTheme);
            await _module.InvokeVoidAsync("setColors", background, foreground);
        }
    }

    // A textarea's value has \n line ends; keeping the code the same makes its offsets the textarea's.
    private static string Normalize(string code) => code.Contains('\r') ? code.Replace("\r\n", "\n").Replace('\r', '\n') : code;

    private static string Ms(TimeSpan time) => time.TotalMilliseconds.ToString(time.TotalMilliseconds < 10 ? "0.00" : "0.0", CultureInfo.InvariantCulture) + " ms";

    private static string Visible(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            builder.Append(c switch
            {
                ' ' => '·',
                '\t' => '→',
                _ => c,
            });
        }

        return builder.ToString();
    }

    private static string FontStyleText(FontStyle style)
    {
        if (style is FontStyle.None or FontStyle.NotSet)
        {
            return "normal";
        }

        var parts = new List<string>(4);
        if ((style & FontStyle.Italic) != 0)
        {
            parts.Add("italic");
        }

        if ((style & FontStyle.Bold) != 0)
        {
            parts.Add("bold");
        }

        if ((style & FontStyle.Underline) != 0)
        {
            parts.Add("underline");
        }

        if ((style & FontStyle.Strikethrough) != 0)
        {
            parts.Add("strikethrough");
        }

        return string.Join(' ', parts);
    }

    private static RenderFragment RuleText(MatchedThemeRule? rule) => builder =>
    {
        if (rule is null)
        {
            builder.AddContent(0, "the theme's default");
            return;
        }

        builder.OpenElement(1, "code");
        builder.AddContent(2, rule.Selector);
        builder.CloseElement();
        if (rule.Selector != rule.MatchedScope)
        {
            builder.AddContent(3, " matched ");
            builder.OpenElement(4, "code");
            builder.AddContent(5, rule.MatchedScope);
            builder.CloseElement();
        }

        builder.OpenElement(6, "span");
        builder.AddAttribute(7, "class", "rule-meta");
        builder.AddContent(8, $"rule #{rule.Index}{(rule.Name is null ? null : " “" + rule.Name + "”")}{(rule.Foreground is null ? null : ", " + rule.Foreground)}{(rule.FontStyle is null ? null : ", " + (rule.FontStyle.Length == 0 ? "normal" : rule.FontStyle))}");
        builder.CloseElement();
    };
}
