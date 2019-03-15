﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;

using Discord;

using DiscordDkpBot.Configuration;
using DiscordDkpBot.Extensions;

using Microsoft.Extensions.Logging;

namespace DiscordDkpBot.Auctions
{
	public interface IAuctionProcessor
	{
		Task<AuctionBid> AddOrUpdateBid (string item, string character, string rank, int bid, IMessage message);
		Task<Auction> CancelAuction (string name, IMessage message);
		Task<AuctionBid> CancelBid (string item, IMessage message);
		Task<Auction> StartAuction (int? quantity, string name, int? minutes, IMessage messageChannel);
	}

	public class AuctionProcessor : IAuctionProcessor
	{
		private readonly AuctionState auctionState;
		private readonly DkpBotConfiguration configuration;
		private readonly ILogger<AuctionProcessor> log;
		private readonly Dictionary<string, RankConfiguration> ranks;

		public AuctionProcessor (DkpBotConfiguration configuration, AuctionState auctionState, ILogger<AuctionProcessor> log)
		{
			ranks = configuration.Ranks.ToDictionary(x => x.Name, x => x, StringComparer.OrdinalIgnoreCase);
			this.configuration = configuration;
			this.auctionState = auctionState;
			this.log = log;
		}

		public Task<AuctionBid> AddOrUpdateBid (string item, string character, string rank, int bid, IMessage message)
		{
			// First make sure we can make a valid bid out of it.
			if (!ranks.TryGetValue(rank, out RankConfiguration rankConfig))
			{
				throw new ArgumentException($"Rank {rank} does not exist.");
			}

			if (!auctionState.Auctions.TryGetValue(item, out Auction auction))
			{
				throw new AuctionNotFoundException(item);
			}

			AuctionBid newBid = auction.Bids.AddOrUpdate(new AuctionBid(auction, character, bid, rankConfig, message.Author));

			log.LogInformation($"Created bid: {newBid}");

			message.Channel.SendMessageAsync($"Bid accepted for **{newBid.Auction}**\n"
				+ $"```{newBid}```"
				+ $"If you win, you could pay up to **{newBid.BidAmount * newBid.Rank.PriceMultiplier}**.\n"
				+ $"If you wish to modify your bid before the auction completes, simply enter a new bid in the next {auction.MinutesRemaining:##.#} minutes.\n"
				+ "If you wish to cancel your bid use the following syntax:\n"
				+ $"```\"{newBid.Auction.Name}\" cancel```");

			return Task.FromResult(newBid);
		}

		public CompletedAuction CalculateWinners (Auction auction)
		{
			List<AuctionBid> bids = auction.Bids.ToList();
			log.LogTrace("Finding winners for {0} from bids submitted: ({1})", auction.DetailString, string.Join("', ", auction.Bids));
			List<WinningBid> winners = new List<WinningBid>();

			for (int i = 0; i < auction.Quantity; i++)
			{
				// Grab the first winner.
				WinningBid winner = CalculateWinner(bids);

				if (winner == null)
				{
					// No more winners to be found. we're done.
					break;
				}
				else
				{
					winners.Add(winner);

					// Remove that winner and go again.
					bids.Remove(winner.Bid);
				}
			}

			log.LogInformation("{0} found {1} winners: {2}", auction.DetailString, winners.Count, string.Join(", ", winners));

			return new CompletedAuction(auction, winners);
		}

		public async Task<Auction> CancelAuction (string name, IMessage message)
		{
			if (!auctionState.Auctions.TryRemove(name, out Auction auction))
			{
				throw new AuctionAlreadyExistsException($"Auction for {name} does not exists.");
			}
			auction.Stop();
			string cancelMessage = $"Cancelled auction: {auction.DetailString}.";

			await message.Channel.SendMessageAsync(cancelMessage);
			log.LogTrace(cancelMessage);

			return auction;
		}

		public Task<AuctionBid> CancelBid (string item, IMessage message)
		{
			if (!auctionState.Auctions.TryGetValue(item, out Auction auction))
			{
				throw new AuctionNotFoundException(item);
			}

			if (!auction.Bids.TryRemove(message.Author.Id, out AuctionBid bid))
			{
				throw new BidNotFoundException(item);
			}

			message.Channel.SendMessageAsync($"Bid cancelled for **{auction}**.\nYou have {auction.MinutesRemaining:##.#} minutes to re bid.");

			return Task.FromResult(bid);
		}

		public async Task<Auction> StartAuction (int? quantity, string name, int? minutes, IMessage message)
		{
			Auction auction = new Auction(auctionState.NextAuctionId, quantity ?? 1, name, minutes ?? configuration.DefaultAuctionDurationMinutes, message.Author);
			if (!auctionState.Auctions.TryAdd(auction.Name, auction))
			{
				throw new AuctionAlreadyExistsException($"Auction for {auction.Name} already exists.");
			}

			IUserMessage announcement = await message.Channel.SendMessageAsync(auction.Announcement);

			auction.Tick += async (o, s) => await announcement.ModifyAsync(m => m.Content = auction.Announcement);
			auction.Completed += async (o, s) =>
										{
											await announcement.ModifyAsync(m => m.Content = auction.ClosedText);
											await FinishAuction(auction, announcement);
										};

			auction.Start();

			log.LogTrace("Started auction: {0}", auction.DetailString);

			return auction;
		}

		private WinningBid CalculateWinner (List<AuctionBid> bids)
		{
			bids.Sort();
			log.LogTrace("Finding best winner from: ({0})", string.Join(", ", bids));
			List<AuctionBid> winningBids = new List<AuctionBid>();
			AuctionBid loser = null;

			foreach (AuctionBid bid in bids)
			{
				if (winningBids.None())
				{
					// First winner
					winningBids.Add(bid);
				}
				else if (winningBids.Last().CompareTo(bid) == 0)
				{
					// Tied for first winner.
					winningBids.Add(bid);
				}
				else
				{
					// You lose! Good DAY sir!
					loser = bid;
					break;
				}
			}

			log.LogTrace("Found {0} winners.", winningBids.Count);

			if (winningBids.None())
			{
				return null;
			}

			Random random = new Random();
			AuctionBid winner = winningBids.OrderBy(x => random.Next()).First();

			int applicableLooserBid;
			if (loser == null)
			{
				applicableLooserBid = 0;
			}
			else if (winner.Rank.MaxBid > loser.Rank.MaxBid && winner.BidAmount > loser.Rank.MaxBid)
			{
				// If our bid cap and is higher than their bid cap, and we bid over their cap. reduce their bid.
				applicableLooserBid = Math.Min(loser.BidAmount, loser.Rank.MaxBid);
			}
			else
			{
				// Otherwise, 
				applicableLooserBid = loser.BidAmount;
			}

			int price = applicableLooserBid + 1;
			int finalPrice = price * winner.Rank.PriceMultiplier;
			return new WinningBid(winner, finalPrice);
		}

		private Task FinishAuction (Auction auction, IUserMessage announcement)
		{
			auctionState.Auctions.TryRemove(auction.Name, out Auction _);

			CompletedAuction completedAuction = CalculateWinners(auction);

			auctionState.CompletedAuctions.TryAdd(completedAuction.ID, completedAuction);

			return announcement.Channel.SendMessageAsync(completedAuction.ToString());
		}
	}
}
