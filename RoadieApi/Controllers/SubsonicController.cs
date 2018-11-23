﻿using Mapster;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Roadie.Api.ModelBinding;
using Roadie.Api.Services;
using Roadie.Library.Caching;
using Roadie.Library.Extensions;
using Roadie.Library.Identity;
using Roadie.Library.Models.ThirdPartyApi.Subsonic;
using System;
using System.Net;
using System.Threading.Tasks;

namespace Roadie.Api.Controllers
{
    [Route("subsonic/rest")]
    [ApiController]
    public class SubsonicController : EntityControllerBase
    {
        private IPlayActivityService PlayActivityService { get; }
        private ISubsonicService SubsonicService { get; }

        /// <summary>
        /// This is the user authenticated via the Subsonic methods - NOT the current API Identity User.
        /// </summary>
        private Library.Models.Users.User SubsonicUser { get; set; }

        private ITrackService TrackService { get; }

        public SubsonicController(ISubsonicService subsonicService, ITrackService trackService, IPlayActivityService playActivityService, ILoggerFactory logger, ICacheManager cacheManager, IConfiguration configuration, UserManager<ApplicationUser> userManager)
            : base(cacheManager, configuration, userManager)
        {
            this.Logger = logger.CreateLogger("RoadieApi.Controllers.SubsonicController");
            this.SubsonicService = subsonicService;
            this.TrackService = trackService;
            this.PlayActivityService = playActivityService;
        }

        [HttpGet("getAlbum.view")]
        [HttpPost("getAlbum.view")]
        [ProducesResponseType(200)]
        public async Task<IActionResult> GetAlbum(SubsonicRequest request)
        {
            var authResult = await this.AuthenticateUser(request);
            if (authResult != null)
            {
                return authResult;
            }
            var result = await this.SubsonicService.GetAlbum(request, this.SubsonicUser);
            return this.BuildResponse(request, result, "album");
        }

        [HttpGet("getAlbumInfo.view")]
        [HttpPost("getAlbumInfo.view")]
        [ProducesResponseType(200)]
        public async Task<IActionResult> GetAlbumInfo(SubsonicRequest request)
        {
            var authResult = await this.AuthenticateUser(request);
            if (authResult != null)
            {
                return authResult;
            }
            var result = await this.SubsonicService.GetAlbumInfo(request, this.SubsonicUser, AlbumInfoVersion.One);
            return this.BuildResponse(request, result, "albumInfo");
        }

        [HttpGet("getAlbumInfo2.view")]
        [HttpPost("getAlbumInfo2.view")]
        [ProducesResponseType(200)]
        public async Task<IActionResult> GetAlbumInfo2(SubsonicRequest request)
        {
            var authResult = await this.AuthenticateUser(request);
            if (authResult != null)
            {
                return authResult;
            }
            var result = await this.SubsonicService.GetAlbumInfo(request, this.SubsonicUser, AlbumInfoVersion.Two);
            return this.BuildResponse(request, result, "albumInfo");
        }

        [HttpGet("getAlbumList.view")]
        [HttpPost("getAlbumList.view")]
        [ProducesResponseType(200)]
        public async Task<IActionResult> GetAlbumList(SubsonicRequest request)
        {
            var authResult = await this.AuthenticateUser(request);
            if (authResult != null)
            {
                return authResult;
            }
            var result = await this.SubsonicService.GetAlbumList(request, this.SubsonicUser, AlbumListVersions.One);
            return this.BuildResponse(request, result, "albumList");
        }

        [HttpGet("getAlbumList2.view")]
        [HttpPost("getAlbumList2.view")]
        [ProducesResponseType(200)]
        public async Task<IActionResult> GetAlbumList2(SubsonicRequest request)
        {
            var authResult = await this.AuthenticateUser(request);
            if (authResult != null)
            {
                return authResult;
            }
            var result = await this.SubsonicService.GetAlbumList(request, this.SubsonicUser, AlbumListVersions.Two);
            return this.BuildResponse(request, result, "albumList");
        }

        [HttpGet("getArtist.view")]
        [HttpPost("getArtist.view")]
        [ProducesResponseType(200)]
        public async Task<IActionResult> GetArtist(SubsonicRequest request)
        {
            var authResult = await this.AuthenticateUser(request);
            if (authResult != null)
            {
                return authResult;
            }
            var result = await this.SubsonicService.GetArtist(request, this.SubsonicUser);
            return this.BuildResponse(request, result, "artist");
        }

