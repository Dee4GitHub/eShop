using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Asp.Versioning;
using Asp.Versioning.Http;
using eShop.Ordering.API.Application.Models;
using eShop.Ordering.Domain.AggregatesModel.BuyerAggregate;
using eShop.Ordering.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using DomainOrder = eShop.Ordering.Domain.AggregatesModel.OrderAggregate.Order;

namespace eShop.Ordering.FunctionalTests;

// Tests added for T1 (remove MediatR). Each one passes on the MediatR pipeline and must keep
// passing, unchanged, after the refactor. Every test uses its own user id, so tests never see
// each other's orders.
// A valid order is not checked for 200 in this fixture. The order commits, then the outbox
// step returns 500: IntegrationEventLogService finds event types in the entry assembly, which
// under the test runner is Ordering.FunctionalTests, not Ordering.API.
public sealed class OrderingBehaviourTests : IClassFixture<OrderingApiFixture>
{
    private const string IdentityUrlPlaceholder = "<identity-url>";

    private readonly OrderingApiFixture _fixture;
    private readonly HttpClient _httpClient;

    public OrderingBehaviourTests(OrderingApiFixture fixture)
    {
        _fixture = fixture;
        _httpClient = CreateVersionedClient(fixture);
    }

    // AC4: validation still runs on the inner command. Only CreateOrderCommandValidator checks
    // the CVV length; the domain only checks that it is not blank. An expired card would not
    // do, because the PaymentMethod constructor rejects that as well.
    [Fact]
    public async Task CreateOrder_WithInvalidCvv_ReturnsOkButCreatesNoOrder()
    {
        var userId = Guid.NewGuid().ToString();

        var response = await PostOrderAsync(_httpClient, BuildOrder(userId, cvv: "12"), Guid.NewGuid());

        var (buyer, orders) = await FindBuyerAndOrdersAsync(_fixture, userId);
        Assert.Null(buyer);
        Assert.Empty(orders);

        // The endpoint returns 200 whatever the command result, because IdentifiedCommandHandler
        // swallows the validation exception and returns false.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // AC4: Logging, then Validator, then Transaction wrap the outer command, and the inner
    // command goes through Logging and Validator again inside the same transaction.
    [Fact]
    public async Task CreateOrder_RunsLoggingThenValidatorThenTransaction_OnBothPasses()
    {
        var logs = new CapturingLoggerProvider();
        await using var factory = _fixture.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<ILoggerProvider>(logs)));
        var client = CreateVersionedClient(factory);

        await PostOrderAsync(client, BuildOrder(Guid.NewGuid().ToString()), Guid.NewGuid());

        const string identified = "IdentifiedCommand<CreateOrderCommand,Boolean>";
        const string inner = "CreateOrderCommand";

        var expected = new[]
        {
            ("Handling", identified),
            ("Validating", identified),
            ("Begin transaction", identified),
            ("Handling", inner),
            ("Validating", inner),
            ("Commit transaction", identified),
        };

