﻿using System;
using System.Xml.Serialization;

namespace DiscordDkpBot.Dkp.EqDkp.Xml
{
	[XmlType ("mdkp_Itempools")]
	public class MultiDkpItemPool
	{
		[XmlElement ("itempool_id")]
		public int Id { get; set; }
	}
}
