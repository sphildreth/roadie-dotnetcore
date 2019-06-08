﻿using Roadie.Library.Models.Users;
using System.Threading.Tasks;

namespace Roadie.Library.Scrobble
{
    public interface IScrobblerIntegration
    {
        Task<OperationResult<bool>> NowPlaying(User roadieUser, ScrobbleInfo scrobble);

        Task<OperationResult<bool>> Scrobble(User roadieUser, ScrobbleInfo scrobble);
    }
}