        Assert.Equal(expected, logs.BehaviourEntries());
        Assert.DoesNotContain(("Begin transaction", inner), logs.BehaviourEntries());
    }

    // AC5: a second request with the same x-requestid is not processed again.
    [Fact]
    public async Task CreateOrder_SameRequestIdTwice_CreatesOneOrder()
    {
        var userId = Guid.NewGuid().ToString();
        var requestId = Guid.NewGuid();
        var order = BuildOrder(userId);

        await PostOrderAsync(_httpClient, order, requestId);
        var duplicate = await PostOrderAsync(_httpClient, order, requestId);

        var (buyer, orders) = await FindBuyerAndOrdersAsync(_fixture, userId);
        Assert.NotNull(buyer);
        Assert.Single(orders);

        // The duplicate returns the stored result without running the command, so it writes no
        // integration event and gets a real 200.
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
    }

    // AC6: OrderStartedDomainEvent creates the buyer, and the handler's own save dispatches
    // BuyerAndPaymentMethodVerifiedDomainEvent, whose handler sets BuyerId and PaymentId on the
    // order. Both ids being set proves the nested dispatch ran.
    [Fact]
    public async Task CreateOrder_ValidOrder_CreatesBuyerAndLinksItToOrder()
    {
        var userId = Guid.NewGuid().ToString();

        await PostOrderAsync(_httpClient, BuildOrder(userId), Guid.NewGuid());

        var (buyer, orders) = await FindBuyerAndOrdersAsync(_fixture, userId);
        Assert.NotNull(buyer);
        Assert.Single(buyer.PaymentMethods);

        var order = Assert.Single(orders);
        Assert.Equal(buyer.Id, order.BuyerId);
        Assert.NotNull(order.PaymentId);
    }

    // AC8: the OpenAPI document is unchanged. If the snapshot is missing, the actual document is
    // written next to it as .received.json and the test fails, so a baseline is never created
    // without someone reading it first.
    [Fact]
    public async Task OpenApiDocument_MatchesSavedSnapshot()
    {
        var client = _fixture.CreateDefaultClient();
        var response = await client.GetAsync("openapi/v1.json", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();

        var actual = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        MaskIdentityUrls(actual);

        var snapshotPath = SnapshotPath();
        if (!File.Exists(snapshotPath))
        {
            var receivedPath = Path.ChangeExtension(snapshotPath, ".received.json");
            Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath));
            await File.WriteAllTextAsync(receivedPath, actual.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), TestContext.Current.CancellationToken);
            Assert.Fail($"No OpenAPI snapshot found. The actual document was written to {receivedPath}. Review it and rename it to {Path.GetFileName(snapshotPath)}.");
        }

        var expected = JsonNode.Parse(await File.ReadAllTextAsync(snapshotPath, TestContext.Current.CancellationToken));
        Assert.True(JsonNode.DeepEquals(expected, actual), "The OpenAPI document differs from the saved snapshot.");
    }

    private static HttpClient CreateVersionedClient(WebApplicationFactory<Program> factory)
    {
        var handler = new ApiVersionHandler(new QueryStringApiVersionWriter(), new ApiVersion(1.0));
        return factory.CreateDefaultClient(handler);
    }

    private static CreateOrderRequest BuildOrder(string userId, string cvv = "123")
    {
        var item = new BasketItem
        {
            Id = Guid.NewGuid().ToString(),
            ProductId = 12,
            ProductName = "Test",
            UnitPrice = 10,
            OldUnitPrice = 9,
            Quantity = 1,
            PictureUrl = null
        };

        return new CreateOrderRequest(
            UserId: userId,
            UserName: "BehaviourTestUser",
            City: "Sydney",
            Street: "1 George Street",
            State: "NSW",
            Country: "Australia",
            ZipCode: "2000",
            CardNumber: "4012888888881881",
            CardHolderName: "Behaviour Test",
            CardExpiration: DateTime.UtcNow.AddYears(1),
            CardSecurityNumber: cvv,
            CardTypeId: 1,
            Buyer: userId,
            Items: new List<BasketItem> { item });
    }

    private static async Task<HttpResponseMessage> PostOrderAsync(HttpClient client, CreateOrderRequest order, Guid requestId)
    {
        var content = new StringContent(JsonSerializer.Serialize(order), Encoding.UTF8, "application/json")
        {
            Headers = { { "x-requestid", requestId.ToString() } }
        };
        return await client.PostAsync("api/orders", content, TestContext.Current.CancellationToken);
    }

    private static async Task<(Buyer Buyer, List<DomainOrder> Orders)> FindBuyerAndOrdersAsync(WebApplicationFactory<Program> factory, string userId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrderingContext>();

        var buyer = await context.Buyers
            .Include(b => b.PaymentMethods)
            .SingleOrDefaultAsync(b => b.IdentityGuid == userId, TestContext.Current.CancellationToken);

        if (buyer is null)
        {
            return (null, new List<DomainOrder>());
        }

        var orders = await context.Orders
            .Where(o => o.BuyerId == buyer.Id)
            .ToListAsync(TestContext.Current.CancellationToken);

        return (buyer, orders);
    }

    // The fixture starts Identity.API on a random port, and the document embeds its authorise and
    // token URLs, so those two values are replaced before the comparison.
    private static void MaskIdentityUrls(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var name in obj.Select(p => p.Key).ToList())
                {
                    if (name is "authorizationUrl" or "tokenUrl")
                    {
                        obj[name] = IdentityUrlPlaceholder;
                    }
                    else if (obj[name] is not null)
                    {
                        MaskIdentityUrls(obj[name]);
                    }
                }
                break;
            case JsonArray array:
                foreach (var child in array.Where(c => c is not null))
                {
                    MaskIdentityUrls(child);
                }
                break;
        }
    }

    private static string SnapshotPath([CallerFilePath] string sourceFile = "")
        => Path.Combine(Path.GetDirectoryName(sourceFile), "Snapshots", "ordering-openapi-v1.json");

    // Records the log entries written by the three pipeline behaviours, in the order they are
    // written. It matches on the message template, so the four templates must not change.
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private static readonly Dictionary<string, (string Marker, string NameKey)> BehaviourTemplates = new()
        {
            ["Handling command {CommandName} ({@Command})"] = ("Handling", "CommandName"),
            ["Validating command {CommandType}"] = ("Validating", "CommandType"),
            ["Begin transaction {TransactionId} for {CommandName} ({@Command})"] = ("Begin transaction", "CommandName"),
            ["Commit transaction {TransactionId} for {CommandName}"] = ("Commit transaction", "CommandName"),
        };

        private readonly ConcurrentQueue<(string Marker, string CommandName)> _entries = new();

        public (string Marker, string CommandName)[] BehaviourEntries() => _entries.ToArray();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

        public void Dispose()
        {
        }

        private void Record(IReadOnlyList<KeyValuePair<string, object>> state)
        {
            var template = state.FirstOrDefault(p => p.Key == "{OriginalFormat}").Value as string;
            if (template is null || !BehaviourTemplates.TryGetValue(template, out var behaviour))
            {
                return;
            }

            var commandName = state.FirstOrDefault(p => p.Key == behaviour.NameKey).Value as string;
            _entries.Enqueue((behaviour.Marker, commandName));
        }

        private sealed class CapturingLogger(CapturingLoggerProvider provider) : ILogger
        {
            public IDisposable BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            {
                if (state is IReadOnlyList<KeyValuePair<string, object>> values)
                {
                    provider.Record(values);
                }
            }
        }
    }
}
