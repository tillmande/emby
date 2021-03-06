using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Dto;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Services;
using MediaBrowser.Model.Extensions;

namespace MediaBrowser.Api
{
    /// <summary>
    /// Class GetGameSystemSummaries
    /// </summary>
    [Route("/Games/SystemSummaries", "GET", Summary = "Finds games similar to a given game.")]
    public class GetGameSystemSummaries : IReturn<GameSystemSummary[]>
    {
        /// <summary>
        /// Gets or sets the user id.
        /// </summary>
        /// <value>The user id.</value>
        [ApiMember(Name = "UserId", Description = "Optional. Filter by user id", IsRequired = false, DataType = "string", ParameterType = "query", Verb = "GET")]
        public Guid UserId { get; set; }
    }

    /// <summary>
    /// Class GamesService
    /// </summary>
    [Authenticated]
    public class GamesService : BaseApiService
    {
        /// <summary>
        /// The _user manager
        /// </summary>
        private readonly IUserManager _userManager;

        /// <summary>
        /// The _user data repository
        /// </summary>
        private readonly IUserDataManager _userDataRepository;
        /// <summary>
        /// The _library manager
        /// </summary>
        private readonly ILibraryManager _libraryManager;

        /// <summary>
        /// The _item repo
        /// </summary>
        private readonly IItemRepository _itemRepo;
        /// <summary>
        /// The _dto service
        /// </summary>
        private readonly IDtoService _dtoService;

        private readonly IAuthorizationContext _authContext;

        /// <summary>
        /// Initializes a new instance of the <see cref="GamesService" /> class.
        /// </summary>
        /// <param name="userManager">The user manager.</param>
        /// <param name="userDataRepository">The user data repository.</param>
        /// <param name="libraryManager">The library manager.</param>
        /// <param name="itemRepo">The item repo.</param>
        /// <param name="dtoService">The dto service.</param>
        public GamesService(IUserManager userManager, IUserDataManager userDataRepository, ILibraryManager libraryManager, IItemRepository itemRepo, IDtoService dtoService, IAuthorizationContext authContext)
        {
            _userManager = userManager;
            _userDataRepository = userDataRepository;
            _libraryManager = libraryManager;
            _itemRepo = itemRepo;
            _dtoService = dtoService;
            _authContext = authContext;
        }

        /// <summary>
        /// Gets the specified request.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <returns>System.Object.</returns>
        public object Get(GetGameSystemSummaries request)
        {
            var user = request.UserId == null ? null : _userManager.GetUserById(request.UserId);
            var query = new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[] { typeof(GameSystem).Name },
                DtoOptions = new DtoOptions(false)
                {
                    EnableImages = false
                }
            };

            var result = _libraryManager.GetItemList(query)
                .Cast<GameSystem>()
                .Select(i => GetSummary(i, user))
                .ToArray();

            return ToOptimizedResult(result);
        }

        /// <summary>
        /// Gets the summary.
        /// </summary>
        /// <param name="system">The system.</param>
        /// <param name="user">The user.</param>
        /// <returns>GameSystemSummary.</returns>
        private GameSystemSummary GetSummary(GameSystem system, User user)
        {
            var summary = new GameSystemSummary
            {
                Name = system.GameSystemName,
                DisplayName = system.Name
            };

            var items = user == null ?
                system.GetRecursiveChildren(i => i is Game) :
                system.GetRecursiveChildren(user, new InternalItemsQuery(user)
                {
                    IncludeItemTypes = new[] { typeof(Game).Name },
                    DtoOptions = new DtoOptions(false)
                    {
                        EnableImages = false
                    }
                });

            var games = items.Cast<Game>().ToArray();

            summary.ClientInstalledGameCount = games.Count(i => i.IsPlaceHolder);

            summary.GameCount = games.Length;

            summary.GameFileExtensions = games.Where(i => !i.IsPlaceHolder).Select(i => Path.GetExtension(i.Path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return summary;
        }
    }
}
