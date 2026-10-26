using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Tint.Grammars;
using Tint.Playground;
using Tint.Themes;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");

builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddSingleton(_ => new PlaygroundEngine(TintGrammars.All, TintThemes.All));

await builder.Build().RunAsync();
