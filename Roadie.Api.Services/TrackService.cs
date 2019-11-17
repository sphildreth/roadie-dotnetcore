﻿using Mapster;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Roadie.Library;
using Roadie.Library.Caching;
using Roadie.Library.Configuration;
using Roadie.Library.Data.Context;
using Roadie.Library.Encoding;
using Roadie.Library.Enums;
using Roadie.Library.Extensions;
using Roadie.Library.Identity;
using Roadie.Library.Imaging;
using Roadie.Library.MetaData.Audio;
using Roadie.Library.Models;
using Roadie.Library.Models.Pagination;
using Roadie.Library.Models.Releases;
using Roadie.Library.Models.Statistics;
using Roadie.Library.Models.Users;
using Roadie.Library.Utility;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Threading.Tasks;
using data = Roadie.Library.Data;

namespace Roadie.Api.Services
{
    public class TrackService : ServiceBase, ITrackService
    {
        private IAdminService AdminService { get; }

        private IAudioMetaDataHelper AudioMetaDataHelper { get; }

        private IBookmarkService BookmarkService { get; }

        public TrackService(IRoadieSettings configuration, IHttpEncoder httpEncoder, IHttpContext httpContext,
                            IRoadieDbContext dbContext, ICacheManager cacheManager, ILogger<TrackService> logger,
                            IBookmarkService bookmarkService, IAdminService adminService, IAudioMetaDataHelper audioMetaDataHelper)
            : base(configuration, httpEncoder, dbContext, cacheManager, logger, httpContext)
        {
            BookmarkService = bookmarkService;
            AudioMetaDataHelper = audioMetaDataHelper;
            AdminService = adminService;
        }

        public TrackService(IRoadieSettings configuration, IRoadieDbContext dbContext, ICacheManager cacheManager, ILogger logger)
            : base(configuration, null, dbContext, cacheManager, logger, null)
        {
        }

        public static long DetermineByteEndFromHeaders(IHeaderDictionary headers, long fileLength)
        {
            var defaultFileLength = fileLength - 1;
            if (headers == null || !headers.Any(x => x.Key == "Range"))
            {
                return defaultFileLength;
            }

            long? result = null;
            var rangeHeader = headers["Range"];
            string rangeEnd = null;
            var rangeBegin = rangeHeader.FirstOrDefault();
            if (!string.IsNullOrEmpty(rangeBegin))
            {
                //bytes=0-
                rangeBegin = rangeBegin.Replace("bytes=", "");
                var parts = rangeBegin.Split('-');
                rangeBegin = parts[0];
                if (parts.Length > 1)
                {
                    rangeEnd = parts[1];
                }

                if (!string.IsNullOrEmpty(rangeEnd))
                {
                    result = long.TryParse(rangeEnd, out var outValue) ? (int?)outValue : null;
                }
            }

            return result ?? defaultFileLength;
        }

        public static long DetermineByteStartFromHeaders(IHeaderDictionary headers)
        {
            if (headers == null || !headers.Any(x => x.Key == "Range"))
            {
                return 0;
            }

            long result = 0;
            var rangeHeader = headers["Range"];
            var rangeBegin = rangeHeader.FirstOrDefault();
            if (!string.IsNullOrEmpty(rangeBegin))
            {
                //bytes=0-
                rangeBegin = rangeBegin.Replace("bytes=", "");
                var parts = rangeBegin.Split('-');
                rangeBegin = parts[0];
                if (!string.IsNullOrEmpty(rangeBegin))
                {
                    long.TryParse(rangeBegin, out result);
                }
            }

            return result;
        }

