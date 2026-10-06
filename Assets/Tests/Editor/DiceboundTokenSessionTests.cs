using System;
using NUnit.Framework;

public class DiceboundTokenSessionTests
{
    private object _session;
    private Type _type;

    [SetUp]
    public void SetUp()
    {
        _type = Type.GetType("TokenSessionState, Assembly-CSharp", true);
        _session = Activator.CreateInstance(_type);
    }

    private bool Issue(ulong id, bool gm = false, bool copy = false) =>
        (bool)_type.GetMethod("IssueHero").Invoke(_session, new object[] { id, gm, copy });

    [Test]
    public void EachPlayerGetsOnlyOneHero()
    {
        Assert.That(Issue(10), Is.True);
        Assert.That(Issue(10), Is.False);
        Assert.That(Issue(11), Is.True);
    }

    [Test]
    public void CopiesAndGameMasterTokensAreNotHeroes()
    {
        Assert.That(Issue(0, gm: true), Is.False);
        Assert.That(Issue(10, copy: true), Is.False);
        Assert.That(Issue(10), Is.True, "A copy does not consume the first manually created hero.");
        Assert.That(Issue(10, copy: true), Is.False);
    }

    [Test]
    public void ReconnectionDoesNotIssueAnotherHeroEvenIfOriginalWasDeleted()
    {
        Issue(10);
        _type.GetMethod("ReassignPlayer").Invoke(_session, new object[] { 10UL, 20UL });
        Assert.That(Issue(20), Is.False);
        _type.GetMethod("ReassignPlayer").Invoke(_session, new object[] { 20UL, 30UL });
        Assert.That(Issue(30), Is.False);
    }

    [Test]
    public void ReconnectionBeforeFirstHeroStillAllowsFirstHero()
    {
        _type.GetMethod("ReassignPlayer").Invoke(_session, new object[] { 10UL, 20UL });
        Assert.That(Issue(20), Is.True);
    }

    [Test]
    public void NewLobbyResetsHeroIssuance()
    {
        Issue(10);
        _type.GetMethod("Clear").Invoke(_session, null);
        Assert.That(Issue(10), Is.True);
    }
}
