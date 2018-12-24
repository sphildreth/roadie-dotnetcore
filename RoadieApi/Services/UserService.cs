﻿using Mapster;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Roadie.Library;
using Roadie.Library.Caching;
using Roadie.Library.Configuration;
using Roadie.Library.Encoding;
using Roadie.Library.Identity;
using Roadie.Library.Imaging;
using Roadie.Library.Models;
using Roadie.Library.Models.Pagination;
using Roadie.Library.Models.Statistics;
using Roadie.Library.Models.Users;
using Roadie.Library.Utility;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Threading.Tasks;
using data = Roadie.Library.Data;

namespace Roadie.Api.Services
{
    public class UserService : ServiceBase, IUserService
    {
        private UserManager<ApplicationUser> UserManager { get; }

        public UserService(IRoadieSettings configuration,
                             IHttpEncoder httpEncoder,
                             IHttpContext httpContext,
                             data.IRoadieDbContext context,
                             ICacheManager cacheManager,
                             ILogger<ArtistService> logger,
                             UserManager<ApplicationUser> userManager)
            : base(configuration, httpEncoder, context, cacheManager, logger, httpContext)
        {
            this.UserManager = userManager;
        }

        public async Task<OperationResult<User>> ById(User user, Guid id)
        {
            var timings = new Dictionary<string, long>();
            var tsw = new Stopwatch();

            var sw = Stopwatch.StartNew();
            sw.Start();
            var cacheKey = string.Format("urn:user_by_id_operation:{0}", id);
            var result = await this.CacheManager.GetAsync<OperationResult<User>>(cacheKey, async () =>
            {
                tsw.Restart();
                var rr = await this.UserByIdAction(id);
                tsw.Stop();
                timings.Add("UserByIdAction", tsw.ElapsedMilliseconds);
                return rr;

            }, ApplicationUser.CacheRegionUrn(id));
            sw.Stop();
            if (result?.Data != null)
            {
                result.Data.Avatar = this.MakeUserThumbnailImage(id);
            }
            timings.Add("operation", sw.ElapsedMilliseconds);
            this.Logger.LogDebug("ById Timings: id [{0}]", id);
            return new OperationResult<User>(result.Messages)
            {
                Data = result?.Data,
                Errors = result?.Errors,
                IsNotFoundResult = result?.IsNotFoundResult ?? false,
                IsSuccess = result?.IsSuccess ?? false,
                OperationTime = sw.ElapsedMilliseconds
            };
        }

