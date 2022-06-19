using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

try {
    var token = Environment.GetEnvironmentVariable("Telebot_Telegram_Token");
    var botClient = new TelegramBotClient(token);
    var httpClient = new HttpClient();

    using var cts = new CancellationTokenSource();
    var me = await botClient.GetMeAsync();
    Console.WriteLine($"Start listening for @{me.Username}");

    var processedMessageIds = new ConcurrentBag<int>();

    while (true)
    {
        try
        {
            await HandleNextUpdatesAsync();
        }
        catch (Exception exception)
        {
            Console.WriteLine(exception);
        }
    }

    async Task HandleNextUpdatesAsync()
    {
        var updates = await botClient.GetUpdatesAsync();
        var updatesToProcess = updates.Where(update =>
            update.Type == UpdateType.Message
            && update.Message!.Type == MessageType.Text
            && !update.Message.From!.IsBot
            && !processedMessageIds.Contains(update.Id));

        foreach (var update in updatesToProcess)
        {
            processedMessageIds.Add(update.Id);
            var chatId = update.Message!.Chat.Id;
            var messageText = update.Message.Text;

            Console.WriteLine($"Received a '{messageText}' message in chat {chatId}.");
            var request = new HttpRequestMessage(HttpMethod.Post, Environment.GetEnvironmentVariable("Telebot_Fluentd_Url"));
            request.Content = new StringContent($"json={JsonSerializer.Serialize(new { MessageId = update.Id, ChatId = chatId, MessageText = update.Message.Text })}");
            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/x-www-form-urlencoded");
            var response = await httpClient.SendAsync(request);
            Console.WriteLine($"Sent a message to a Fluentd via HTTP Api. Response: {response.ReasonPhrase}");
        
            // Echo received message text
            await botClient.SendTextMessageAsync(
                chatId: chatId,
                text: "You said:\n" + messageText,
                cancellationToken: cts.Token);
        }
    }
} catch(Exception exception) {
    Console.WriteLine($"Error: ${exception.Message}");
}
