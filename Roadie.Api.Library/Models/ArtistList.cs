﻿using Roadie.Library.Enums;
using Roadie.Library.Models.Users;
using Roadie.Library.Utility;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Roadie.Library.Models
{
    [Serializable]
    [DebuggerDisplay("Artist Id {Id}, Name {Artist.Text}")]
    public sealed class ArtistList : EntityInfoModelBase
    {
        public DataToken Artist { get; set; }
        public bool IsValid => Id != Guid.Empty;
        public DateTime? LastPlayed { get; set; }
        public IEnumerable<string> MissingReleasesForCollection { get; set; }
        public int? PlayedCount { get; set; }
        public double? Rank { get; set; }
        public short? Rating { get; set; }
        public int? ReleaseCount { get; set; }
        public Image Thumbnail { get; set; }
        public int? TrackCount { get; set; }
        public UserArtist UserRating { get; set; }
        public Statuses? Status { get; set; }
        public string StatusVerbose => (Status ?? Statuses.Missing).ToString();

        public static ArtistList FromDataArtist(Data.Artist artist, Image thumbnail)
        {
            return new ArtistList
            {
                DatabaseId = artist.Id,
                Id = artist.RoadieId,
                Artist = new DataToken
                {
                    Text = artist.Name,
                    Value = artist.RoadieId.ToString()
                },
                Thumbnail = thumbnail,
                Rating = artist.Rating,
                Rank = SafeParser.ToNumber<double?>(artist.Rank),
                CreatedDate = artist.CreatedDate,
                LastUpdated = artist.LastUpdated,
                LastPlayed = artist.LastPlayed,
                PlayedCount = artist.PlayedCount,
                ReleaseCount = artist.ReleaseCount,
                TrackCount = artist.TrackCount,
                SortName = artist.SortName,
                Status = artist.Status
            };
        }
    }

    public class ArtistListComparer : IEqualityComparer<ArtistList>
    {
        public bool Equals(ArtistList x, ArtistList y)
        {
            if (x == null && y == null)
                return true;
            if (x != null && y == null || x == null && y != null) return false;
            return x.DatabaseId.Equals(y.DatabaseId) && x.Id.Equals(y.Id);
        }

        public int GetHashCode(ArtistList item)
        {
            return item.Id.GetHashCode();
        }
    }
}