        public async Task<OperationResult<bool>> UpdateProfile(User userPerformingUpdate, User userBeingUpdatedModel)
        {
            var user = this.DbContext.Users.FirstOrDefault(x => x.RoadieId == userBeingUpdatedModel.UserId);
            if (user == null)
            {
                return new OperationResult<bool>(true, string.Format("User Not Found [{0}]", userBeingUpdatedModel.UserId));
            }
            if (user.Id != userPerformingUpdate.Id && !userPerformingUpdate.IsAdmin)
            {
                return new OperationResult<bool>
                {
                    Errors = new List<Exception> { new Exception("Access Denied") }
                };
            }
            // Check concurrency stamp
            if(user.ConcurrencyStamp != userBeingUpdatedModel.ConcurrencyStamp)
            {
                return new OperationResult<bool>
                {
                    Errors = new List<Exception> { new Exception("User data is stale.") }
                };
            }
            // Check that username (if changed) doesn't already exist 
            if (user.UserName != userBeingUpdatedModel.UserName)
            {
                var userByUsername = this.DbContext.Users.FirstOrDefault(x => x.NormalizedUserName == userBeingUpdatedModel.UserName.ToUpper());
                if (userByUsername != null)
                {
                    return new OperationResult<bool>
                    {
                        Errors = new List<Exception> { new Exception("Username already in use") }
                    };
                }
            }
            // Check that email (if changed) doesn't already exist
            if (user.Email != userBeingUpdatedModel.Email)
            {
                var userByEmail = this.DbContext.Users.FirstOrDefault(x => x.NormalizedEmail == userBeingUpdatedModel.Email.ToUpper());
                if (userByEmail != null)
                {
                    return new OperationResult<bool>
                    {
                        Errors = new List<Exception> { new Exception("Email already in use") }
                    };
                }
            }
            user.UserName = userBeingUpdatedModel.UserName;
            user.NormalizedUserName = userBeingUpdatedModel.UserName.ToUpper();
            user.Email = userBeingUpdatedModel.Email;
            user.NormalizedEmail = userBeingUpdatedModel.Email.ToUpper();
            user.ApiToken = userBeingUpdatedModel.ApiToken;
            user.Timezone = userBeingUpdatedModel.Timezone;
            user.Timeformat = userBeingUpdatedModel.Timeformat;
            user.PlayerTrackLimit = userBeingUpdatedModel.PlayerTrackLimit;
            user.RandomReleaseLimit = userBeingUpdatedModel.RandomReleaseLimit;
            user.RecentlyPlayedLimit = userBeingUpdatedModel.RecentlyPlayedLimit;
            user.Profile = userBeingUpdatedModel.Profile;
            user.DoUseHtmlPlayer = userBeingUpdatedModel.DoUseHtmlPlayer;
            user.IsPrivate = userBeingUpdatedModel.IsPrivate;
            user.LastUpdated = DateTime.UtcNow;
            user.FtpUrl = userBeingUpdatedModel.FtpUrl;
            user.FtpDirectory = userBeingUpdatedModel.FtpDirectory;
            user.FtpUsername = userBeingUpdatedModel.FtpUsername;
            user.FtpPassword = EncryptionHelper.Encrypt(userBeingUpdatedModel.FtpPassword, user.RoadieId.ToString());
            user.ConcurrencyStamp = Guid.NewGuid().ToString();

            if(!string.IsNullOrEmpty(userBeingUpdatedModel.AvatarData))
            {
                var imageData = ImageHelper.ImageDataFromUrl(userBeingUpdatedModel.AvatarData);
                if(imageData != null)
                {
                    user.Avatar = ImageHelper.ResizeImage(imageData, this.Configuration.ThumbnailImageSize.Width, this.Configuration.ThumbnailImageSize.Height);
                }
            }
            await this.DbContext.SaveChangesAsync();

            if(!string.IsNullOrEmpty(userBeingUpdatedModel.Password) && !string.IsNullOrEmpty(userBeingUpdatedModel.PasswordConfirmation))
            {
                if(userBeingUpdatedModel.Password !=  userBeingUpdatedModel.PasswordConfirmation)
                {
                    return new OperationResult<bool>
                    {
                        Errors = new List<Exception> { new Exception("Password does not match confirmation") }
                    };
                }
                string resetToken = await UserManager.GeneratePasswordResetTokenAsync(user);
                var identityResult = await UserManager.ResetPasswordAsync(user, resetToken, userBeingUpdatedModel.Password);
                if (!identityResult.Succeeded)
                {                    
                    return new OperationResult<bool>
                    {
                        Errors = identityResult.Errors != null ? identityResult.Errors.Select(x => new Exception($"Code [{ x.Code }], Description [{ x.Description }]")) : new List<Exception> { new Exception("Unable to reset password") }
                    };
                }
            }

            this.CacheManager.ClearRegion(ApplicationUser.CacheRegionUrn(user.RoadieId));

            this.Logger.LogInformation($"User `{ userPerformingUpdate }` modifed user `{ userBeingUpdatedModel }`");

            return new OperationResult<bool>
            {
                IsSuccess = true,
                Data = true
            };
        }

        private Task<OperationResult<User>> UserByIdAction(Guid id)
        {
            var user = this.GetUser(id);
            if (user == null)
            {
                return Task.FromResult(new OperationResult<User>(true, string.Format("User Not Found [{0}]", id)));
            }
            return Task.FromResult(new OperationResult<User>
            {
                IsSuccess = true,
                Data = user.Adapt<User>()
            });
        }

