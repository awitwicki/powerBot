﻿using PowerBot.Core.Managers;
using PowerBot.Core.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Types.Enums;

namespace PowerBot.Core
{
    public class PowerbotCore
    {
        private static TelegramBotClient Bot { get; set; }
        public TelegramBotClient BotClient => Bot;

        public static bool Started { get; set; } = false;
        public string AccessTokenEnvName { get; set; } = "TelegramAccessToken";
        public string TelegramAccessToken { get; set; }

        public PowerbotCore() { }

        public async Task StartAsync(CancellationToken stoppingToken)
        {
            if (TelegramAccessToken == null)
            {
                var TelegramAccessToken = Environment.GetEnvironmentVariable(AccessTokenEnvName);
                if (TelegramAccessToken == null)
                    throw new InvalidOperationException($"Can't find environment variable {AccessTokenEnvName}");
            }

            Bot = new TelegramBotClient(TelegramAccessToken);
            StartBotAsync();
            Started = true;

            Console.WriteLine("Bot started");
            await LogsManager.CreateLog($"Bot started", LogLevel.Info);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Bot.StopReceiving();

            return Task.CompletedTask;
        }

        public void StartBotAsync()
        {
            Bot.OnMessage += BotOnMessageReceived;
            Bot.OnReceiveError += BotOnReceiveError;

            //Start listening
            Bot.StartReceiving(Array.Empty<UpdateType>());
        }

        async void BotOnMessageReceived(object sender, MessageEventArgs messageEventArgs)
        {
            var message = messageEventArgs.Message;

            var checkMessageResult = await CheckMessage(messageEventArgs);
            if (!checkMessageResult)
                return;

            if (message.Text != null)
                await MessageInvoker(messageEventArgs);
        }

        private async Task MessageInvoker(MessageEventArgs messageEventArgs)
        {
            //Log stats
            await StatsManager.AddStatAction(ActionType.Message);

            //Get message data
            var chatId = messageEventArgs.Message.Chat.Id;
            var user = await UserManager.AddOrUpdateUser(messageEventArgs);

            if (messageEventArgs.Message.Chat.Type == ChatType.Supergroup ||
                messageEventArgs.Message.Chat.Type == ChatType.Group)
                await ChatManager.AddOrUpdateChat(messageEventArgs);

            //Get all handlers
            var handlers = ReflectiveEnumerator.GetEnumerableOfType<BaseHandler>();

            foreach (var handlerType in handlers)
            {
                //Find method in handler
                MethodInfo[] handlerMethods = handlerType.GetMethods();

                foreach (var method in handlerMethods)
                {
                    //Pattern matching for message text
                    if (BaseHandler.MatchMethod(method, messageEventArgs.Message.Text))
                    {
                        //Check user access by role
                        if (!BaseHandler.ValidateAccess(method, user))
                            return;

                        try
                        {
                            //Get and send chatAction from attributes
                            var chatAction = BaseHandler.GetChatActionAttributes(method);
                            if (chatAction.HasValue)
                                await Bot.SendChatActionAsync(chatId, chatAction.Value);

                            //Cast handler object
                            var handler = Activator.CreateInstance(handlerType);

                            //Set params
                            ((BaseHandler)handler).Init(Bot, user, messageEventArgs);

                            //Invoke method
                            await (Task)method.Invoke(handler, parameters: new object[] { });
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex.Message, "Invoker error");
                            await LogsManager.CreateLog($"Invoker error *{ex.Message}*", LogLevel.Critical);

                            //Log stats
                            await StatsManager.AddStatAction(ActionType.Error);
                        }
                    }
                    else
                    {
                        //Cant find method
                        //await LogsManager.CreateLog($"Can't find method for *{messageEventArgs.Message.Text}*", LogLevel.Warning);
                        //Console.WriteLine($"Can't find method for *{messageEventArgs.Message.Text}*");
                    }
                }
            }
        }

        private async Task<bool> CheckMessage(MessageEventArgs messageEventArgs)
        {
            // Process user in db
            var user = await UserManager.AddOrUpdateUser(messageEventArgs);

            //Filters for messages
            if (messageEventArgs != null)
            {
                var message = messageEventArgs.Message;
                if (message == null || (message.Type != MessageType.Text && message.Type != MessageType.Document)) return false;

                //Ignore old messages
                if (message.Date.AddMinutes(1) < DateTime.UtcNow)
                    return false;
            }

            //Authorize User (if user banned - ignore)
            if (user.IsBanned)
                return false;

            return true;
        }

        private void BotOnReceiveError(object sender, ReceiveErrorEventArgs receiveErrorEventArgs)
        {
            //Log stats
            StatsManager.AddStatAction(ActionType.Error)
                .GetAwaiter()
                .GetResult();

            Debug.WriteLine(receiveErrorEventArgs.ApiRequestException.ErrorCode);
            Debug.WriteLine(receiveErrorEventArgs.ApiRequestException.Message);
        }
    }
}