        [HttpGet("getArtistInfo.view")]
        [HttpPost("getArtistInfo.view")]
        [ProducesResponseType(200)]
        public async Task<IActionResult> GetArtistInfo(SubsonicRequest request, int? count, bool? includeNotPresent)
        {
            var authResult = await this.AuthenticateUser(request);
            if (authResult != null)
            {
                return authResult;
            }
            var result = await this.SubsonicService.GetArtistInfo(request, count, includeNotPresent ?? false, ArtistInfoVersion.One);
            return this.BuildResponse(request, result, "artistInfo");
        }

        [HttpGet("getArtistInfo2.view")]
        [HttpPost("getArtistInfo2.view")]
        [ProducesResponseType(200)]
        public async Task<IActionResult> GetArtistInfo2(SubsonicRequest request, int? count, bool? includeNotPresent)
        {
            var authResult = await this.AuthenticateUser(request);
            if (authResult != null)
            {
                return authResult;
            }
            var result = await this.SubsonicService.GetArtistInfo(request, count, includeNotPresent ?? false, ArtistInfoVersion.Two);
            return this.BuildResponse(request, result, "artistInfo2");
        }

        [HttpGet("getArtists.view")]
        [HttpPost("getArtists.view")]
        [ProducesResponseType(200)]
        public async Task<IActionResult> GetArtists(SubsonicRequest request)
        {
            var authResult = await this.AuthenticateUser(request);
            if (authResult != null)
            {
                return authResult;
            }
            var result = await this.SubsonicService.GetArtists(request, this.SubsonicUser);
            return this.BuildResponse(request, result, "artists");
        }

        [HttpGet("getAvatar.view")]
        [HttpPost("getAvatar.view")]
        [ProducesResponseType(200)]
        public async Task<IActionResult> GetAvatar(SubsonicRequest request, string username)
        {
            var user = await this.UserManager.FindByNameAsync(username);
            return Redirect($"/images/user/{ user.RoadieId }/{this.RoadieSettings.ThumbnailImageSize.Width}/{this.RoadieSettings.ThumbnailImageSize.Height}");
        }

        [HttpGet("getCoverArt.view")]
        [HttpPost("getCoverArt.view")]
        [ProducesResponseType(200)]
        public async Task<IActionResult> GetCoverArt(SubsonicRequest request, int? size)
        {
            var result = await this.SubsonicService.GetCoverArt(request, size);
            if (result == null || result.IsNotFoundResult)
            {
                return NotFound();
            }
            if (!result.IsSuccess)
            {
                this.Logger.LogWarning($"GetCoverArt Failed For [{ JsonConvert.SerializeObject(request) }]");
                return StatusCode((int)HttpStatusCode.InternalServerError);
            }
            return File(fileContents: result.Data.Bytes,
                        contentType: result.ContentType,
                        fileDownloadName: $"{ result.Data.Caption ?? request.id.ToString()}.jpg",
                        lastModified: result.LastModified,
                        entityTag: result.ETag);
        }

        [HttpGet("getGenres.view")]
        [HttpPost("getGenres.view")]
        [ProducesResponseType(200)]
        public async Task<IActionResult> GetGenres(SubsonicRequest request)
        {
            var result = await this.SubsonicService.GetGenres(request);
            return this.BuildResponse(request, result, "genres");
        }

        [HttpGet("getIndexes.view")]
        [HttpPost("getIndexes.view")]
        [ProducesResponseType(200)]
        public async Task<IActionResult> GetIndexes(SubsonicRequest request, long? ifModifiedSince = null)
        {
            var authResult = await this.AuthenticateUser(request);
            if (authResult != null)
            {
                return authResult;
            }
            var result = await this.SubsonicService.GetIndexes(request, this.SubsonicUser, ifModifiedSince);
            return this.BuildResponse(request, result, "indexes");
        }

        [HttpGet("getLicense.view")]
        [HttpPost("getLicense.view")]
        [ProducesResponseType(200)]
        public IActionResult GetLicense(SubsonicRequest request)
        {
            var result = this.SubsonicService.GetLicense(request);
            return this.BuildResponse(request, result, "license");
        }