        public async Task<OperationResult<Track>> ById(User roadieUser, Guid id, IEnumerable<string> includes)
        {
            var timings = new Dictionary<string, long>();
            var tsw = new Stopwatch();

            var sw = Stopwatch.StartNew();
            sw.Start();
            var cacheKey = string.Format("urn:track_by_id_operation:{0}:{1}", id, includes == null ? "0" : string.Join("|", includes));
            var result = await CacheManager.GetAsync(cacheKey, async () =>
            {
                tsw.Restart();
                var rr = await TrackByIdAction(id, includes);
                tsw.Stop();
                timings.Add("TrackByIdAction", tsw.ElapsedMilliseconds);
                return rr;
            }, data.Track.CacheRegionUrn(id));
            if (result?.Data != null && roadieUser != null)
            {
                tsw.Restart();
                var user = GetUser(roadieUser.UserId);
                tsw.Stop();
                timings.Add("getUser", tsw.ElapsedMilliseconds);

                tsw.Restart();
                var track = GetTrack(id);
                tsw.Stop();
                timings.Add("getTrack", tsw.ElapsedMilliseconds);

                result.Data.TrackPlayUrl = MakeTrackPlayUrl(user, HttpContext.BaseUrl, track.Id, track.RoadieId);

                tsw.Restart();
                var userBookmarkResult = await BookmarkService.List(roadieUser, new PagedRequest(), false, BookmarkType.Track);
                if (userBookmarkResult.IsSuccess)
                {
                    result.Data.UserBookmarked =
                        userBookmarkResult?.Rows?.FirstOrDefault(x => x.Bookmark.Value == track.RoadieId.ToString()) !=
                        null;
                }
                tsw.Stop();
                timings.Add("userBookmarks", tsw.ElapsedMilliseconds);

                tsw.Restart();
                var userTrack = DbContext.UserTracks.FirstOrDefault(x => x.TrackId == track.Id && x.UserId == roadieUser.Id);
                if (userTrack != null)
                {
                    result.Data.UserRating = new UserTrack
                    {
                        Rating = userTrack.Rating,
                        IsDisliked = userTrack.IsDisliked ?? false,
                        IsFavorite = userTrack.IsFavorite ?? false,
                        LastPlayed = userTrack.LastPlayed,
                        PlayedCount = userTrack.PlayedCount
                    };
                }
                tsw.Stop();
                timings.Add("userTracks", tsw.ElapsedMilliseconds);

                if (result.Data.Comments.Any())
                {
                    tsw.Restart();
                    var commentIds = result.Data.Comments.Select(x => x.DatabaseId).ToArray();
                    var userCommentReactions = (from cr in DbContext.CommentReactions
                                                where commentIds.Contains(cr.CommentId)
                                                where cr.UserId == roadieUser.Id
                                                select cr).ToArray();
                    foreach (var comment in result.Data.Comments)
                    {
                        var userCommentReaction =
                            userCommentReactions.FirstOrDefault(x => x.CommentId == comment.DatabaseId);
                        comment.IsDisliked = userCommentReaction?.ReactionValue == CommentReaction.Dislike;
                        comment.IsLiked = userCommentReaction?.ReactionValue == CommentReaction.Like;
                    }
                    tsw.Stop();
                    timings.Add("userComments", tsw.ElapsedMilliseconds);
                }
            }

            sw.Stop();
            Logger.LogInformation($"ById Track: `{ result?.Data }`, includes [{ includes.ToCSV() }], timings [{ timings.ToTimings() }]");
            return new OperationResult<Track>(result.Messages)
            {
                Data = result?.Data,
                Errors = result?.Errors,
                IsNotFoundResult = result?.IsNotFoundResult ?? false,
                IsSuccess = result?.IsSuccess ?? false,
                OperationTime = sw.ElapsedMilliseconds
            };
        }

