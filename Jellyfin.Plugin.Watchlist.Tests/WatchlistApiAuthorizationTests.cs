using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// Who may reach an endpoint of this plugin. The answer is the server's, and these
/// tests are about the plugin asking it: every endpoint carries an authorisation
/// attribute, none opts back out, and the policies that reach an endpoint are the
/// ones somebody wrote down.
/// </summary>
/// <remarks>
/// Read by reflection rather than by eye, because the failure this guards against is
/// an endpoint added later. A new action on this controller inherits the attribute at
/// the type; a new controller inherits nothing, and that is the case a person reading
/// the diff of the file they are adding is least likely to catch.
/// </remarks>
public class WatchlistApiAuthorizationTests
{
    /// <summary>
    /// The policies this plugin's endpoints are allowed to name, and it holds one.
    ///
    /// It was empty until #87, and empty was a statement rather than a blank: the
    /// server's default policy is what an endpoint gets from an attribute naming
    /// nothing, and it is the right one for a list a user keeps for themselves. The
    /// entry below is the administrative surface that line said would bring one -
    /// making the shared list and taking it away, which are the only two endpoints
    /// here that demand elevation in their attribute. Anything else appearing in this
    /// set is a permission demanded by accident, and the two are still told apart by
    /// somebody editing this line on purpose.
    /// </summary>
    private static readonly string[] PoliciesTheEndpointsMayName = [Policies.RequiresElevation];

    /// <summary>
    /// The rule. Every endpoint this plugin ships is covered by an authorisation
    /// attribute, from its own method or from the type it sits on.
    /// </summary>
    [Fact]
    public void EveryEndpointCarriesAnAuthorisationAttribute()
    {
        var offenders = Controllers().SelectMany(UnauthorisedEndpointsOf).ToList();

        Assert.True(
            offenders.Count == 0,
            "These endpoints carry no authorisation attribute: " + string.Join(", ", offenders));
    }

    /// <summary>
    /// The other half of the same rule, because an attribute at the type is undone by
    /// one word at the method. An endpoint that opts out of authorisation is reachable
    /// by anybody who can reach the server, and it would read in a diff as a smaller
    /// change than the attribute it cancels.
    /// </summary>
    [Fact]
    public void NoEndpointOptsBackOutOfAuthorisation()
    {
        var offenders = Controllers().SelectMany(AnonymousEndpointsOf).ToList();

        Assert.True(
            offenders.Count == 0,
            "These endpoints allow anonymous callers: " + string.Join(", ", offenders));
    }

    /// <summary>
    /// The policies that actually reach an endpoint, pinned. This is what keeps the
    /// second sentence of the rule true: what requires elevation in this plugin is the
    /// set written above, and the day anything else does, it is a deliberate edit here
    /// rather than a line in a controller nobody read twice.
    /// </summary>
    [Fact]
    public void TheEndpointsNameNoPolicyBeyondTheExpectedSet()
    {
        Assert.Equal(PoliciesTheEndpointsMayName, PoliciesNamedBy(Controllers()));
    }

    /// <summary>
    /// The endpoints that demand elevation in their attribute, pinned by name. The set
    /// above is over POLICIES, so an endpoint losing its attribute while another keeps
    /// the same one moves nothing there - measured by taking the attribute off one of
    /// the two and watching that test stay green. This is the pin that notices.
    /// </summary>
    private static readonly string[] EndpointsThatDemandElevation =
    [
        "WatchlistController.CreateSharedWatchlist",
        "WatchlistController.RemoveSharedWatchlist",
    ];

    /// <summary>
    /// The pin over which endpoints demand elevation, rather than over how many
    /// distinct policies the surface names.
    /// </summary>
    [Fact]
    public void TheEndpointsThatDemandElevationAreTheOnesWrittenDown()
    {
        Assert.Equal(EndpointsThatDemandElevation, EndpointsNamingAPolicyIn(Controllers()));
    }

    /// <summary>
    /// The bite for the pin above, over the fixture controller rather than over the
    /// shipped one, so the reader is watched naming an endpoint.
    /// </summary>
    [Fact]
    public void TheScanNamesTheEndpointThatDemandsAPolicy()
    {
        Assert.Equal(
            ["AControllerThatDemandsElevation.DoSomethingAdministrative"],
            EndpointsNamingAPolicyIn([typeof(AControllerThatDemandsElevation)]));
    }

    /// <summary>
    /// A scan that reads no controllers passes over a plugin whose every endpoint is
    /// open, so what was read is checked rather than trusted.
    /// </summary>
    [Fact]
    public void ThereAreEndpointsToRead()
    {
        Assert.NotEmpty(Controllers().SelectMany(EndpointsOf).ToList());
    }

