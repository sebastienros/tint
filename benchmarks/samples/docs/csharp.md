# csharp documentation sample

Forty fenced blocks, as a documentation site renders them. Generated from short hand-written snippets.

## Example 1

````cs
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
var app = builder.Build();
app.MapGet("/", () => "Hello World!");
app.Run();
````

## Example 2

````cs
public record Person(string FirstName, string LastName, int Age)
{
    public string FullName => $"{FirstName} {LastName}";
}
````

## Example 3

````cs
public async Task<IActionResult> GetAsync(int id, CancellationToken cancellationToken)
{
    var item = await _db.Items.FindAsync([id], cancellationToken);
    return item is null ? NotFound() : Ok(item);
}
````

## Example 4

````cs
// Filter and project with LINQ.
var adults = people
    .Where(p => p.Age >= 18)
    .OrderBy(p => p.LastName)
    .Select(p => new { p.FullName, p.Age })
    .ToList();
````

## Example 5

````cs
[Fact]
public void AddsTwoNumbers()
{
    var calculator = new Calculator();
    Assert.Equal(4, calculator.Add(2, 2));
}
````

## Example 6

````cs
switch (shape)
{
    case Circle { Radius: > 10 } c:
        Console.WriteLine($"Large circle {c.Radius}");
        break;
    case Rectangle r when r.Width == r.Height:
        Console.WriteLine("Square");
        break;
    default:
        throw new ArgumentOutOfRangeException(nameof(shape));
}
````

## Example 7

````cs
namespace Contoso.Orders;

/// <summary>Stores orders.</summary>
public interface IOrderRepository
{
    Task<Order?> FindAsync(Guid id);
    IAsyncEnumerable<Order> ListAsync(string? customer = null);
}
````

## Example 8

````cs
using var stream = File.OpenRead(path);
using var reader = new StreamReader(stream, Encoding.UTF8);
string? line;
while ((line = await reader.ReadLineAsync()) is not null)
{
    if (line.StartsWith('#')) continue;
    count += line.Length;
}
````

## Example 9

````cs
services.AddHttpClient<GitHubClient>(client =>
{
    client.BaseAddress = new Uri("https://api.github.com/");
    client.DefaultRequestHeaders.Add("User-Agent", "sample");
});
````

## Example 10

````cs
Span<byte> buffer = stackalloc byte[256];
if (Utf8Formatter.TryFormat(value, buffer, out var written))
{
    destination.Write(buffer[..written]);
}
````

## Example 11

````cs
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
var app = builder.Build();
app.MapGet("/", () => "Hello World!");
app.Run();
````

## Example 12

````cs
public record Person(string FirstName, string LastName, int Age)
{
    public string FullName => $"{FirstName} {LastName}";
}
````

## Example 13

````cs
public async Task<IActionResult> GetAsync(int id, CancellationToken cancellationToken)
{
    var item = await _db.Items.FindAsync([id], cancellationToken);
    return item is null ? NotFound() : Ok(item);
}
````

## Example 14

````cs
// Filter and project with LINQ.
var adults = people
    .Where(p => p.Age >= 18)
    .OrderBy(p => p.LastName)
    .Select(p => new { p.FullName, p.Age })
    .ToList();
````

## Example 15

````cs
[Fact]
public void AddsTwoNumbers()
{
    var calculator = new Calculator();
    Assert.Equal(4, calculator.Add(2, 2));
}
````

## Example 16

````cs
switch (shape)
{
    case Circle { Radius: > 10 } c:
        Console.WriteLine($"Large circle {c.Radius}");
        break;
    case Rectangle r when r.Width == r.Height:
        Console.WriteLine("Square");
        break;
    default:
        throw new ArgumentOutOfRangeException(nameof(shape));
}
````

## Example 17

````cs
namespace Contoso.Orders;

/// <summary>Stores orders.</summary>
public interface IOrderRepository
{
    Task<Order?> FindAsync(Guid id);
    IAsyncEnumerable<Order> ListAsync(string? customer = null);
}
````

## Example 18

````cs
using var stream = File.OpenRead(path);
using var reader = new StreamReader(stream, Encoding.UTF8);
string? line;
while ((line = await reader.ReadLineAsync()) is not null)
{
    if (line.StartsWith('#')) continue;
    count += line.Length;
}
````

## Example 19

````cs
services.AddHttpClient<GitHubClient>(client =>
{
    client.BaseAddress = new Uri("https://api.github.com/");
    client.DefaultRequestHeaders.Add("User-Agent", "sample");
});
````

## Example 20

