﻿using Roadie.Library.Identity;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Roadie.Library.Data
{
    [Table("playlist")]
    public partial class Playlist : NamedEntityBase
    {
        [NotMapped]
        public new string SortName { get; set; }

        [Column("Description")]
        [MaxLength(1000)]
        public string Description { get; set; }

        [Column("isPublic")]
        public bool IsPublic { get; set; }

        public ICollection<PlaylistTrack> Tracks { get; set; }

        public ApplicationUser User { get; set; }

        [Column("userId")]
        public int? UserId { get; set; }

        [Column("duration")]
        public int? Duration { get; set; }

        [Column("trackCount")]
        public short TrackCount { get; set; } 

        [Column("releaseCount")]
        public short ReleaseCount { get; set; }

        public ICollection<Comment> Comments { get; set; }

        public Playlist()
        {
            this.Comments = new HashSet<Comment>();
        }
    }
}