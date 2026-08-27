using System;
using System.Threading.Tasks;
using Jellyfin.Data.Events.Users;
using MediaBrowser.Controller.Events;

namespace Jellyfin.Plugin.Watchlist.Users;

/// <summary>
/// How the server tells this plugin that a user is gone.
/// </summary>
/// <remarks>
/// <para>
/// A consumer rather than a subscription to an event on a manager, because the two
/// supported server lines raise this one through the event manager and expose no such
/// event on the user manager. The server resolves every consumer of a type from its
/// own container when it publishes, so registering this is what attaches it and there
/// is nothing to detach: it lives and dies with the container the plugin was
/// registered into.
/// </para>
/// <para>
/// It translates and nothing more. The rule is <see cref="DeletedUserHandler"/>'s, so
/// what the rule sees is one identifier whether the server raised the event or a test
/// called the handler.
/// </para>
/// </remarks>
public sealed class UserDeletedSubscription : IEventConsumer<UserDeletedEventArgs>
{
    private readonly DeletedUserHandler _handler;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserDeletedSubscription"/> class.
    /// </summary>
    /// <param name="handler">What decides what happens to the deleted user's list.</param>
    public UserDeletedSubscription(DeletedUserHandler handler)
    {
        _handler = handler;
    }

    /// <inheritdoc />
    public Task OnEvent(UserDeletedEventArgs eventArgs)
    {
        ArgumentNullException.ThrowIfNull(eventArgs);

        _handler.Handle(eventArgs.Argument.Id);

        return Task.CompletedTask;
    }
}
