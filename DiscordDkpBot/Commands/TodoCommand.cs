﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

using Discord;

using DiscordDkpBot.Configuration;

namespace DiscordDkpBot.Commands
{
	public abstract class TodoCommand : IChannelCommand
	{
		private readonly DiscordConfiguration config;
		public const string todo = @"```
 - reign in dkpcheck (so it doesn't trigger on "".dkp todo"" etc)
 - add class leaderboards
 - add configuration options for messages.
 ```";
		public string CommandDescription => "TODO:";
		public TodoCommand(DiscordConfiguration config)
		{
			this.config = config;
		}
		public async Task<bool> TryInvokeAsync(IMessage message)
		{
			if (message.Content?.Equals(ChannelSyntax, StringComparison.OrdinalIgnoreCase) == true)
			{
				await message.Channel.SendMessageAsync(todo);
				return true;
			}
			return false;
		}

		public string ChannelSyntax => $"{config.CommandPrefix} todo";
	}
}