        [HttpGet("getLyrics.view")]
        [HttpPost("getLyrics.view")]
        [ProducesResponseType(200)]
        public IActionResult GetLyrics(SubsonicRequest request, string artist, string title)
        {
            var result = this.SubsonicService.GetLyrics(request, artist, title);
            return this.BuildResponse(request, result, "lyrics ");
        }

        [HttpGet("getMusicDirectory.view")]
        [HttpPost("getMusicDirectory.view")]
        [ProducesResponseType(200)]
        public async Task<IActionResult> GetMusicDirectory(SubsonicRequest request)
        {
            var authResult = await this.AuthenticateUser(request);
            if (authResult != null)
            {
                return authResult;
            }
            var result = await this.SubsonicService.GetMusicDirectory(request, this.SubsonicUser);
            return this.BuildResponse(request, result, "directory");
        }

        [HttpGet("getMusicFolders.view")]
        [HttpPost("getMusicFolders.view")]
        [ProducesResponseType(200)]
        public async Task<IActionResult> GetMusicFolders(SubsonicRequest request)
        {
            var result = await this.SubsonicService.GetMusicFolders(request);
            return this.BuildResponse(request, result, "musicFolders");
        }

        [HttpGet("getPlaylist.view")]
        [HttpPost("getPlaylist.view")]
        [ProducesResponseType(200)]
        public async Task<IActionResult> GetPlaylist(SubsonicRequest request)
        {
            var authResult = await this.AuthenticateUser(request);
            if (authResult != null)
            {
                return authResult;
            }
            var result = await this.SubsonicService.GetPlaylist(request, this.SubsonicUser);
            return this.BuildResponse(request, result, "playlist");
        }

        [HttpGet("getPlaylists.view")]
        [HttpPost("getPlaylists.view")]
        [ProducesResponseType(200)]
        public async Task<IActionResult> GetPlaylists(SubsonicRequest request, string username)
        {
            var result = await this.SubsonicService.GetPlaylists(request, this.SubsonicUser, username);
            return this.BuildResponse(request, result, "playlists");
        }

        [HttpGet("getPodcasts.view")]
        [HttpPost("getPodcasts.view")]
        [ProducesResponseType(200)]
        public async Task<IActionResult> GetPodcasts(SubsonicRequest request, bool includeEpisodes)
        {
            var result = await this.SubsonicService.GetPodcasts(request);
            return this.BuildResponse(request, result, "podcasts");
        }

        [HttpGet("getRandomSongs.view")]
        [HttpPost("getRandomSongs.view")]
        [ProducesResponseType(200)]
        public async Task<IActionResult> GetRandomSongs(SubsonicRequest request)
        {
            var authResult = await this.AuthenticateUser(request);
            if (authResult != null)
            {
                return authResult;
            }
            var result = await this.SubsonicService.GetRandomSongs(request, this.SubsonicUser);
            return this.BuildResponse(request, result, "randomSongs");
        }

        [HttpGet("getSimilarSongs.view")]
        [HttpPost("getSimilarSongs.view")]
        [ProducesResponseType(200)]
        public async Task<IActionResult> GetSimilarSongs(SubsonicRequest request, int? count = 50)
        {
            var authResult = await this.AuthenticateUser(request);
            if (authResult != null)
            {
                return authResult;
            }
            var result = await this.SubsonicService.GetSimliarSongs(request, this.SubsonicUser, SimilarSongsVersion.One, count);
            return this.BuildResponse(request, result, "similarSongs");
        }

        [HttpGet("getSimilarSongs2.view")]
        [HttpPost("getSimilarSongs2.view")]
        [ProducesResponseType(200)]
        public async Task<IActionResult> GetSimilarSongs2(SubsonicRequest request, int? count = 50)
        {
            var authResult = await this.AuthenticateUser(request);
            if (authResult != null)
            {
                return authResult;
            }
            var result = await this.SubsonicService.GetSimliarSongs(request, this.SubsonicUser, SimilarSongsVersion.Two, count);
            return this.BuildResponse(request, result, "similarSongs2");
        }

        [HttpGet("getSong.view")]
        [HttpPost("getSong.view")]
        [ProducesResponseType(200)]
        public async Task<IActionResult> GetSong(SubsonicRequest request)
        {
            var authResult = await this.AuthenticateUser(request);
            if (authResult != null)
            {
                return authResult;
            }
            var result = await this.SubsonicService.GetSong(request, this.SubsonicUser);
            return this.BuildResponse(request, result, "song");
        }