        public Task<Library.Models.Pagination.PagedResult<UserList>> List(PagedRequest request)
        {
            var sw = new Stopwatch();
            sw.Start();

            if (!string.IsNullOrEmpty(request.Sort))
            {
                request.Sort = request.Sort.Replace("createdDate", "createdDateTime");
                request.Sort = request.Sort.Replace("lastLogin", "lastUpdatedDateTime");
                request.Sort = request.Sort.Replace("lastApiAccess", "lastApiAccessDateTime");
                request.Sort = request.Sort.Replace("registeredOn", "registeredDateTime");
            }

            var result = (from u in this.DbContext.Users
                          where (request.FilterValue.Length == 0 || (request.FilterValue.Length > 0 && (u.UserName.Contains(request.FilterValue))))
                          select new UserList
                          {
                              DatabaseId = u.Id,
                              Id = u.RoadieId,
                              User = new DataToken
                              {
                                  Text = u.UserName,
                                  Value = u.RoadieId.ToString()
                              },
                              IsEditor = u.UserRoles.Any(x => x.Role.Name == "Editor"),
                              IsPrivate = u.IsPrivate,
                              Thumbnail = this.MakeUserThumbnailImage(u.RoadieId),
                              CreatedDate = u.CreatedDate,
                              LastUpdated = u.LastUpdated,
                              RegisteredDate = u.RegisteredOn,
                              LastLoginDate = u.LastLogin,
                              LastApiAccessDate = u.LastApiAccess
                          });

            UserList[] rows = null;
            var rowCount = result.Count();
            var sortBy = string.IsNullOrEmpty(request.Sort) ? request.OrderValue(new Dictionary<string, string> { { "User.Text", "ASC" } }) : request.OrderValue(null);
            rows = result.OrderBy(sortBy).Skip(request.SkipValue).Take(request.LimitValue).ToArray();

            if (rows.Any())
            {
                foreach (var row in rows)
                {
                    var userArtists = this.DbContext.UserArtists.Include(x => x.Artist).Where(x => x.UserId == row.DatabaseId).ToArray();
                    var userReleases = this.DbContext.UserReleases.Include(x => x.Release).Where(x => x.UserId == row.DatabaseId).ToArray();
                    var userTracks = this.DbContext.UserTracks.Include(x => x.Track).Where(x => x.UserId == row.DatabaseId).ToArray();

                    var mostPlayedArtist = (from a in this.DbContext.Artists
                                            join r in this.DbContext.Releases on a.Id equals r.ArtistId
                                            join rm in this.DbContext.ReleaseMedias on r.Id equals rm.ReleaseId
                                            join t in this.DbContext.Tracks on rm.Id equals t.ReleaseMediaId
                                            join ut in this.DbContext.UserTracks on t.Id equals ut.TrackId
                                            where ut.UserId == row.DatabaseId
                                            select new { a, ut.PlayedCount })
                                             .GroupBy(a => a.a)
                                             .Select(x => new DataToken
                                             {
                                                 Text = x.Key.Name,
                                                 Value = x.Key.RoadieId.ToString(),
                                                 Data = x.Sum(t => t.PlayedCount)
                                             })
                                             .OrderByDescending(x => x.Data)
                                             .FirstOrDefault();

                    var mostPlayedRelease = (from r in this.DbContext.Releases
                                             join rm in this.DbContext.ReleaseMedias on r.Id equals rm.ReleaseId
                                             join t in this.DbContext.Tracks on rm.Id equals t.ReleaseMediaId
                                             join ut in this.DbContext.UserTracks on t.Id equals ut.TrackId
                                             where ut.UserId == row.DatabaseId
                                             select new { r, ut.PlayedCount })
                                             .GroupBy(r => r.r)
                                             .Select(x => new DataToken
                                             {
                                                 Text = x.Key.Title,
                                                 Value = x.Key.RoadieId.ToString(),
                                                 Data = x.Sum(t => t.PlayedCount)
                                             })
                                             .OrderByDescending(x => x.Data)
                                             .FirstOrDefault();

                    var mostPlayedTrack = userTracks
                                          .OrderByDescending(x => x.PlayedCount)
                                          .Select(x => new DataToken
                                          {
                                              Text = x.Track.Title,
                                              Value = x.Track.RoadieId.ToString(),
                                              Data = x.PlayedCount
                                          })
                                          .FirstOrDefault();

                    row.Statistics = new UserStatistics
                    {
                        MostPlayedArtist = mostPlayedArtist,
                        MostPlayedRelease = mostPlayedRelease,
                        MostPlayedTrack = mostPlayedTrack,
                        RatedArtists = userArtists.Where(x => x.Rating > 0).Count(),
                        FavoritedArtists = userArtists.Where(x => x.IsFavorite ?? false).Count(),
                        DislikedArtists = userArtists.Where(x => x.IsDisliked ?? false).Count(),
                        RatedReleases = userReleases.Where(x => x.Rating > 0).Count(),
                        FavoritedReleases = userReleases.Where(x => x.IsFavorite ?? false).Count(),
                        DislikedReleases = userReleases.Where(x => x.IsDisliked ?? false).Count(),
                        RatedTracks = userTracks.Where(x => x.Rating > 0).Count(),
                        PlayedTracks = userTracks.Where(x => x.PlayedCount.HasValue).Select(x => x.PlayedCount).Sum(),
                        FavoritedTracks = userTracks.Where(x => x.IsFavorite ?? false).Count(),
                        DislikedTracks = userTracks.Where(x => x.IsDisliked ?? false).Count()
                    };
                }
            }
            sw.Stop();
            return Task.FromResult(new Library.Models.Pagination.PagedResult<UserList>
            {
                TotalCount = rowCount,
                CurrentPage = request.PageValue,
                TotalPages = (int)Math.Ceiling((double)rowCount / request.LimitValue),
                OperationTime = sw.ElapsedMilliseconds,
                Rows = rows
            });
        }