        public async Task<Library.Models.Pagination.PagedResult<TrackList>> List(PagedRequest request, User roadieUser, bool? doRandomize = false, Guid? releaseId = null)
        {
            try
            {
                var sw = new Stopwatch();
                sw.Start();

                int? rowCount = null;

                if (!string.IsNullOrEmpty(request.Sort))
                {
                    request.Sort = request.Sort.Replace("Release.Text", "Release.Release.Text");
                }

                var favoriteTrackIds = new int[0].AsQueryable();
                if (request.FilterFavoriteOnly)
                {
                    favoriteTrackIds = from t in DbContext.Tracks
                                       join ut in DbContext.UserTracks on t.Id equals ut.TrackId
                                       where ut.UserId == roadieUser.Id
                                       where ut.IsFavorite ?? false
                                       select t.Id;
                }

                var playListTrackPositions = new Dictionary<int, int>();
                var playlistTrackIds = new int[0];
                if (request.FilterToPlaylistId.HasValue)
                {
                    var playlistTrackInfos = (from plt in DbContext.PlaylistTracks
                                              join p in DbContext.Playlists on plt.PlayListId equals p.Id
                                              join t in DbContext.Tracks on plt.TrackId equals t.Id
                                              where p.RoadieId == request.FilterToPlaylistId.Value
                                              orderby plt.ListNumber
                                              select new
                                              {
                                                  plt.ListNumber,
                                                  t.Id
                                              }).ToArray();

                    rowCount = playlistTrackInfos.Count();
                    playListTrackPositions = playlistTrackInfos
                                              .Skip(request.SkipValue)
                                              .Take(request.LimitValue)
                                              .ToDictionary(x => x.Id, x => x.ListNumber);
                    playlistTrackIds = playListTrackPositions.Select(x => x.Key).ToArray();
                    request.Sort = "TrackNumber";
                    request.Order = "ASC";
                    request.Page = 1; // Set back to first or it skips already paged tracks for playlist
                    request.SkipValue = 0;
                }

                var collectionTrackIds = new int[0];
                if (request.FilterToCollectionId.HasValue)
                {
                    request.Limit = roadieUser?.PlayerTrackLimit ?? 50;

                    collectionTrackIds = (from cr in DbContext.CollectionReleases
                                          join c in DbContext.Collections on cr.CollectionId equals c.Id
                                          join r in DbContext.Releases on cr.ReleaseId equals r.Id
                                          join rm in DbContext.ReleaseMedias on r.Id equals rm.ReleaseId
                                          join t in DbContext.Tracks on rm.Id equals t.ReleaseMediaId
                                          where c.RoadieId == request.FilterToCollectionId.Value
                                          orderby cr.ListNumber, rm.MediaNumber, t.TrackNumber
                                          select t.Id)
                                          .Skip(request.SkipValue)
                                          .Take(request.LimitValue)
                                          .ToArray();
                }

                IQueryable<int> topTrackids = null;
                if (request.FilterTopPlayedOnly)
                {
                    // Get request number of top played songs for artist
                    topTrackids = (from t in DbContext.Tracks
                                   join ut in DbContext.UserTracks on t.Id equals ut.TrackId
                                   join rm in DbContext.ReleaseMedias on t.ReleaseMediaId equals rm.Id
                                   join r in DbContext.Releases on rm.ReleaseId equals r.Id
                                   join a in DbContext.Artists on r.ArtistId equals a.Id
                                   where a.RoadieId == request.FilterToArtistId
                                   orderby ut.PlayedCount descending
                                   select t.Id
                                   ).Skip(request.SkipValue)
                                    .Take(request.LimitValue);
                }

                int[] randomTrackIds = null;
                SortedDictionary<int, int> randomTrackData = null;
                if (doRandomize ?? false)
                {
                    var randomLimit = roadieUser?.RandomReleaseLimit ?? request.LimitValue;
                    randomTrackData = await DbContext.RandomTrackIds(roadieUser?.Id ?? -1, randomLimit, request.FilterFavoriteOnly, request.FilterRatedOnly);
                    randomTrackIds = randomTrackData.Select(x => x.Value).ToArray();
                    rowCount = DbContext.Releases.Count();
                }

                Guid?[] filterToTrackIds = null;
                if (request.FilterToTrackId.HasValue || request.FilterToTrackIds != null)
                {
                    var f = new List<Guid?>();
                    if (request.FilterToTrackId.HasValue)
                    {
                        f.Add(request.FilterToTrackId);
                    }

                    if (request.FilterToTrackIds != null)
                    {
                        foreach (var ft in request.FilterToTrackIds)
                        {
                            if (!f.Contains(ft))
                            {
                                f.Add(ft);
                            }
                        }
                    }

                    filterToTrackIds = f.ToArray();
                }

                var normalizedFilterValue = !string.IsNullOrEmpty(request.FilterValue) ? request.FilterValue.ToAlphanumericName() : null;

                var isEqualFilter = false;
                if (!string.IsNullOrEmpty(request.FilterValue))
                {
                    var filter = request.FilterValue;
                    // if filter string is wrapped in quotes then is an exact not like search, e.g. "Diana Ross" should not return "Diana Ross & The Supremes"
                    if (filter.StartsWith('"') && filter.EndsWith('"'))
                    {
                        isEqualFilter = true;
                        request.Filter = filter.Substring(1, filter.Length - 2);
                    }
                }

                // Did this for performance against the Track table, with just * selects the table scans are too much of a performance hit.
                var resultQuery = from t in DbContext.Tracks
                                  join rm in DbContext.ReleaseMedias on t.ReleaseMediaId equals rm.Id
                                  join r in DbContext.Releases on rm.ReleaseId equals r.Id
                                  join releaseArtist in DbContext.Artists on r.ArtistId equals releaseArtist.Id
                                  join trackArtist in DbContext.Artists on t.ArtistId equals trackArtist.Id into tas
                                  from trackArtist in tas.DefaultIfEmpty()
                                  where t.Hash != null
                                  where randomTrackIds == null || randomTrackIds.Contains(t.Id)
                                  where filterToTrackIds == null || filterToTrackIds.Contains(t.RoadieId)
                                  where releaseId == null || r.RoadieId == releaseId
                                  where request.FilterMinimumRating == null || t.Rating >= request.FilterMinimumRating.Value
                                  where request.FilterValue == "" || (trackArtist != null && trackArtist.Name.Contains(request.FilterValue)) || t.Title.Contains(request.FilterValue) || t.AlternateNames.Contains(request.FilterValue) || t.AlternateNames.Contains(normalizedFilterValue) || t.PartTitles.Contains(request.FilterValue)
                                  where !isEqualFilter || t.Title.Equals(request.FilterValue) || t.AlternateNames.Equals(request.FilterValue) || t.AlternateNames.Equals(normalizedFilterValue) || t.PartTitles.Equals(request.FilterValue)
                                  where !request.FilterFavoriteOnly || favoriteTrackIds.Contains(t.Id)
                                  where request.FilterToPlaylistId == null || playlistTrackIds.Contains(t.Id)
                                  where !request.FilterTopPlayedOnly || topTrackids.Contains(t.Id)
                                  where request.FilterToArtistId == null || ((t.TrackArtist != null && t.TrackArtist.RoadieId == request.FilterToArtistId) || r.Artist.RoadieId == request.FilterToArtistId)
                                  where !request.IsHistoryRequest || t.PlayedCount > 0
                                  where request.FilterToCollectionId == null || collectionTrackIds.Contains(t.Id)
                                  select new
                                  {
                                      ti = new
                                      {
                                          t.Id,
                                          t.RoadieId,
                                          t.CreatedDate,
                                          t.LastUpdated,
                                          t.LastPlayed,
                                          t.Duration,
                                          t.FileSize,
                                          t.PlayedCount,
                                          t.PartTitles,
                                          t.Rating,
                                          t.Tags,
                                          t.TrackNumber,
                                          t.Status,
                                          t.Title
                                      },
                                      rmi = new
                                      {
                                          rm.MediaNumber
                                      },
                                      rl = new ReleaseList
                                      {
                                          DatabaseId = r.Id,
                                          Id = r.RoadieId,
                                          Artist = new DataToken
                                          {
                                              Value = releaseArtist.RoadieId.ToString(),
                                              Text = releaseArtist.Name
                                          },
                                          Release = new DataToken
                                          {
                                              Text = r.Title,
                                              Value = r.RoadieId.ToString()
                                          },
                                          ArtistThumbnail = MakeArtistThumbnailImage(Configuration, HttpContext, releaseArtist.RoadieId),
                                          CreatedDate = r.CreatedDate,
                                          Duration = r.Duration,
                                          LastPlayed = r.LastPlayed,
                                          LastUpdated = r.LastUpdated,
                                          LibraryStatus = r.LibraryStatus,
                                          MediaCount = r.MediaCount,
                                          Rating = r.Rating,
                                          Rank = r.Rank,
                                          ReleaseDateDateTime = r.ReleaseDate,
                                          ReleasePlayUrl = $"{HttpContext.BaseUrl}/play/release/{r.RoadieId}",
                                          Status = r.Status,
                                          Thumbnail = MakeReleaseThumbnailImage(Configuration, HttpContext, r.RoadieId),
                                          TrackCount = r.TrackCount,
                                          TrackPlayedCount = r.PlayedCount
                                      },
                                      ta = trackArtist == null
                                          ? null
                                          : new ArtistList
                                          {
                                              DatabaseId = trackArtist.Id,
                                              Id = trackArtist.RoadieId,
                                              Artist = new DataToken
                                              { Text = trackArtist.Name, Value = trackArtist.RoadieId.ToString() },
                                              Rating = trackArtist.Rating,
                                              Rank = trackArtist.Rank,
                                              CreatedDate = trackArtist.CreatedDate,
                                              LastUpdated = trackArtist.LastUpdated,
                                              LastPlayed = trackArtist.LastPlayed,
                                              PlayedCount = trackArtist.PlayedCount,
                                              ReleaseCount = trackArtist.ReleaseCount,
                                              TrackCount = trackArtist.TrackCount,
                                              SortName = trackArtist.SortName,
                                              Thumbnail = MakeArtistThumbnailImage(Configuration, HttpContext, trackArtist.RoadieId)
                                          },
                                      ra = new ArtistList
                                      {
                                          DatabaseId = releaseArtist.Id,
                                          Id = releaseArtist.RoadieId,
                                          Artist = new DataToken
                                          { Text = releaseArtist.Name, Value = releaseArtist.RoadieId.ToString() },
                                          Rating = releaseArtist.Rating,
                                          Rank = releaseArtist.Rank,
                                          CreatedDate = releaseArtist.CreatedDate,
                                          LastUpdated = releaseArtist.LastUpdated,
                                          LastPlayed = releaseArtist.LastPlayed,
                                          PlayedCount = releaseArtist.PlayedCount,
                                          ReleaseCount = releaseArtist.ReleaseCount,
                                          TrackCount = releaseArtist.TrackCount,
                                          SortName = releaseArtist.SortName,
                                          Thumbnail = MakeArtistThumbnailImage(Configuration, HttpContext, releaseArtist.RoadieId)
                                      }
                                  };

                if (!string.IsNullOrEmpty(request.FilterValue))
                {
                    if (request.FilterValue.StartsWith("#"))
                    {
                        // Find any releases by tags
                        var tagValue = request.FilterValue.Replace("#", "");
                        resultQuery = resultQuery.Where(x => x.ti.Tags != null && x.ti.Tags.Contains(tagValue));
                    }
                }

                var user = GetUser(roadieUser.UserId);
                var result = resultQuery.Select(x =>
                    new TrackList
                    {
                        DatabaseId = x.ti.Id,
                        Id = x.ti.RoadieId,
                        Track = new DataToken
                        {
                            Text = x.ti.Title,
                            Value = x.ti.RoadieId.ToString()
                        },
                        Status = x.ti.Status,
                        Artist = x.ra,
                        CreatedDate = x.ti.CreatedDate,
                        Duration = x.ti.Duration,
                        FileSize = x.ti.FileSize,
                        LastPlayed = x.ti.LastPlayed,
                        LastUpdated = x.ti.LastUpdated,
                        MediaNumber = x.rmi.MediaNumber,
                        PlayedCount = x.ti.PlayedCount,
                        PartTitles = x.ti.PartTitles,
                        Rating = x.ti.Rating,
                        Release = x.rl,
                        ReleaseDate = x.rl.ReleaseDateDateTime,
                        Thumbnail = MakeTrackThumbnailImage(Configuration, HttpContext, x.ti.RoadieId),
                        Title = x.ti.Title,
                        TrackArtist = x.ta,
                        TrackNumber = x.ti.TrackNumber,
                        TrackPlayUrl = MakeTrackPlayUrl(user, HttpContext.BaseUrl, x.ti.Id, x.ti.RoadieId)
                    });
                string sortBy = null;

                rowCount = rowCount ?? result.Count();
                TrackList[] rows = null;

                if (!doRandomize ?? false)
                {
                    if (request.Action == User.ActionKeyUserRated)
                    {
                        sortBy = string.IsNullOrEmpty(request.Sort)
                            ? request.OrderValue(new Dictionary<string, string> { { "UserTrack.Rating", "DESC" }, { "MediaNumber", "ASC" }, { "TrackNumber", "ASC" } })
                            : request.OrderValue();
                    }
                    else
                    {
                        if (request.Sort == "Rating")
                        {
                            // The request is to sort tracks by Rating if the artist only has a few tracks rated then order by those then order by played (put most popular after top rated)
                            sortBy = request.OrderValue(new Dictionary<string, string> { { "Rating", request.Order }, { "PlayedCount", request.Order } });
                        }
                        else
                        {
                            sortBy = string.IsNullOrEmpty(request.Sort)
                                ? request.OrderValue(new Dictionary<string, string> { { "Release.Release.Text", "ASC" }, { "MediaNumber", "ASC" }, { "TrackNumber", "ASC" } })
                                : request.OrderValue();
                        }
                    }
                }

                if (doRandomize ?? false)
                {
                    var resultData = result.ToArray();
                    rows = (from r in resultData
                            join ra in randomTrackData on r.DatabaseId equals ra.Value
                            orderby ra.Key
                            select r
                           ).ToArray();
                }
                else
                {
                    rows = result
                            .OrderBy(sortBy)
                            .Skip(request.SkipValue)
                            .Take(request.LimitValue)
                            .ToArray();
                }
                if (rows.Any() && roadieUser != null)
                {
                    var rowIds = rows.Select(x => x.DatabaseId).ToArray();
                    var userTrackRatings = (from ut in DbContext.UserTracks
                                            where ut.UserId == roadieUser.Id
                                            where rowIds.Contains(ut.TrackId)
                                            select ut).ToArray();
                    foreach (var userTrackRating in userTrackRatings)
                    {
                        var row = rows.FirstOrDefault(x => x.DatabaseId == userTrackRating.TrackId);
                        if (row != null)
                        {
                            row.UserRating = new UserTrack
                            {
                                IsDisliked = userTrackRating.IsDisliked ?? false,
                                IsFavorite = userTrackRating.IsFavorite ?? false,
                                Rating = userTrackRating.Rating,
                                LastPlayed = userTrackRating.LastPlayed,
                                PlayedCount = userTrackRating.PlayedCount
                            };
                        }
                    }

                    var releaseIds = rows.Select(x => x.Release.DatabaseId).Distinct().ToArray();
                    var userReleaseRatings = (from ur in DbContext.UserReleases
                                              where ur.UserId == roadieUser.Id
                                              where releaseIds.Contains(ur.ReleaseId)
                                              select ur).ToArray();
                    foreach (var userReleaseRating in userReleaseRatings)
                    {
                        foreach (var row in rows.Where(x => x.Release.DatabaseId == userReleaseRating.ReleaseId))
                        {
                            row.Release.UserRating = userReleaseRating.Adapt<UserRelease>();
                        }
                    }

                    var artistIds = rows.Select(x => x.Artist.DatabaseId).ToArray();
                    if (artistIds != null && artistIds.Any())
                    {
                        var userArtistRatings = (from ua in DbContext.UserArtists
                                                 where ua.UserId == roadieUser.Id
                                                 where artistIds.Contains(ua.ArtistId)
                                                 select ua).ToArray();
                        foreach (var userArtistRating in userArtistRatings)
                        {
                            foreach (var artistTrack in rows.Where(
                                x => x.Artist.DatabaseId == userArtistRating.ArtistId))
                            {
                                artistTrack.Artist.UserRating = userArtistRating.Adapt<UserArtist>();
                            }
                        }
                    }

                    var trackArtistIds = rows.Where(x => x.TrackArtist != null).Select(x => x.TrackArtist.DatabaseId).ToArray();
                    if (trackArtistIds != null && trackArtistIds.Any())
                    {
                        var userTrackArtistRatings = (from ua in DbContext.UserArtists
                                                      where ua.UserId == roadieUser.Id
                                                      where trackArtistIds.Contains(ua.ArtistId)
                                                      select ua).ToArray();
                        if (userTrackArtistRatings != null && userTrackArtistRatings.Any())
                        {
                            foreach (var userTrackArtistRating in userTrackArtistRatings)
                            {
                                foreach (var artistTrack in rows.Where(x =>
                                    x.TrackArtist != null &&
                                    x.TrackArtist.DatabaseId == userTrackArtistRating.ArtistId))
                                {
                                    artistTrack.Artist.UserRating = userTrackArtistRating.Adapt<UserArtist>();
                                }
                            }
                        }
                    }
                }

                if (rows.Any())
                {
                    var rowIds = rows.Select(x => x.DatabaseId).ToArray();
                    var favoriteUserTrackRatings = (from ut in DbContext.UserTracks
                                                    where ut.IsFavorite ?? false
                                                    where rowIds.Contains(ut.TrackId)
                                                    select ut).ToArray();
                    foreach (var row in rows)
                    {
                        row.FavoriteCount = favoriteUserTrackRatings.Where(x => x.TrackId == row.DatabaseId).Count();
                        row.TrackNumber = playListTrackPositions.ContainsKey(row.DatabaseId) ? playListTrackPositions[row.DatabaseId] : row.TrackNumber;
                    }
                }

                if(playListTrackPositions.Any())
                {
                    rows = rows.OrderBy(x => x.TrackNumber).ToArray();
                }

                sw.Stop();
                return new Library.Models.Pagination.PagedResult<TrackList>
                {
                    TotalCount = rowCount ?? 0,
                    CurrentPage = request.PageValue,
                    TotalPages = (int)Math.Ceiling((double)rowCount / request.LimitValue),
                    OperationTime = sw.ElapsedMilliseconds,
                    Rows = rows
                };
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error In List, Request [{0}], User [{1}]", JsonConvert.SerializeObject(request), roadieUser);
                return new Library.Models.Pagination.PagedResult<TrackList>
                {
                    Message = "An Error has occured"
                };
            }
        }