        [HttpGet("getSongsByGenre.view")]
        [HttpPost("getSongsByGenre.view")]
        [ProducesResponseType(200)]
        public async Task<IActionResult> GetSongsByGenre(SubsonicRequest request)
        {
            var authResult = await this.AuthenticateUser(request);
            if (authResult != null)
            {
                return authResult;
            }
            var result = await this.SubsonicService.GetSongsByGenre(request, this.SubsonicUser);
            return this.BuildResponse(request, result, "songsByGenre");
        }

        [HttpGet("getStarred.view")]
        [HttpPost("getStarred.view")]
        [ProducesResponseType(200)]
        public async Task<IActionResult> GetStarred(SubsonicRequest request)
        {
            var authResult = await this.AuthenticateUser(request);
            if (authResult != null)
            {
                return authResult;
            }
            var result = await this.SubsonicService.GetStarred(request, this.SubsonicUser, StarredVersion.One);
            return this.BuildResponse(request, result, "starred");
        }

        [HttpGet("getStarred2.view")]
        [HttpPost("getStarred2.view")]
        [ProducesResponseType(200)]
        public async Task<IActionResult> GetStarred2(SubsonicRequest request)
        {
            var authResult = await this.AuthenticateUser(request);
            if (authResult != null)
            {
                return authResult;
            }
            var result = await this.SubsonicService.GetStarred(request, this.SubsonicUser, StarredVersion.Two);
            return this.BuildResponse(request, result, "starred");
        }

        [HttpGet("getTopSongs.view")]
        [HttpPost("getTopSongs.view")]
        [ProducesResponseType(200)]
        public async Task<IActionResult> GetTopSongs(SubsonicRequest request, string artist, int? count = 50)
        {
            var authResult = await this.AuthenticateUser(request);
            if (authResult != null)
            {
                return authResult;
            }
            var result = await this.SubsonicService.GetTopSongs(request, this.SubsonicUser, artist, count);
            return this.BuildResponse(request, result, "topSongs");
        }

        [HttpGet("getUser.view")]
        [HttpPost("getUser.view")]
        [ProducesResponseType(200)]
        public async Task<IActionResult> GetUser(SubsonicRequest request, string username)
        {
            var authResult = await this.AuthenticateUser(request);
            if (authResult != null)
            {
                return authResult;
            }
            var result = await this.SubsonicService.GetUser(request, username ?? request.u);
            return this.BuildResponse(request, result, "user");
        }

        [HttpGet("getVideos.view")]
        [HttpPost("getVideos.view")]
        [ProducesResponseType(200)]
        public IActionResult GetVideos(SubsonicRequest request)
        {
            var result = this.SubsonicService.GetVideos(request);
            return this.BuildResponse(request, result, "videos");
        }

        [HttpGet("ping.view")]
        [HttpPost("ping.view")]
        [ProducesResponseType(200)]
        public IActionResult Ping(SubsonicRequest request)
        {
            if (request.IsJSONRequest)
            {
                var result = this.SubsonicService.Ping(request);
                return this.BuildResponse(request, result);
            }
            return Content("<subsonic-response xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns=\"http://subsonic.org/restapi\" status=\"ok\" version=\"1.16.0\" />", "application/xml");
        }

        /// <summary>
        /// Returns albums, artists and songs matching the given search criteria. Supports paging through the result.
        /// </summary>
        [HttpGet("search.view")]
        [HttpPost("search.view")]
        [ProducesResponseType(200)]
        public async Task<IActionResult> Search(SubsonicRequest request)
        {
            var authResult = await this.AuthenticateUser(request);
            if (authResult != null)
            {
                return authResult;
            }
            var result = await this.SubsonicService.Search(request, this.SubsonicUser, SearchVersion.One);
            return this.BuildResponse(request, result, "searchResult");
        }

        [HttpGet("search2.view")]
        [HttpPost("search2.view")]
        [ProducesResponseType(200)]
        public async Task<IActionResult> Search2(SubsonicRequest request)
        {
            var authResult = await this.AuthenticateUser(request);
            if (authResult != null)
            {
                return authResult;
            }
            var result = await this.SubsonicService.Search(request, this.SubsonicUser, SearchVersion.Two);
            return this.BuildResponse(request, result, "searchResult2");
        }