        public async Task<OperationResult<bool>> SetArtistBookmark(Guid artistId, User roadieUser, bool isBookmarked)
        {
            var user = this.GetUser(roadieUser.UserId);
            if (user == null)
            {
                return new OperationResult<bool>(true, $"Invalid User [{ roadieUser }]");
            }
            var artist = this.GetArtist(artistId);
            if (artist == null)
            {
                return new OperationResult<bool>(true, $"Invalid Artist [{ artistId }]");
            }
            var result = await this.SetBookmark(user, Library.Enums.BookmarkType.Artist, artist.Id, isBookmarked);

            this.CacheManager.ClearRegion(artist.CacheRegion);

            return new OperationResult<bool>
            {
                IsSuccess = true,
                Data = true
            };
        }

        public async Task<OperationResult<bool>> SetArtistFavorite(Guid artistId, User roadieUser, bool isFavorite)
        {
            var user = this.GetUser(roadieUser.UserId);
            if (user == null)
            {
                return new OperationResult<bool>(true, $"Invalid User [{ roadieUser }]");
            }
            return await base.ToggleArtistFavorite(artistId, user, isFavorite);
        }

        public async Task<OperationResult<short>> SetArtistRating(Guid artistId, User roadieUser, short rating)
        {
            var user = this.GetUser(roadieUser.UserId);
            if (user == null)
            {
                return new OperationResult<short>(true, $"Invalid User [{ roadieUser }]");
            }
            return await base.SetArtistRating(artistId, user, rating);
        }

        public async Task<OperationResult<bool>> SetCollectionBookmark(Guid collectionId, User roadieUser, bool isBookmarked)
        {
            var user = this.GetUser(roadieUser.UserId);
            if (user == null)
            {
                return new OperationResult<bool>(true, $"Invalid User [{ roadieUser }]");
            }
            var collection = this.GetCollection(collectionId);
            if (collection == null)
            {
                return new OperationResult<bool>(true, $"Invalid Collection [{ collectionId }]");
            }
            var result = await this.SetBookmark(user, Library.Enums.BookmarkType.Collection, collection.Id, isBookmarked);

            this.CacheManager.ClearRegion(collection.CacheRegion);

            return new OperationResult<bool>
            {
                IsSuccess = true,
                Data = true
            };
        }

        public async Task<OperationResult<bool>> SetLabelBookmark(Guid labelId, User roadieUser, bool isBookmarked)
        {
            var user = this.GetUser(roadieUser.UserId);
            if (user == null)
            {
                return new OperationResult<bool>(true, $"Invalid User [{ roadieUser }]");
            }
            var label = this.GetLabel(labelId);
            if (label == null)
            {
                return new OperationResult<bool>(true, $"Invalid Label [{ labelId }]");
            }
            var result = await this.SetBookmark(user, Library.Enums.BookmarkType.Label, label.Id, isBookmarked);

            this.CacheManager.ClearRegion(label.CacheRegion);

            return new OperationResult<bool>
            {
                IsSuccess = true,
                Data = true
            };
        }