        /// <summary>
        ///     Fast as possible check if exists and return minimum information on Track
        /// </summary>
        public OperationResult<Track> StreamCheckAndInfo(User roadieUser, Guid id)
        {
            var track = DbContext.Tracks.FirstOrDefault(x => x.RoadieId == id);
            if (track == null)
            {
                return new OperationResult<Track>(true, string.Format("Track Not Found [{0}]", id));
            }

            return new OperationResult<Track>
            {
                Data = track.Adapt<Track>(),
                IsSuccess = true
            };
        }

        public async Task<OperationResult<TrackStreamInfo>> TrackStreamInfo(Guid trackId, long beginBytes, long endBytes, User roadieUser)
        {
            var track = DbContext.Tracks.FirstOrDefault(x => x.RoadieId == trackId);
            if (!(track?.IsValid ?? true))
            {
                // Not Found try recanning release
                var release = (from r in DbContext.Releases
                               join rm in DbContext.ReleaseMedias on r.Id equals rm.ReleaseId
                               where rm.Id == track.ReleaseMediaId
                               select r).FirstOrDefault();
                if (!release.IsLocked ?? false && roadieUser != null)
                {
                    await AdminService.ScanRelease(new ApplicationUser
                    {
                        Id = roadieUser.Id.Value
                    }, release.RoadieId, false, true);
                    track = DbContext.Tracks.FirstOrDefault(x => x.RoadieId == trackId);
                }
                else
                {
                    Logger.LogWarning($"TrackStreamInfo: Track [{ trackId }] was invalid but release [{ release.RoadieId }] is locked, did not rescan.");
                }
                if (track == null)
                {
                    return new OperationResult<TrackStreamInfo>($"TrackStreamInfo: Unable To Find Track [{trackId}]");
                }
                if (!track.IsValid)
                {
                    return new OperationResult<TrackStreamInfo>($"TrackStreamInfo: Invalid Track. Track Id [{trackId}], FilePath [{track.FilePath}], Filename [{track.FileName}]");
                }
            }
            string trackPath = null;
            try
            {
                trackPath = track.PathToTrack(Configuration);
            }
            catch (Exception ex)
            {
                return new OperationResult<TrackStreamInfo>(ex);
            }

            var trackFileInfo = new FileInfo(trackPath);
            if (!trackFileInfo.Exists)
            {
                // Not Found try recanning release
                var release = (from r in DbContext.Releases
                               join rm in DbContext.ReleaseMedias on r.Id equals rm.ReleaseId
                               where rm.Id == track.ReleaseMediaId
                               select r).FirstOrDefault();
                if (!release.IsLocked ?? false && roadieUser != null)
                {
                    await AdminService.ScanRelease(new ApplicationUser
                    {
                        Id = roadieUser.Id.Value
                    }, release.RoadieId, false, true);
                }

                track = DbContext.Tracks.FirstOrDefault(x => x.RoadieId == trackId);
                if (track == null)
                {
                    return new OperationResult<TrackStreamInfo>($"TrackStreamInfo: Unable To Find Track [{trackId}]");
                }

                try
                {
                    trackPath = track.PathToTrack(Configuration);
                }
                catch (Exception ex)
                {
                    return new OperationResult<TrackStreamInfo>(ex);
                }

                if (!trackFileInfo.Exists)
                {
                    track.UpdateTrackMissingFile();
                    await DbContext.SaveChangesAsync();
                    return new OperationResult<TrackStreamInfo>(
                        $"TrackStreamInfo: TrackId [{trackId}] Unable to Find Track [{trackFileInfo.FullName}]");
                }
            }

            var disableCaching = true;

            var contentDurationTimeSpan = TimeSpan.FromMilliseconds(track.Duration ?? 0);
            var info = new TrackStreamInfo
            {
                FileName = HttpEncoder?.UrlEncode(track.FileName).ToContentDispositionFriendly(),
                ContentDisposition = $"attachment; filename=\"{HttpEncoder?.UrlEncode(track.FileName).ToContentDispositionFriendly()}\"",
                ContentDuration = contentDurationTimeSpan.TotalSeconds.ToString()
            };
            var contentLength = endBytes - beginBytes + 1;
            info.Track = new DataToken
            {
                Text = track.Title,
                Value = track.RoadieId.ToString()
            };
            info.BeginBytes = beginBytes;
            info.EndBytes = endBytes;
            info.ContentRange = $"bytes {beginBytes}-{endBytes}/{contentLength}";
            info.ContentLength = contentLength.ToString();
            info.IsFullRequest = beginBytes == 0 && endBytes == trackFileInfo.Length - 1;
            info.IsEndRangeRequest = beginBytes > 0 && endBytes != trackFileInfo.Length - 1;
            info.LastModified = (track.LastUpdated ?? track.CreatedDate).ToString("R");
            if (!disableCaching)
            {
                var cacheTimeout = 86400; // 24 hours
                info.CacheControl = $"public, max-age={cacheTimeout.ToString()} ";
                info.Expires = DateTime.UtcNow.AddMinutes(cacheTimeout).ToString("R");
                info.Etag = track.Etag;
            }
            else
            {
                info.CacheControl = "no-store, must-revalidate, no-cache, max-age=0";
                info.Pragma = "no-cache";
                info.Expires = "Mon, 01 Jan 1990 00:00:00 GMT";
            }
            var bytesToRead = (int)(endBytes - beginBytes) + 1;
            var trackBytes = new byte[bytesToRead];
            using (var fs = trackFileInfo.OpenRead())
            {
                try
                {
                    fs.Seek(beginBytes, SeekOrigin.Begin);
                    var r = fs.Read(trackBytes, 0, bytesToRead);
                }
                catch (Exception ex)
                {
                    return new OperationResult<TrackStreamInfo>(ex);
                }
            }

            info.Bytes = trackBytes;
            return new OperationResult<TrackStreamInfo>
            {
                IsSuccess = true,
                Data = info
            };
        }