    /// <summary>
    /// The bite. A controller with no attribute anywhere is what a second controller
    /// added tomorrow looks like, and both its endpoints are named.
    /// </summary>
    [Fact]
    public void TheScanRefusesAControllerThatCarriesNoAttribute()
    {
        Assert.Equal(
            ["AControllerSomebodyMightAdd.GetSomething", "AControllerSomebodyMightAdd.RemoveSomething"],
            UnauthorisedEndpointsOf(typeof(AControllerSomebodyMightAdd)));
    }

    /// <summary>
    /// The one-change neighbour of the fixture above: the same controller with the
    /// attribute at the type and nothing else different. It passes, so the scan is not
    /// one that refuses every controller put in front of it.
    /// </summary>
    [Fact]
    public void TheScanPassesTheSameControllerWithTheAttributeAtTheType()
    {
        Assert.Empty(UnauthorisedEndpointsOf(typeof(AControllerSomebodyMightAddAuthorised)));
    }

    /// <summary>
    /// The near miss, and the one worth spending the effort on. The type is authorised,
    /// so the first scan is satisfied, and one method carries the word that cancels it.
    /// </summary>
    [Fact]
    public void TheScanRefusesTheOneEndpointThatOptsOut()
    {
        Assert.Empty(UnauthorisedEndpointsOf(typeof(AControllerWithOneOpenEndpoint)));
        Assert.Equal(
            ["AControllerWithOneOpenEndpoint.GetSomethingOpenly"],
            AnonymousEndpointsOf(typeof(AControllerWithOneOpenEndpoint)));
    }

    /// <summary>
    /// And the same controller with that one word removed, which is the repair somebody
    /// would make and the run that proves the refusal was about it.
    /// </summary>
    [Fact]
    public void TheScanPassesTheSameControllerWithoutThatOneWord()
    {
        Assert.Empty(AnonymousEndpointsOf(typeof(AControllerWithOneClosedEndpoint)));
    }

    /// <summary>
    /// The policy pin, proven to bite the same way. A controller demanding elevation is
    /// the shape an administrative endpoint arrives in, and the scan reports the policy
    /// rather than passing it.
    /// </summary>
    [Fact]
    public void TheScanReportsAPolicyThatReachesAnEndpoint()
    {
        Assert.Equal(
            [Policies.RequiresElevation],
            PoliciesNamedBy([typeof(AControllerThatDemandsElevation)]));
    }