        [HttpGet("search3.view")]
        [HttpPost("search3.view")]
        [ProducesResponseType(200)]
        public async Task<IActionResult> Search3(SubsonicRequest request)
        {
            var authResult = await this.AuthenticateUser(request);
            if (authResult != null)
            {
                return authResult;
            }
            var result = await this.SubsonicService.Search(request, this.SubsonicUser, SearchVersion.Three);
            return this.BuildResponse(request, result, "searchResult3");
        }

        [HttpGet("stream.view")]
        [HttpPost("stream.view")]
        [ProducesResponseType(200)]
        public async Task<FileStreamResult> StreamTrack(SubsonicRequest request)
        {
            var authResult = await this.AuthenticateUser(request);
            if (authResult != null)
            {
                Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            }
            var trackId = request.TrackId;
            if (trackId == null)
            {
                Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            }
            return await base.StreamTrack(trackId.Value, this.TrackService, this.PlayActivityService, this.SubsonicUser);
        }

        private async Task<IActionResult> AuthenticateUser(SubsonicRequest request)
        {
            var appUser = await this.SubsonicService.Authenticate(request);
            if (!(appUser?.IsSuccess ?? false) || (appUser?.IsNotFoundResult ?? false))
            {
                return this.BuildResponse(request, appUser.Adapt<SubsonicOperationResult<Response>>());
            }
            this.SubsonicUser = this.UserModelForUser(appUser.Data.User);
            return null;
        }

        #region Response Builder Methods

        private IActionResult BuildResponse(SubsonicRequest request, SubsonicOperationResult<Response> response = null, string responseType = null)
        {
            var acceptHeader = this.Request.Headers["Accept"];
            this.Logger.LogTrace($"Subsonic Request: Method [{ this.Request.Method }], Accept Header [{ acceptHeader }], Path [{ this.Request.Path }], Query String [{ this.Request.QueryString }], Response Error Code [{ response.ErrorCode }], Request [{ JsonConvert.SerializeObject(request) }] ResponseType [{ responseType }]");
            if (response?.ErrorCode.HasValue ?? false)
            {
                return this.SendError(request, response);
            }
            if (request.IsJSONRequest)
            {
                this.Response.ContentType = "application/json";
                var status = response?.Data?.status.ToString();
                var version = response?.Data?.version ?? Roadie.Api.Services.SubsonicService.SubsonicVersion;
                string jsonResult = null;
                if (responseType == null)
                {
                    jsonResult = "{ \"subsonic-response\": { \"status\":\"" + status + "\", \"version\": \"" + version + "\" }}";
                }
                else
                {
                    jsonResult = "{ \"subsonic-response\": { \"status\":\"" + status + "\", \"version\": \"" + version + "\", \"" + responseType + "\":" + (response?.Data != null ? JsonConvert.SerializeObject(response.Data.Item) : string.Empty) + "}}";
                }
                if ((request?.f ?? string.Empty).Equals("jsonp", StringComparison.OrdinalIgnoreCase))
                {
                    jsonResult = request.callback + "(" + jsonResult + ");";
                }
                return Content(jsonResult);
            }
            this.Response.ContentType = "application/xml";
            return Ok(response.Data);
        }

        private IActionResult SendError(SubsonicRequest request, SubsonicOperationResult<Response> response = null)
        {
            var version = response?.Data?.version ?? Roadie.Api.Services.SubsonicService.SubsonicVersion;
            string errorDescription = response?.ErrorCode?.DescriptionAttr();
            int? errorCode = (int?)response?.ErrorCode;
            if (request.IsJSONRequest)
            {
                this.Response.ContentType = "application/json";
                return Content("{ \"subsonic-response\": { \"status\":\"failed\", \"version\": \"" + version + "\", \"error\":{\"code\":\"" + errorCode + "\",\"message\":\"" + errorDescription + "\"}}}");
            }
            this.Response.ContentType = "application/xml";
            return Content($"<?xml version=\"1.0\" encoding=\"UTF-8\"?><subsonic-response xmlns=\"http://subsonic.org/restapi\" status=\"failed\" version=\"{ version }\"><error code=\"{ errorCode }\" message=\"{ errorDescription }\"/></subsonic-response>");
        }

        #endregion Response Builder Methods
    }
}