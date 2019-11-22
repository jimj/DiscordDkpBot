using System;
using System.Collections.Generic;
using System.Linq;
using System.Timers;

using Discord;

using DiscordDkpBot.Configuration;
using DiscordDkpBot.Dkp.EqDkpPlus.Xml;

namespace DiscordDkpBot.Auctions
{
	public class Auction
	{
		private readonly Timer timer;
		public IUser Author { get; }
		private object bidsLock = new object();
		private BidCollection Bids { get; } = new BidCollection();
		public string CancelledText => $"Cancelled auction: {ShortDescription}.";
		public IMessageChannel Channel { get; }
		public string ClosedText => $"**[{ShortDescription}]** Bids are now closed.";
		public string DetailDescription => $"({ID}) {Quantity}x {Name} for {MinutesRemaining} min.";
		public int ID { get; }
		public double MinutesRemaining { get; private set; }
		public string Name { get; }
		public int Quantity { get; }
		public RaidInfo Raid { get; }
		public string ShortDescription => $"{Quantity}x {Name}";
		public event Action<object, Auction> Completed;
		public event Action<object, Auction> Tick;
		public bool IsComplete { get; private set; } = false;

		public Auction (int id, int quantity, string name, double minutesRemaining, RaidInfo raid, IMessage message)
		{
			ID = id;
			Name = name;
			Quantity = quantity;
			MinutesRemaining = minutesRemaining;
			Author = message.Author;
			Channel = message.Channel;
			Raid = raid;

			timer = new Timer(TimeSpan.FromMinutes(0.5).TotalMilliseconds);
			timer.AutoReset = true;
			timer.Elapsed += OnTick;
		}

		public string GetAnnouncementText (IEnumerable<RankConfiguration> ranks)
		{
			return $"**[{ShortDescription}]**\nBids are open for **{ShortDescription}** for **{MinutesRemaining}** minutes.\n```\"{Name}\" character 69 {string.Join("/", ranks.Select(x => x.Name))}```";
		}

		public void Start ()
		{
			timer.Start();
		}

		public void Stop ()
		{
			timer.Stop();
		}

		public override string ToString ()
		{
			return ShortDescription;
		}

		private void OnTick (object sender, ElapsedEventArgs e)
		{
			MinutesRemaining -= 0.5;

			if (MinutesRemaining > 0)
			{
				Tick?.Invoke(timer, this);
			}
			else
			{
				timer.Stop();

				Completed?.Invoke(timer, this);
			}
		}

		public AuctionBid AddOrUpdateBid (AuctionBid bid)
		{
			lock (bidsLock)
			{
				if (IsComplete)
				{
					// If we're done they missed out
					throw new AuctionNotFoundException(Name);
				}
				Bids.AddOrUpdate(bid);
			}
			return bid;

		}

		public int GetBid (int characterId)
		{
			return Bids.Where(b => b.CharacterId == characterId).Sum(x => x.BidAmount);
		}

		public List<AuctionBid> GetBids()
		{
			lock (bidsLock)
			{
				return Bids.ToList();
			}
		}

		public bool TryRemoveBid(ulong authorId, out AuctionBid auctionBid)
		{
			lock (bidsLock)
			{
				return Bids.TryRemove(authorId, out auctionBid);
			}
		}
	}
}
