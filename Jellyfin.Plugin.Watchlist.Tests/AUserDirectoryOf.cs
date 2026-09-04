using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Jellyfin.Data.Events;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Users;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// A user directory that answers the one question this plugin asks it, out of a table,
/// and refuses every other one loudly.
/// </summary>
/// <remarks>
/// The same shape as <see cref="ALibraryOf"/> and for the same reason. The interface is
/// wide and the completion adapter uses one member of it, so every other member throws
/// rather than returning a default: a call this fake was never meant to answer fails the
/// test that made it instead of passing on an invented value.
/// </remarks>
internal sealed class AUserDirectoryOf : IUserManager
{
    private readonly Dictionary<Guid, User> _users = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="AUserDirectoryOf"/> class.
    /// </summary>
    /// <param name="ids">The users this server knows, by identifier.</param>
    public AUserDirectoryOf(params Guid[] ids)
    {
        ArgumentNullException.ThrowIfNull(ids);

        foreach (var id in ids)
        {
            _users[id] = new User("watcher", "Jellyfin.Server.Implementations.Users.DefaultAuthenticationProvider", "Jellyfin.Server.Implementations.Users.DefaultPasswordResetProvider")
            {
                Id = id,
            };
        }
    }

    /// <summary>
    /// The one user under that identifier, or null where this server does not know them.
    /// </summary>
    /// <param name="id">The identifier.</param>
    /// <returns>The user, or null.</returns>
    public User? GetUserById(Guid id) => _users.GetValueOrDefault(id);

    private static NotSupportedException Unasked() =>
        new("This plugin asks a user directory one question and this is not it.");

    public event EventHandler<GenericEventArgs<User>>? OnUserUpdated { add { } remove { } }

    // The five below are the shape the interface has on the line this project is pinned
    // at. They were absent while the pin sat at 10.11.11, where the same operations are
    // spelled with an identifier and a getter instead of the entity and a property, and
    // moving the pin to the floor the manifest promises is what asked for them. They
    // throw like every other member this plugin never calls.
    public IEnumerable<User> Users => throw Unasked();

    public IEnumerable<Guid> UsersIds => throw Unasked();

    public Task RenameUser(User user, string newName) => throw Unasked();

    public Task ResetPassword(User user) => throw Unasked();

    public Task ChangePassword(User user, string newPassword) => throw Unasked();

    public IEnumerable<User> GetUsers() => throw Unasked();

    public IEnumerable<Guid> GetUsersIds() => throw Unasked();

    public Task InitializeAsync() => throw Unasked();

    public User GetFirstUser() => throw Unasked();

    public User? GetUserByName(string name) => throw Unasked();

    public Task RenameUser(Guid userId, string oldName, string newName) => throw Unasked();

    public Task UpdateUserAsync(User user) => throw Unasked();

    public Task<User> CreateUserAsync(string name) => throw Unasked();

    public Task DeleteUserAsync(Guid userId) => throw Unasked();

    public Task ResetPassword(Guid userId) => throw Unasked();

    public Task ChangePassword(Guid userId, string newPassword) => throw Unasked();

    public UserDto GetUserDto(User user, string? remoteEndPoint = null) => throw Unasked();

    public Task<User?> AuthenticateUser(string username, string password, string remoteEndPoint, bool isUserSession) => throw Unasked();

    public Task<ForgotPasswordResult> StartForgotPasswordProcess(string enteredUsername, bool isInNetwork) => throw Unasked();

    public Task<PinRedeemResult> RedeemPasswordResetPin(string pin) => throw Unasked();

    public NameIdPair[] GetAuthenticationProviders() => throw Unasked();

    public NameIdPair[] GetPasswordResetProviders() => throw Unasked();

    public Task UpdateConfigurationAsync(Guid userId, UserConfiguration config) => throw Unasked();

    public Task UpdatePolicyAsync(Guid userId, UserPolicy policy) => throw Unasked();

    public Task ClearProfileImageAsync(User user) => throw Unasked();
}
