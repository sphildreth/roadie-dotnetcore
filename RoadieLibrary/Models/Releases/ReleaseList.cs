﻿using Mapster;
using Newtonsoft.Json;
using Roadie.Library.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace Roadie.Library.Models.Releases
{
    [Serializable]
    public class ReleaseList : EntityInfoModelBase
    {
        public DataToken Release { get; set; }
        public Guid ArtistId { get; set; }
        public string ArtistName { get; set; }
        public Image ArtistThumbnail { get; set; }
        public LibraryStatus LibraryStatus { get; set; }
        public IEnumerable<ReleaseMediaList> Media { get; set; }
        public short? Rating { get; set; }

        public string ReleaseDate
        {
            get
            {
                return this.ReleaseDateDateTime.HasValue ? this.ReleaseDateDateTime.Value.ToUniversalTime().ToString("yyyy-MM-dd") : null;
            }
        }

        [JsonIgnore]
        public DateTime? ReleaseDateDateTime { get; set; }

        public string ReleasePlayUrl { get; set; }

        public string ReleaseYear
        {
            get
            {
                return this.ReleaseDateDateTime.HasValue ? this.ReleaseDateDateTime.Value.ToUniversalTime().ToString("yyyy") : null;
            }
        }

        public Image Thumbnail { get; set; }
        public int? TrackCount { get; set; }
        public int? TrackPlayedCount { get; set; }
        public short? UserRating { get; set; }
    }
}
