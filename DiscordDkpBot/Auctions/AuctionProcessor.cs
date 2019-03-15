﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Timers;

using Discord.WebSocket;

using DiscordDkpBot.Configuration;
using DiscordDkpBot.Extensions;

using Microsoft.Extensions.Logging;

namespace DiscordDkpBot.Auctions
{
	public interface IAuctionProcessor
	{
		Auction StartAuction (int? quantity, string name, int? minutes, ISocketMessageChannel messageChannel, SocketUser author);
		AuctionBid CreateBid (string item, string character, string rank, int bid, SocketUser messageAuthor);
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

		public Auction StartAuction (int? quantity, string name, int? minutes, ISocketMessageChannel channel, SocketUser author)
		{
			Auction auction = CreateAuction(quantity, name, minutes, author);

			StartAuctionTimers(auction, channel);

			log.LogTrace("Started Auction: {0}", auction.DetailString);

			return auction;
		}

		private void StartAuctionTimers (Auction auction,ISocketMessageChannel channel)
		{
			// Completion Timer
			Timer timer = new Timer(TimeSpan.FromMinutes(Math.Max(1, auction.Minutes)).TotalMilliseconds);
			timer.AutoReset = false;
			timer.Elapsed += (o, s) => FinishAuction(auction, channel);
			timer.Start();
		}

		private Auction CreateAuction (int? quantity, string name, int? minutes, SocketUser author)
		{
			Auction auction = new Auction(auctionState.NextAuctionId, quantity ?? 1, name, minutes ?? configuration.DefaultAuctionDurationMinutes, author);
			if (!auctionState.Auctions.TryAdd(auction.Name, auction))
			{
				throw new AuctionAlreadyExistsException($"Auction for {auction.Name} already exists.");
			}
			return auction;
		}

		public AuctionBid CreateBid (string item, string character, string rank, int bid, SocketUser messageAuthor)
		{
			// First make sure we can make a valid bid out of it.
			if (!ranks.TryGetValue(rank, out RankConfiguration rankConfig))
			{
				throw new ArgumentException($"Rank {rank} does not exist.");
			}

			if (!auctionState.Auctions.TryGetValue(item, out Auction auction))
			{
				throw new AuctionNotFoundException($"Could not find auction '{item}'.");
			}

			AuctionBid newBid = auction.Bids.AddOrUpdate(new AuctionBid(auction, character, bid, rankConfig, messageAuthor));

			log.LogInformation($"Created bid: {newBid}");

			return newBid;
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
			if (winner.Rank.MaxBid > loser.Rank.MaxBid && winner.BidAmount > loser.Rank.MaxBid)
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

		private void FinishAuction (Auction auction, ISocketMessageChannel channel)
		{
			auctionState.Auctions.TryRemove(auction.Name, out Auction _);

			CompletedAuction completedAuction = CalculateWinners(auction);

			auctionState.CompletedAuctions.TryAdd(completedAuction.ID, completedAuction);

			channel.SendMessageAsync(completedAuction.ToString());
		}
	}
}
