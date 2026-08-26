using System;
using System.Threading;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// A user data manager that raises the one event this plugin listens to, and refuses
/// every other question loudly.
/// </summary>
/// <remarks>
/// It counts the handlers attached to the event rather than only carrying them, which
/// is what lets a test see that the subscription is taken down again. An unsubscribe
/// that never happened leaves a handler running against a store nobody is using, and
/// nothing about the plugin's own behaviour would show it.
/// </remarks>
internal sealed class AUserDataManagerOf : IUserDataManager
{
    private EventHandler<UserDataSaveEventArgs>? _saved;

    /// <inheritdoc />
    public event EventHandler<UserDataSaveEventArgs>? UserDataSaved
    {
        add => _saved += value;
        remove => _saved -= value;
    }

    /// <summary>
    /// Gets how many handlers are attached to the event right now.
    /// </summary>
    public int Listeners => _saved?.GetInvocationList().Length ?? 0;

    /// <summary>
    /// Raises the event, as the server does when it has saved user data.
    /// </summary>
    /// <param name="args">What to raise it with.</param>
    public void Raise(UserDataSaveEventArgs args) => _saved?.Invoke(this, args);

    private static NotSupportedException Unasked() =>
        new("This plugin listens to one event on a user data manager and asks it nothing.");

    public UserItemData GetUserData(User user, BaseItem item) => throw Unasked();

    public UserItemDataDto GetUserDataDto(BaseItem item, User user) => throw Unasked();

    public UserItemDataDto GetUserDataDto(BaseItem item, BaseItemDto? itemDto, User user, DtoOptions options) => throw Unasked();

    public void SaveUserData(User user, BaseItem item, UserItemData userData, UserDataSaveReason reason, CancellationToken cancellationToken) => throw Unasked();

    public void SaveUserData(User user, BaseItem item, UpdateUserItemDataDto userDataDto, UserDataSaveReason reason) => throw Unasked();

    public bool UpdatePlayState(BaseItem item, UserItemData data, long? reportedPositionTicks) => throw Unasked();
}