        public async Task<OperationResult<bool>> SetPlaylistBookmark(Guid playlistId, User roadieUser, bool isBookmarked)
        {
            var user = this.GetUser(roadieUser.UserId);
            if (user == null)
            {
                return new OperationResult<bool>(true, $"Invalid User [{ roadieUser }]");
            }
            var playlist = this.GetPlaylist(playlistId);
            if (playlist == null)
            {
                return new OperationResult<bool>(true, $"Invalid Playlist [{ playlistId }]");
            }
            var result = await this.SetBookmark(user, Library.Enums.BookmarkType.Playlist, playlist.Id, isBookmarked);

            this.CacheManager.ClearRegion(playlist.CacheRegion);

            return new OperationResult<bool>
            {
                IsSuccess = true,
                Data = true
            };
        }

        public async Task<OperationResult<bool>> SetReleaseBookmark(Guid releaseid, User roadieUser, bool isBookmarked)
        {
            var user = this.GetUser(roadieUser.UserId);
            if (user == null)
            {
                return new OperationResult<bool>(true, $"Invalid User [{ roadieUser }]");
            }
            var release = this.GetRelease(releaseid);
            if (release == null)
            {
                return new OperationResult<bool>(true, $"Invalid Release [{ releaseid }]");
            }
            var result = await this.SetBookmark(user, Library.Enums.BookmarkType.Release, release.Id, isBookmarked);

            this.CacheManager.ClearRegion(release.CacheRegion);

            return new OperationResult<bool>
            {
                IsSuccess = true,
                Data = true
            };
        }

        public async Task<OperationResult<bool>> SetReleaseFavorite(Guid releaseId, User roadieUser, bool isFavorite)
        {
            var user = this.GetUser(roadieUser.UserId);
            if (user == null)
            {
                return new OperationResult<bool>(true, $"Invalid User [{ roadieUser }]");
            }
            return await base.ToggleReleaseFavorite(releaseId, user, isFavorite);
        }

        public async Task<OperationResult<short>> SetReleaseRating(Guid releaseId, User roadieUser, short rating)
        {
            var user = this.GetUser(roadieUser.UserId);
            if (user == null)
            {
                return new OperationResult<short>(true, $"Invalid User [{ roadieUser }]");
            }
            return await base.SetReleaseRating(releaseId, user, rating);
        }

        public async Task<OperationResult<bool>> SetTrackBookmark(Guid trackId, User roadieUser, bool isBookmarked)
        {
            var user = this.GetUser(roadieUser.UserId);
            if (user == null)
            {
                return new OperationResult<bool>(true, $"Invalid User [{ roadieUser }]");
            }
            var track = this.GetTrack(trackId);
            if (track == null)
            {
                return new OperationResult<bool>(true, $"Invalid Track [{ trackId }]");
            }
            var result = await this.SetBookmark(user, Library.Enums.BookmarkType.Track, track.Id, isBookmarked);

            this.CacheManager.ClearRegion(track.CacheRegion);

            return new OperationResult<bool>
            {
                IsSuccess = true,
                Data = true
            };
        }

        public async Task<OperationResult<short>> SetTrackRating(Guid trackId, User roadieUser, short rating)
        {
            var user = this.GetUser(roadieUser.UserId);
            if (user == null)
            {
                return new OperationResult<short>(true, $"Invalid User [{ roadieUser }]");
            }
            return await base.SetTrackRating(trackId, user, rating);
        }

        private async Task<OperationResult<bool>> SetBookmark(ApplicationUser user, Library.Enums.BookmarkType bookmarktype, int bookmarkTargetId, bool isBookmarked)
        {
            var bookmark = this.DbContext.Bookmarks.FirstOrDefault(x => x.BookmarkTargetId == bookmarkTargetId &&
                                                                        x.BookmarkType == bookmarktype &&
                                                                        x.UserId == user.Id);
            if (isBookmarked)
            {
                // Remove bookmark
                if (bookmark != null)
                {
                    this.DbContext.Bookmarks.Remove(bookmark);
                    await this.DbContext.SaveChangesAsync();
                }
            }
            else
            {
                // Add bookmark
                if (bookmark == null)
                {
                    this.DbContext.Bookmarks.Add(new data.Bookmark
                    {
                        UserId = user.Id,
                        BookmarkTargetId = bookmarkTargetId,
                        BookmarkType = bookmarktype,
                        CreatedDate = DateTime.UtcNow,
                        Status = Library.Enums.Statuses.Ok
                    });
                    await this.DbContext.SaveChangesAsync();
                }
            }

            this.CacheManager.ClearRegion(user.CacheRegion);

            return new OperationResult<bool>
            {
                IsSuccess = true,
                Data = true
            };
        }
    }
}