        public async Task<OperationResult<bool>> UpdateTrack(User user, Track model)
        {
            var didChangeTrack = false;
            var sw = new Stopwatch();
            sw.Start();
            var errors = new List<Exception>();
            var track = DbContext.Tracks
                .Include(x => x.ReleaseMedia)
                .Include(x => x.ReleaseMedia.Release)
                .Include(x => x.ReleaseMedia.Release.Artist)
                .FirstOrDefault(x => x.RoadieId == model.Id);
            if (track == null)
            {
                return new OperationResult<bool>(true, string.Format("Track Not Found [{0}]", model.Id));
            }

            try
            {
                var originalTitle = track.Title;
                var originalTrackNumber = track.TrackNumber;
                var originalFilename = track.PathToTrack(Configuration);
                var now = DateTime.UtcNow;
                track.IsLocked = model.IsLocked;
                track.Status = SafeParser.ToEnum<Statuses>(model.Status);
                track.Title = model.Title;
                track.AlternateNames = model.AlternateNamesList.ToDelimitedList();
                track.Rating = model.Rating;
                track.AmgId = model.AmgId;
                track.LastFMId = model.LastFMId;
                track.MusicBrainzId = model.MusicBrainzId;
                track.SpotifyId = model.SpotifyId;
                track.Tags = model.TagsList.ToDelimitedList();
                track.PartTitles = model.PartTitlesList == null || !model.PartTitlesList.Any()
                    ? null
                    : string.Join("\n", model.PartTitlesList);

                data.Artist trackArtist = null;
                if (model.TrackArtistToken != null)
                {
                    var artistId = SafeParser.ToGuid(model.TrackArtistToken.Value);
                    if (artistId.HasValue)
                    {
                        trackArtist = GetArtist(artistId.Value);
                        if (trackArtist != null)
                        {
                            track.ArtistId = trackArtist.Id;
                        }
                    }
                }
                else
                {
                    track.ArtistId = null;
                }

                var trackImage = ImageHelper.ImageDataFromUrl(model.NewThumbnailData);
                if (trackImage != null)
                {
                    // Save unaltered image to cover file
                    var trackThumbnailName = track.PathToTrackThumbnail(Configuration);
                    File.WriteAllBytes(trackThumbnailName, ImageHelper.ConvertToJpegFormat(trackImage));
                }

                // See if Title was changed if so then  modify DB Filename and rename track
                var shouldFileNameBeUpdated = originalTitle != track.Title || originalTrackNumber != track.TrackNumber;
                if (shouldFileNameBeUpdated)
                {
                    track.FileName = FolderPathHelper.TrackFileName(Configuration, track.Title, track.TrackNumber, track.ReleaseMedia.MediaNumber, track.ReleaseMedia.TrackCount);
                    File.Move(originalFilename, track.PathToTrack(Configuration));
                }
                track.LastUpdated = now;
                await DbContext.SaveChangesAsync();

                var trackFileInfo = new FileInfo(track.PathToTrack(Configuration));
                var audioMetaData = await AudioMetaDataHelper.GetInfo(trackFileInfo);
                if (audioMetaData != null)
                {
                    audioMetaData.Title = track.Title;
                    if (trackArtist != null)
                    {
                        audioMetaData.Artist = trackArtist.Name;
                    }
                    AudioMetaDataHelper.WriteTags(audioMetaData, trackFileInfo);
                }
                CacheManager.ClearRegion(track.CacheRegion);
                Logger.LogInformation($"UpdateTrack `{track}` By User `{user}`: Edited Track [{didChangeTrack}]");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
                errors.Add(ex);
            }

            sw.Stop();

            return new OperationResult<bool>
            {
                IsSuccess = !errors.Any(),
                Data = !errors.Any(),
                OperationTime = sw.ElapsedMilliseconds,
                Errors = errors
            };
        }

