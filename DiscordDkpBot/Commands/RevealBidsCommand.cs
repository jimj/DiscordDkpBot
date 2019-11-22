﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Discord;

using DiscordDkpBot.Auctions;
using DiscordDkpBot.Configuration;

using Microsoft.Extensions.Logging;

namespace DiscordDkpBot.Commands
{
	public class RevealBidsCommand : IChannelCommand
	{
		private readonly DkpBotConfiguration configuration;
		private readonly ILogger<RevealBidsCommand> log;
		private readonly Regex pattern;
		private readonly string revealChannel;
		private readonly AuctionState state;

		public string ChannelSyntax => $"{configuration.CommandPrefix} reveal {{auctionId}} (Only in configured officer channel).";

		public RevealBidsCommand(DkpBotConfiguration configuration, AuctionState state, ILogger<RevealBidsCommand> log)
		{
			pattern = new Regex($@"^{Regex.Escape(configuration.CommandPrefix)}\s*reveal\s+(?<auction>\d+)\s*$", RegexOptions.IgnoreCase);
			this.configuration = configuration;
			this.state = state;
			this.log = log;
			revealChannel = configuration.Discord.RevealBidsChannelName;
		}

		public (bool success, int auctionId) ParseArgs(string messageContent)
		{
			Match match = pattern.Match(messageContent);
			if (!match.Success)
			{
				return (false, 0);
			}

			int auctionId = int.Parse(match.Groups["auction"].Value);
			return (true, auctionId);
		}

		public async Task<bool> TryInvokeAsync(IMessage message)
		{
			if (message.Channel.Name != revealChannel)
			{
				return false;
			}

			(bool success, int auctionId) = ParseArgs(message.Content);

			if (!success)
			{
				log.LogDebug("Did not match pattern.");
				return false;
			}

			if (!state.CompletedAuctions.TryGetValue(auctionId, out CompletedAuction auction))
			{
				string notFound = $"Could not find AuctionId '{auctionId}'.";
				log.LogWarning(notFound);
				await message.Channel.SendMessageAsync(notFound);
				return false;
			}

			StringBuilder builder = new StringBuilder();
			IEnumerable<string> winners = auction.WinningBids.Select(winner => $"**{winner.Bid.CharacterName}**");

			builder.AppendLine($"**[{auction.Auction.ShortDescription}]** Awarded to {string.Join(", ", winners)}");
			builder.AppendLine("Bids in ranked order:");

			builder.AppendLine("```css");
			List<AuctionBid> bids = auction.Auction.GetBids();
			bids.Sort();
			foreach (AuctionBid bid in bids)
			{
				builder.AppendLine(bid.RevealString);
			}
			builder.AppendLine("```");
			builder.AppendLine($"(AuctionID: {auction.ID}) {auction.Auction.Author.Username}");

			await message.Channel.SendMessageAsync(builder.ToString());
			return true;
		}
	}
}
