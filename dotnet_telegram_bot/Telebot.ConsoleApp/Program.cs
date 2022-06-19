using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net.Http.Headers;
using System.Text.Json;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;

var builder = WebApplication.CreateBuilder();
builder.Services.AddControllers();
builder.Services.AddHostedService<TelegramBotHostedService>();
builder.Services.AddOpenTelemetryMetrics(openTelemetryBuilder =>
{
    openTelemetryBuilder
        .AddMeter(BotMeter.MeterName)
        .AddRuntimeMetrics()
        .AddPrometheusExporter();
});
builder.Services.AddHttpClient();

var app = builder.Build();
app.MapGet("/hello", () => "Hello World!");
app.UseOpenTelemetryPrometheusScrapingEndpoint();
app.UseRouting();
app.UseEndpoints(endpoints =>
{
    endpoints.MapControllers();
});
app.Run();

public static class BotMeter
{
    public const string MeterName = "Telebot";
    private static readonly Meter NativeMeter = new Meter(MeterName, "1.0.0");
    private static readonly Counter<int> ProcessedMessagesCounter =
        NativeMeter.CreateCounter<int>(name: "processed-messages",  description: "The number of messages processed by the bot");

    public static void IncrementProcessedMessagesCounter()
    {
        ProcessedMessagesCounter.Add(1);
    }
}

public class TelegramBotHostedService : IHostedService, IDisposable
{
    private bool _isActive = false;
    private readonly HttpClient _httpClient;
    private readonly ConcurrentBag<int> _processedMessageIds = new ConcurrentBag<int>();

    public TelegramBotHostedService(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient(nameof(TelegramBotHostedService));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var token = ReadEnvironmentVariableOrThrow("Telebot_Telegram_Token");
            var botClient = new TelegramBotClient(token);
            var me = await botClient.GetMeAsync(cancellationToken: cancellationToken);
            Console.WriteLine($"Start listening for @{me.Username}");
            _isActive = true;
            while (_isActive)
            {
                await HandleNextUpdatesAsync(botClient, cancellationToken);
            }
        }
        catch (Exception exception)
        {
            Console.WriteLine(exception);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _isActive = false;
        Console.WriteLine("Stopped telegram bot");
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
    
    private async Task HandleNextUpdatesAsync(TelegramBotClient botClient, CancellationToken cancellationToken = default)
    {
        var updates = await botClient.GetUpdatesAsync(cancellationToken: cancellationToken);
        var updatesToProcess = updates.Where(update =>
            update.Type == UpdateType.Message
            && update.Message!.Type == MessageType.Text
            && !update.Message.From!.IsBot
            && !_processedMessageIds.Contains(update.Id));

        foreach (var update in updatesToProcess)
        {
            _processedMessageIds.Add(update.Id);
            var chatId = update.Message!.Chat.Id;
            var messageText = update.Message.Text;

            Console.WriteLine($"Received a '{messageText}' message in chat {chatId}.");
            BotMeter.IncrementProcessedMessagesCounter();
            var request = new HttpRequestMessage(HttpMethod.Post, ReadEnvironmentVariableOrThrow("Telebot_Fluentd_Url"));
            request.Content = new StringContent($"json={JsonSerializer.Serialize(new { MessageId = update.Id, ChatId = chatId, MessageText = update.Message.Text })}");
            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/x-www-form-urlencoded");
            var response = await _httpClient.SendAsync(request, cancellationToken);
            Console.WriteLine($"Sent a message to a Fluentd via HTTP Api. Response: {response.ReasonPhrase}");

            // Echo received message text
            await botClient.SendTextMessageAsync(
                chatId: chatId,
                text: "You said:\n" + messageText,
                cancellationToken: cancellationToken);
        }
    }

    private string ReadEnvironmentVariableOrThrow(string variableName)
        => Environment.GetEnvironmentVariable(variableName) ?? throw new ApplicationException($"Can not read environment variable {variableName}");
}