    /// <summary>
    /// The controllers a server scanning this assembly would add to its route table.
    /// </summary>
    /// <returns>One entry per controller, ordered so a failure reads the same twice.</returns>
    private static IReadOnlyList<Type> Controllers() => PluginUnderTest.Assembly
        .GetTypes()
        .Where(t => t.IsPublic && !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t))
        .OrderBy(t => t.FullName, StringComparer.Ordinal)
        .ToList();

    /// <summary>
    /// The endpoints of one controller: its public instance methods carrying an HTTP
    /// method attribute, which is what makes a method a route rather than a helper.
    /// </summary>
    /// <param name="controller">The controller to read.</param>
    /// <returns>One entry per endpoint.</returns>
    private static IReadOnlyList<MethodInfo> EndpointsOf(Type controller) => controller
        .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
        .Where(method => method.GetCustomAttributes(typeof(HttpMethodAttribute), inherit: true).Length > 0)
        .OrderBy(method => method.Name, StringComparer.Ordinal)
        .ToList();

    /// <summary>
    /// The endpoints of one controller that no authorisation attribute reaches, from
    /// the method or from the type it is declared on.
    /// </summary>
    /// <param name="controller">The controller to read.</param>
    /// <returns>One entry per uncovered endpoint, as controller and method.</returns>
    private static IReadOnlyList<string> UnauthorisedEndpointsOf(Type controller) => EndpointsOf(controller)
        .Where(method => AuthorisationReaching(method).Count == 0)
        .Select(method => NameOf(controller, method))
        .ToList();

    /// <summary>
    /// The endpoints of one controller that a caller with no identity may reach,
    /// whatever else is on them.
    /// </summary>
    /// <param name="controller">The controller to read.</param>
    /// <returns>One entry per open endpoint, as controller and method.</returns>
    private static IReadOnlyList<string> AnonymousEndpointsOf(Type controller) => EndpointsOf(controller)
        .Where(method => method.GetCustomAttributes(typeof(IAllowAnonymous), inherit: true).Length > 0
            || controller.GetCustomAttributes(typeof(IAllowAnonymous), inherit: true).Length > 0)
        .Select(method => NameOf(controller, method))
        .ToList();

    /// <summary>
    /// Every policy named by an attribute that reaches an endpoint of these controllers.
    /// An attribute naming nothing contributes nothing, because that is the default
    /// policy and the default policy is the plugin's whole answer today.
    /// </summary>
    /// <param name="controllers">The controllers to read.</param>
    /// <returns>The policy names, deduplicated and ordered.</returns>
    private static IReadOnlyList<string> PoliciesNamedBy(IReadOnlyList<Type> controllers) => controllers
        .SelectMany(EndpointsOf)
        .SelectMany(AuthorisationReaching)
        .Select(authorisation => authorisation.Policy)
        .Where(policy => !string.IsNullOrEmpty(policy))
        .Select(policy => policy!)
        .Distinct(StringComparer.Ordinal)
        .OrderBy(policy => policy, StringComparer.Ordinal)
        .ToList();

    /// <summary>
    /// The authorisation attributes that reach one endpoint. The type is read as well
    /// as the method, because a controller authorised once at the top is the shape this
    /// plugin uses and reading only the method would call every one of its endpoints
    /// unprotected.
    /// </summary>
    /// <param name="method">The endpoint to read.</param>
    /// <returns>What reaches it, which is empty when nothing does.</returns>
    private static IReadOnlyList<IAuthorizeData> AuthorisationReaching(MethodInfo method) =>
    [
        .. method.GetCustomAttributes(typeof(IAuthorizeData), inherit: true).Cast<IAuthorizeData>(),
        .. method.DeclaringType!.GetCustomAttributes(typeof(IAuthorizeData), inherit: true).Cast<IAuthorizeData>(),
    ];

    /// <summary>
    /// The endpoints of these controllers whose reaching attributes name a policy, as
    /// controller and method. An attribute naming nothing contributes nothing, because
    /// that is the default policy.
    /// </summary>
    /// <param name="controllers">The controllers to read.</param>
    /// <returns>The endpoint names, ordered so a failure reads the same twice.</returns>
    private static IReadOnlyList<string> EndpointsNamingAPolicyIn(IReadOnlyList<Type> controllers) => controllers
        .SelectMany(controller => EndpointsOf(controller).Select(method => (Controller: controller, Method: method)))
        .Where(endpoint => AuthorisationReaching(endpoint.Method)
            .Any(authorisation => !string.IsNullOrEmpty(authorisation.Policy)))
        .Select(endpoint => NameOf(endpoint.Controller, endpoint.Method))
        .OrderBy(name => name, StringComparer.Ordinal)
        .ToList();

    private static string NameOf(Type controller, MethodInfo method) => controller.Name + "." + method.Name;

    /// <summary>
    /// Not shipped. The scan has to have refused something, and this is the shape it
    /// has to refuse: a controller somebody adds beside the one that is authorised,
    /// carrying nothing.
    /// </summary>
    private sealed class AControllerSomebodyMightAdd : ControllerBase
    {
        [HttpGet("Watchlist/Something")]
        public IActionResult GetSomething() => Ok();

        [HttpDelete("Watchlist/Something")]
        public IActionResult RemoveSomething() => Ok();
    }

    /// <summary>
    /// The same controller with the one change that repairs it, so the refusal above is
    /// about the attribute and not about the fixture.
    /// </summary>
    [Authorize]
    private sealed class AControllerSomebodyMightAddAuthorised : ControllerBase
    {
        [HttpGet("Watchlist/Something")]
        public IActionResult GetSomething() => Ok();

        [HttpDelete("Watchlist/Something")]
        public IActionResult RemoveSomething() => Ok();
    }

    /// <summary>
    /// The near miss. Authorised at the type, and one endpoint takes it back.
    /// </summary>
    [Authorize]
    private sealed class AControllerWithOneOpenEndpoint : ControllerBase
    {
        [AllowAnonymous]
        [HttpGet("Watchlist/Something")]
        public IActionResult GetSomethingOpenly() => Ok();

        [HttpGet("Watchlist/SomethingElse")]
        public IActionResult GetSomethingElse() => Ok();
    }

    /// <summary>
    /// The one-change neighbour of the near miss, with that word removed and nothing
    /// else different.
    /// </summary>
    [Authorize]
    private sealed class AControllerWithOneClosedEndpoint : ControllerBase
    {
        [HttpGet("Watchlist/Something")]
        public IActionResult GetSomethingOpenly() => Ok();

        [HttpGet("Watchlist/SomethingElse")]
        public IActionResult GetSomethingElse() => Ok();
    }

    /// <summary>
    /// What an administrative endpoint would look like, so the policy pin is a scan
    /// somebody has watched report a policy rather than an assertion that it would.
    /// </summary>
    [Authorize(Policy = Policies.RequiresElevation)]
    private sealed class AControllerThatDemandsElevation : ControllerBase
    {
        [HttpPost("Watchlist/Administration")]
        public IActionResult DoSomethingAdministrative() => Ok();
    }
}