        private Task<OperationResult<Track>> TrackByIdAction(Guid id, IEnumerable<string> includes)
        {
            var timings = new Dictionary<string, long>();
            var tsw = new Stopwatch();

            var sw = Stopwatch.StartNew();
            sw.Start();

            tsw.Restart();
            var track = GetTrack(id);
            tsw.Stop();
            timings.Add("getTrack", tsw.ElapsedMilliseconds);

            if (track == null)
            {
                return Task.FromResult(new OperationResult<Track>(true, string.Format("Track Not Found [{0}]", id)));
            }
            tsw.Restart();
            var result = track.Adapt<Track>();
            result.IsLocked = (track.IsLocked ?? false) ||
                              (track.ReleaseMedia.IsLocked ?? false) ||
                              (track.ReleaseMedia.Release.IsLocked ?? false) ||
                              (track.ReleaseMedia.Release.Artist.IsLocked ?? false);
            result.Thumbnail = MakeTrackThumbnailImage(Configuration, HttpContext, id);
            result.MediumThumbnail = MakeThumbnailImage(Configuration, HttpContext, id, "track", Configuration.MediumImageSize.Width, Configuration.MediumImageSize.Height);
            result.ReleaseMediaId = track.ReleaseMedia.RoadieId.ToString();
            result.Artist = ArtistList.FromDataArtist(track.ReleaseMedia.Release.Artist,
                MakeArtistThumbnailImage(Configuration, HttpContext, track.ReleaseMedia.Release.Artist.RoadieId));
            result.ArtistThumbnail = MakeArtistThumbnailImage(Configuration, HttpContext, track.ReleaseMedia.Release.Artist.RoadieId);
            result.Release = ReleaseList.FromDataRelease(track.ReleaseMedia.Release, track.ReleaseMedia.Release.Artist,
                HttpContext.BaseUrl, MakeArtistThumbnailImage(Configuration, HttpContext, track.ReleaseMedia.Release.Artist.RoadieId),
                MakeReleaseThumbnailImage(Configuration, HttpContext, track.ReleaseMedia.Release.RoadieId));
            result.ReleaseThumbnail = MakeReleaseThumbnailImage(Configuration, HttpContext, track.ReleaseMedia.Release.RoadieId);
            tsw.Stop();
            timings.Add("adapt", tsw.ElapsedMilliseconds);
            if (track.ArtistId.HasValue)
            {
                tsw.Restart();
                var trackArtist = DbContext.Artists.FirstOrDefault(x => x.Id == track.ArtistId);
                if (trackArtist == null)
                {
                    Logger.LogWarning($"Unable to find Track Artist [{track.ArtistId}");
                }
                else
                {
                    result.TrackArtist =
                        ArtistList.FromDataArtist(trackArtist, MakeArtistThumbnailImage(Configuration, HttpContext, trackArtist.RoadieId));
                    result.TrackArtistToken = result.TrackArtist.Artist;
                    result.TrackArtistThumbnail = MakeArtistThumbnailImage(Configuration, HttpContext, trackArtist.RoadieId);
                }
                tsw.Stop();
                timings.Add("trackArtist", tsw.ElapsedMilliseconds);
            }

            if (includes != null && includes.Any())
            {
                if (includes.Contains("stats"))
                {
                    tsw.Restart();
                    result.Statistics = new TrackStatistics
                    {
                        FileSizeFormatted = ((long?)track.FileSize).ToFileSize(),
                        Time = new TimeInfo((decimal)track.Duration).ToFullFormattedString(),
                        PlayedCount = track.PlayedCount
                    };
                    var userTracks = (from t in DbContext.Tracks
                                      join ut in DbContext.UserTracks on t.Id equals ut.TrackId
                                      where t.Id == track.Id
                                      select ut).ToArray();
                    if (userTracks != null && userTracks.Any())
                    {
                        result.Statistics.DislikedCount = userTracks.Count(x => x.IsDisliked ?? false);
                        result.Statistics.FavoriteCount = userTracks.Count(x => x.IsFavorite ?? false);
                    }
                    tsw.Stop();
                    timings.Add("stats", tsw.ElapsedMilliseconds);
                }

                if (includes.Contains("comments"))
                {
                    tsw.Restart();
                    var trackComments = DbContext.Comments.Include(x => x.User).Where(x => x.TrackId == track.Id)
                        .OrderByDescending(x => x.CreatedDate).ToArray();
                    if (trackComments.Any())
                    {
                        var comments = new List<Comment>();
                        var commentIds = trackComments.Select(x => x.Id).ToArray();
                        var userCommentReactions = (from cr in DbContext.CommentReactions
                                                    where commentIds.Contains(cr.CommentId)
                                                    select cr).ToArray();
                        foreach (var trackComment in trackComments)
                        {
                            var comment = trackComment.Adapt<Comment>();
                            comment.DatabaseId = trackComment.Id;
                            comment.User = UserList.FromDataUser(trackComment.User,
                                MakeUserThumbnailImage(Configuration, HttpContext, trackComment.User.RoadieId));
                            comment.DislikedCount = userCommentReactions.Count(x =>
                                x.CommentId == trackComment.Id && x.ReactionValue == CommentReaction.Dislike);
                            comment.LikedCount = userCommentReactions.Count(x =>
                                x.CommentId == trackComment.Id && x.ReactionValue == CommentReaction.Like);
                            comments.Add(comment);
                        }

                        result.Comments = comments;
                    }
                    tsw.Stop();
                    timings.Add("comments", tsw.ElapsedMilliseconds);
                }
            }

            sw.Stop();
            Logger.LogInformation($"ByIdAction: Track `{ track }`: includes [{includes.ToCSV()}], timings: [{ timings.ToTimings() }]");
            return Task.FromResult(new OperationResult<Track>
            {
                Data = result,
                IsSuccess = result != null,
                OperationTime = sw.ElapsedMilliseconds
            });
        }
    }
}