using Microsoft.AspNetCore.Authorization;
using TaskTracker.Controllers;
using TaskTracker.Web.Auth;
using Xunit;

namespace TaskTracker.Tests;

/// <summary>
/// Locks in which policy guards each privileged endpoint. Combined with <see cref="TeamAuthorizationTests"/>
/// (which proves the policy denies the wrong roles), this verifies that a caller lacking the required
/// role is refused when hitting these URLs directly.
/// </summary>
public class ControllerAuthorizationTests
{
    private static string? ClassPolicy(Type controller) =>
        controller.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
                  .Cast<AuthorizeAttribute>().Select(a => a.Policy).FirstOrDefault(p => p != null);

    private static string? MethodPolicy(Type controller, string method) =>
        controller.GetMethod(method)!.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
                  .Cast<AuthorizeAttribute>().Select(a => a.Policy).FirstOrDefault(p => p != null);

    [Theory]
    [InlineData(typeof(ApiClientsController),  Policies.TeamAdmin)]
    [InlineData(typeof(WebhooksController),    Policies.TeamAdmin)]
    [InlineData(typeof(ImportController),      Policies.TeamAdmin)]
    [InlineData(typeof(AutomationsController), Policies.TeamAdmin)]
    [InlineData(typeof(SlaController),         Policies.TeamAdmin)]
    [InlineData(typeof(ActivityController),    Policies.SystemAdmin)]
    [InlineData(typeof(MasterController),      Policies.SystemAdmin)]
    [InlineData(typeof(EmailOutboxController), Policies.SystemAdmin)]
    public void AdminControllers_RequireExpectedPolicy(Type controller, string expectedPolicy)
        => Assert.Equal(expectedPolicy, ClassPolicy(controller));

    [Theory]
    [InlineData("Save",         Policies.TeamLead)]
    [InlineData("SetActive",    Policies.TeamLead)]
    [InlineData("Deactivate",   Policies.TeamLead)]
    [InlineData("Complete",     Policies.TeamLead)]
    [InlineData("SaveCapacity", Policies.TeamLead)]
    [InlineData("Delete",       Policies.TeamAdmin)]
    public void SprintMutations_RequireExpectedPolicy(string action, string expectedPolicy)
        => Assert.Equal(expectedPolicy, MethodPolicy(typeof(SprintsController), action));

    [Fact]
    public void SprintReads_StayOpenToMembers()
    {
        // The sprint board (Index) and these read endpoints are intentionally reachable by any
        // team member — no elevated policy. Members get a view-only board; every mutation above
        // stays gated by the TeamLead / TeamAdmin policies.
        Assert.Null(MethodPolicy(typeof(SprintsController), "Index"));
        Assert.Null(MethodPolicy(typeof(SprintsController), "GetIncomplete"));
        Assert.Null(MethodPolicy(typeof(SprintsController), "GetCapacity"));
    }
}