````cs
Span<byte> buffer = stackalloc byte[256];
if (Utf8Formatter.TryFormat(value, buffer, out var written))
{
    destination.Write(buffer[..written]);
}
````

## Example 21

````cs
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
var app = builder.Build();
app.MapGet("/", () => "Hello World!");
app.Run();
````

## Example 22

````cs
public record Person(string FirstName, string LastName, int Age)
{
    public string FullName => $"{FirstName} {LastName}";
}
````

## Example 23

````cs
public async Task<IActionResult> GetAsync(int id, CancellationToken cancellationToken)
{
    var item = await _db.Items.FindAsync([id], cancellationToken);
    return item is null ? NotFound() : Ok(item);
}
````

## Example 24

````cs
// Filter and project with LINQ.
var adults = people
    .Where(p => p.Age >= 18)
    .OrderBy(p => p.LastName)
    .Select(p => new { p.FullName, p.Age })
    .ToList();
````

## Example 25

````cs
[Fact]
public void AddsTwoNumbers()
{
    var calculator = new Calculator();
    Assert.Equal(4, calculator.Add(2, 2));
}
````

## Example 26

````cs
switch (shape)
{
    case Circle { Radius: > 10 } c:
        Console.WriteLine($"Large circle {c.Radius}");
        break;
    case Rectangle r when r.Width == r.Height:
        Console.WriteLine("Square");
        break;
    default:
        throw new ArgumentOutOfRangeException(nameof(shape));
}
````

## Example 27

````cs
namespace Contoso.Orders;

/// <summary>Stores orders.</summary>
public interface IOrderRepository
{
    Task<Order?> FindAsync(Guid id);
    IAsyncEnumerable<Order> ListAsync(string? customer = null);
}
````

## Example 28

````cs
using var stream = File.OpenRead(path);
using var reader = new StreamReader(stream, Encoding.UTF8);
string? line;
while ((line = await reader.ReadLineAsync()) is not null)
{
    if (line.StartsWith('#')) continue;
    count += line.Length;
}
````

## Example 29

````cs
services.AddHttpClient<GitHubClient>(client =>
{
    client.BaseAddress = new Uri("https://api.github.com/");
    client.DefaultRequestHeaders.Add("User-Agent", "sample");
});
````

## Example 30

````cs
Span<byte> buffer = stackalloc byte[256];
if (Utf8Formatter.TryFormat(value, buffer, out var written))
{
    destination.Write(buffer[..written]);
}
````

## Example 31

````cs
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
var app = builder.Build();
app.MapGet("/", () => "Hello World!");
app.Run();
````

## Example 32

````cs
public record Person(string FirstName, string LastName, int Age)
{
    public string FullName => $"{FirstName} {LastName}";
}
````

## Example 33

````cs
public async Task<IActionResult> GetAsync(int id, CancellationToken cancellationToken)
{
    var item = await _db.Items.FindAsync([id], cancellationToken);
    return item is null ? NotFound() : Ok(item);
}
````

## Example 34

````cs
// Filter and project with LINQ.
var adults = people
    .Where(p => p.Age >= 18)
    .OrderBy(p => p.LastName)
    .Select(p => new { p.FullName, p.Age })
    .ToList();
````

## Example 35

````cs
[Fact]
public void AddsTwoNumbers()
{
    var calculator = new Calculator();
    Assert.Equal(4, calculator.Add(2, 2));
}
````

## Example 36

````cs
switch (shape)
{
    case Circle { Radius: > 10 } c:
        Console.WriteLine($"Large circle {c.Radius}");
        break;
    case Rectangle r when r.Width == r.Height:
        Console.WriteLine("Square");
        break;
    default:
        throw new ArgumentOutOfRangeException(nameof(shape));
}
````

## Example 37

````cs
namespace Contoso.Orders;

/// <summary>Stores orders.</summary>
public interface IOrderRepository
{
    Task<Order?> FindAsync(Guid id);
    IAsyncEnumerable<Order> ListAsync(string? customer = null);
}
````

## Example 38

````cs
using var stream = File.OpenRead(path);
using var reader = new StreamReader(stream, Encoding.UTF8);
string? line;
while ((line = await reader.ReadLineAsync()) is not null)
{
    if (line.StartsWith('#')) continue;
    count += line.Length;
}
````

## Example 39

````cs
services.AddHttpClient<GitHubClient>(client =>
{
    client.BaseAddress = new Uri("https://api.github.com/");
    client.DefaultRequestHeaders.Add("User-Agent", "sample");
});
````

## Example 40

````cs
Span<byte> buffer = stackalloc byte[256];
if (Utf8Formatter.TryFormat(value, buffer, out var written))
{
    destination.Write(buffer[..written]);
}
````
