﻿using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Thor.Abstractions;
using Thor.Abstractions.Realtime;
using Thor.Abstractions.Realtime.Dto;


namespace Thor.AzureOpenAI.Realtime;

public class AzureOpenAIRealtimeClient : IRealtimeClient
{
    private readonly ClientWebSocket _socket = new();
    private readonly CancellationTokenSource _cancellationTokenSource = new();

    public event EventHandler<RealtimeResult>? OnMessage;

    public void Dispose()
    {
        _socket.Dispose();
        _cancellationTokenSource.Dispose();
    }

    public async Task OpenAsync(OpenRealtimeInput input, ThorPlatformOptions? options = null)
    {
        _socket.Options.AddSubProtocol("realtime");
        _socket.Options.AddSubProtocol("websocket.api_key." + options!.ApiKey);

        var uri = new Uri(options.Address);
        await _socket.ConnectAsync(
            new Uri(uri.Scheme == "http"
                ? "ws"
                : "wss" + "://" + uri.Host + "/openai/realtime?deployment=" + input.Model + "&api-version=" +
                  options.Other + "&api-key=" + options.ApiKey),
            _cancellationTokenSource.Token);

        _ = Task.Run(async () =>
        {
            var buffer = ArrayPool<byte>.Shared.Rent(1024 * 1024 * 2);
            try
            {
                while (_socket.State == WebSocketState.Open)
                {
                    var result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                        Console.WriteLine("Connection closed.");
                    }
                    else
                    {
                        string responseMessage = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        Console.WriteLine(responseMessage);
                        
                        var content = JsonSerializer.Deserialize<RealtimeResult>(buffer.AsSpan(0, result.Count),
                            ThorJsonSerializer.DefaultOptions);
                        OnMessage?.Invoke(this, content);
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }

            ArrayPool<byte>.Shared.Return(buffer);
        });
    }

    public Task SendAsync(RealtimeInput input)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(input, ThorJsonSerializer.DefaultOptions);
        return _socket.SendAsync(new ArraySegment<byte>(json), WebSocketMessageType.Text, true,
            CancellationToken.None);
    }
}