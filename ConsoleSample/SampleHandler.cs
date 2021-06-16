﻿using PowerBot.Core;
using PowerBot.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot.Types.Enums;

namespace ConsoleSample
{
    class SampleHandler : BaseHandler
    {
        [MessageReaction(ChatAction.Typing)]
        [MessagePattern("/test")]
        public async Task Start()
        {
            string messageText = $"Hi! your database id is {User.Id}, telegram id is {User.TelegramId}, chatId is {ChatId}.";
            messageText += $"\nThis message was handled in SampleHandler";
            
            await Bot.SendTextMessageAsync(ChatId, messageText);
        }
    }
}
